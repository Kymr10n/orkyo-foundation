using System.Text.Json;
using Api.Constants;
using Api.Helpers;
using Api.Models;
using Api.Repositories;

namespace Api.Services;

public interface IRoutingService
{
    Task<List<RoutingInfo>> GetAllAsync(CancellationToken ct = default);
    Task<RoutingInfo?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<RoutingInfo> CreateAsync(CreateRoutingRequest request, CancellationToken ct = default);
    Task<RoutingInfo?> UpdateAsync(Guid id, UpdateRoutingRequest request, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Makes a work order from a routing: a container request with one leaf per step and the
    /// finish-to-start chain between them, in one transaction.
    /// </summary>
    Task<InstantiateRoutingResponse> InstantiateAsync(Guid routingId, InstantiateRoutingRequest request, CancellationToken ct = default);
}

/// <summary>
/// The cross-entity rules a routing has to satisfy. Shape (names, numbers, step order) is the
/// validators' job at the boundary; what is enforced here is that every step's operation is a
/// request template, and — at instantiation — one that targets a resource type, because a
/// step scheduled onto nothing is not an operation.
/// </summary>
public class RoutingService(
    IRoutingRepository routings,
    ITemplateRepository templates,
    IRequestRepository requests,
    IRequestService requestService) : IRoutingService
{
    public Task<List<RoutingInfo>> GetAllAsync(CancellationToken ct = default) => routings.GetAllAsync(ct);

    public Task<RoutingInfo?> GetByIdAsync(Guid id, CancellationToken ct = default) => routings.GetByIdAsync(id, ct);

    public async Task<RoutingInfo> CreateAsync(CreateRoutingRequest request, CancellationToken ct = default)
    {
        await EnsureOperationsAsync(request.Steps, ct);
        return await routings.CreateAsync(request, ct);
    }

    public async Task<RoutingInfo?> UpdateAsync(Guid id, UpdateRoutingRequest request, CancellationToken ct = default)
    {
        await EnsureOperationsAsync(request.Steps, ct);
        return await routings.UpdateAsync(id, request, ct);
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default) => routings.DeleteAsync(id, ct);

    public async Task<InstantiateRoutingResponse> InstantiateAsync(
        Guid routingId, InstantiateRoutingRequest request, CancellationToken ct = default)
    {
        var routing = await routings.GetByIdAsync(routingId, ct)
            ?? throw new NotFoundException("Routing", routingId);

        if (request.ParentRequestId is { } parentId)
            await requestService.EnsureCanParentAsync(parentId, ct);

        var children = new List<CreateRequestRequest>(routing.Steps.Count);
        foreach (var step in routing.Steps)
        {
            var operation = await templates.GetByIdAsync(step.OperationTemplateId, ct)
                ?? throw new ConflictException($"Step {step.StepNo} names an operation that no longer exists");
            if (operation.TargetResourceTypeKeys.Count == 0)
                throw new ConflictException(
                    $"Step {step.StepNo} ('{operation.Name}') targets no resource type, so it cannot be scheduled");

            var items = await templates.GetTemplateItemsAsync(operation.Id, ct);

            children.Add(new CreateRequestRequest
            {
                Name = $"{step.StepNo}. {operation.Name}",
                ParentRequestId = null, // set by the chain write
                PlanningMode = PlanningMode.Leaf,
                SortOrder = step.StepNo,
                SiteId = request.SiteId,
                EarliestStartTs = request.EarliestStartTs,
                LatestEndTs = request.LatestEndTs,
                // Setup once, run time per unit: the part-specific figures live on the step.
                MinimalDurationValue = step.SetupMinutes + step.RunMinutesPerUnit * request.Quantity,
                MinimalDurationUnit = DurationUnit.Minutes,
                TargetResourceTypeKeys = operation.TargetResourceTypeKeys,
                Requirements = items.Select(i => new CreateRequestRequirementRequest
                {
                    CriterionId = i.CriterionId,
                    Value = JsonDocument.Parse(i.Value).RootElement.Clone(),
                }).ToList(),
            });
        }

        // The job itself: a container carrying the window, which nothing propagates server-side,
        // so the same window is copied onto every leaf above. It targets nothing — a container
        // holds no assignments of its own.
        var parent = new CreateRequestRequest
        {
            Name = request.Name,
            Description = routing.Name,
            ParentRequestId = request.ParentRequestId,
            PlanningMode = PlanningMode.Container,
            SiteId = request.SiteId,
            EarliestStartTs = request.EarliestStartTs,
            LatestEndTs = request.LatestEndTs,
            MinimalDurationValue = Math.Max(1, children.Sum(c => c.MinimalDurationValue)),
            MinimalDurationUnit = DurationUnit.Minutes,
            TargetResourceTypeKeys = [],
        };

        var edges = routing.Steps
            .Take(routing.Steps.Count - 1)
            .Select((step, i) => new ChainEdge(i, i + 1, step.LagMinutesAfter))
            .ToList();

        var (created, childIds) = await requests.CreateChainAsync(parent, children, edges, ct);
        return new InstantiateRoutingResponse(created, childIds);
    }

    /// <summary>Every step's operation must exist and be a request template.</summary>
    private async Task EnsureOperationsAsync(IReadOnlyList<RoutingStepRequest> steps, CancellationToken ct)
    {
        foreach (var step in steps)
        {
            var operation = await templates.GetByIdAsync(step.OperationTemplateId, ct)
                ?? throw new NotFoundException("Template", step.OperationTemplateId);
            if (operation.EntityType != TemplateEntityTypes.Request)
                throw new ConflictException(
                    $"Step {step.StepNo} ('{operation.Name}') is a {operation.EntityType} template, not an operation");
        }
    }
}
