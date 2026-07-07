using Api.Helpers;
using Api.Models;
using Api.Repositories;

namespace Api.Services;

/// <summary>
/// Service layer for scheduling requests — the units of demand in the system.
/// Enforces tree integrity rules (no cycles, no leaf children) and delegates
/// persistence to <see cref="IRequestRepository"/>.
/// </summary>
public interface IRequestService
{
    /// <summary>Returns all requests. Pass <c>includeRequirements: true</c> to populate requirement lists.</summary>
    Task<List<RequestInfo>> GetAllAsync(bool includeRequirements = false, CancellationToken ct = default);
    /// <summary>Returns a page of requests.</summary>
    Task<PagedResult<RequestInfo>> GetAllAsync(PageRequest page, bool includeRequirements = false, CancellationToken ct = default);
    /// <summary>Returns the request with the given ID, or <c>null</c> if not found.</summary>
    Task<RequestInfo?> GetByIdAsync(Guid id, bool includeRequirements = true, CancellationToken ct = default);
    /// <summary>Bulk fetch by ids (used by the conflicted-requests filter).</summary>
    Task<List<RequestInfo>> GetByIdsAsync(IReadOnlyList<Guid> ids, bool includeRequirements = true, CancellationToken ct = default);
    /// <summary>Scheduled requests for one site whose bar overlaps [from,to] — the scoped grid feed.</summary>
    Task<List<RequestInfo>> GetScheduledBySiteWindowAsync(Guid siteId, DateTime from, DateTime to, CancellationToken ct = default);
    /// <summary>Unscheduled, directly-schedulable (leaf) backlog (tenant-wide) — drag-to-schedule source. Groups are excluded.</summary>
    Task<List<RequestInfo>> GetUnscheduledAsync(Guid? siteId = null, bool includeSiteNeutral = true, CancellationToken ct = default);
    /// <summary>Creates a request. Validates parent mode and throws <see cref="NotFoundException"/> / <see cref="ConflictException"/> on violations.</summary>
    Task<RequestInfo> CreateAsync(CreateRequestRequest request, CancellationToken ct = default);
    /// <summary>Updates a request. Throws <see cref="NotFoundException"/> / <see cref="ConflictException"/> on tree violations. Returns <c>null</c> if not found.</summary>
    Task<RequestInfo?> UpdateAsync(Guid id, UpdateRequestRequest request, CancellationToken ct = default);
    /// <summary>Deletes a request. Returns <c>false</c> if not found.</summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
    /// <summary>Returns <c>true</c> if a request with the given ID exists.</summary>
    Task<bool> ExistsAsync(Guid id, CancellationToken ct = default);
    /// <summary>Updates only the schedule fields of a request. Throws if a non-leaf request is scheduled directly.</summary>
    Task<RequestInfo?> UpdateScheduleAsync(Guid id, ScheduleRequestRequest request, CancellationToken ct = default);
    /// <summary>Adds a requirement to a request.</summary>
    Task<RequestRequirementInfo> AddRequirementAsync(Guid requestId, AddRequirementRequest requirement, CancellationToken ct = default);
    /// <summary>Removes a requirement. Returns <c>false</c> if not found.</summary>
    Task<bool> DeleteRequirementAsync(Guid requestId, Guid requirementId, CancellationToken ct = default);
    /// <summary>Returns direct children of the given parent request.</summary>
    Task<List<RequestInfo>> GetChildrenAsync(Guid parentId, CancellationToken ct = default);
    /// <summary>Moves a request to a new parent. Throws on cycle, self-parent, or leaf parent.</summary>
    Task<RequestInfo?> MoveAsync(Guid id, Guid? newParentId, int sortOrder, CancellationToken ct = default);
    /// <summary>Returns the total number of descendants.</summary>
    Task<int> GetDescendantCountAsync(Guid id, CancellationToken ct = default);
    /// <summary>Returns <c>true</c> if reparenting would create a cycle.</summary>
    Task<bool> WouldCreateCycleAsync(Guid requestId, Guid newParentId, CancellationToken ct = default);
    /// <summary>Returns the planning mode of the request, or <c>null</c> if not found.</summary>
    Task<PlanningMode?> GetPlanningModeAsync(Guid id, CancellationToken ct = default);
    /// <summary>Returns <c>true</c> if the request has at least one direct child.</summary>
    Task<bool> HasChildrenAsync(Guid id, CancellationToken ct = default);
    /// <summary>Deletes the request and all its descendants. Returns the count deleted.</summary>
    Task<int> DeleteSubtreeAsync(Guid id, CancellationToken ct = default);
}

public class RequestService : IRequestService
{
    private readonly IRequestRepository _repository;

    public RequestService(IRequestRepository repository)
    {
        _repository = repository;
    }

    public Task<List<RequestInfo>> GetAllAsync(bool includeRequirements = false, CancellationToken ct = default)
        => _repository.GetAllAsync(includeRequirements, ct);

    public Task<PagedResult<RequestInfo>> GetAllAsync(PageRequest page, bool includeRequirements = false, CancellationToken ct = default)
        => _repository.GetAllAsync(page, includeRequirements, ct);

    public Task<RequestInfo?> GetByIdAsync(Guid id, bool includeRequirements = true, CancellationToken ct = default)
        => _repository.GetByIdAsync(id, includeRequirements, ct);

    public Task<List<RequestInfo>> GetByIdsAsync(IReadOnlyList<Guid> ids, bool includeRequirements = true, CancellationToken ct = default)
        => _repository.GetByIdsAsync(ids, includeRequirements, ct);

    public Task<List<RequestInfo>> GetScheduledBySiteWindowAsync(Guid siteId, DateTime from, DateTime to, CancellationToken ct = default)
        => _repository.GetScheduledBySiteWindowAsync(siteId, from, to, ct);

    public Task<List<RequestInfo>> GetUnscheduledAsync(Guid? siteId = null, bool includeSiteNeutral = true, CancellationToken ct = default)
        => _repository.GetUnscheduledAsync(siteId, includeSiteNeutral, ct: ct);

    public async Task<RequestInfo> CreateAsync(CreateRequestRequest request, CancellationToken ct = default)
    {
        if (request.ParentRequestId.HasValue)
        {
            var parentMode = await _repository.GetPlanningModeAsync(request.ParentRequestId.Value);
            if (parentMode == null) throw new NotFoundException("Parent request", request.ParentRequestId.Value);
            if (parentMode == PlanningMode.Leaf) throw new ConflictException("Cannot add children to a leaf request");
        }
        return await _repository.CreateAsync(request);
    }

    public async Task<RequestInfo?> UpdateAsync(Guid id, UpdateRequestRequest request, CancellationToken ct = default)
    {
        if (request.ParentRequestId.HasValue)
        {
            if (request.ParentRequestId.Value == id)
                throw new ArgumentException("A request cannot be its own parent");
            var wouldCycle = await _repository.WouldCreateCycleAsync(id, request.ParentRequestId.Value);
            if (wouldCycle) throw new ConflictException("This change would create a circular reference");
            var parentMode = await _repository.GetPlanningModeAsync(request.ParentRequestId.Value);
            if (parentMode == null) throw new NotFoundException("Parent request", request.ParentRequestId.Value);
            if (parentMode == PlanningMode.Leaf) throw new ConflictException("Cannot add children to a leaf request");
        }

        if (request.PlanningMode == PlanningMode.Leaf)
        {
            var hasChildren = await _repository.HasChildrenAsync(id);
            if (hasChildren) throw new ConflictException("Cannot change to leaf mode while request has children");
        }

        var existingMode = await _repository.GetPlanningModeAsync(id);
        var effectiveMode = request.PlanningMode ?? existingMode;
        if (effectiveMode != PlanningMode.Leaf && (request.ResourceId.HasValue || request.StartTs.HasValue || request.EndTs.HasValue))
            throw new ArgumentException("Only leaf requests can be directly scheduled to a space");

        return await _repository.UpdateAsync(id, request);
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default) => _repository.DeleteAsync(id, ct);

    public Task<bool> ExistsAsync(Guid id, CancellationToken ct = default) => _repository.ExistsAsync(id, ct);

    public async Task<RequestInfo?> UpdateScheduleAsync(Guid id, ScheduleRequestRequest request, CancellationToken ct = default)
    {
        var isSchedulingPayload = request.ResourceId.HasValue || request.StartTs.HasValue || request.EndTs.HasValue;
        if (isSchedulingPayload)
        {
            var mode = await _repository.GetPlanningModeAsync(id);
            if (mode != null && mode != PlanningMode.Leaf)
                throw new ArgumentException("Only leaf requests can be directly scheduled to a space");
        }
        return await _repository.UpdateScheduleAsync(id, request);
    }

    public Task<RequestRequirementInfo> AddRequirementAsync(Guid requestId, AddRequirementRequest requirement, CancellationToken ct = default)
        => _repository.AddRequirementAsync(requestId, requirement, ct);

    public Task<bool> DeleteRequirementAsync(Guid requestId, Guid requirementId, CancellationToken ct = default)
        => _repository.DeleteRequirementAsync(requestId, requirementId, ct);

    public Task<List<RequestInfo>> GetChildrenAsync(Guid parentId, CancellationToken ct = default)
        => _repository.GetChildrenAsync(parentId, ct);

    public async Task<RequestInfo?> MoveAsync(Guid id, Guid? newParentId, int sortOrder, CancellationToken ct = default)
    {
        if (newParentId.HasValue)
        {
            if (newParentId.Value == id)
                throw new ArgumentException("A request cannot be its own parent");
            if (await _repository.WouldCreateCycleAsync(id, newParentId.Value))
                throw new ConflictException("Moving this request would create a circular reference");
            var parentMode = await _repository.GetPlanningModeAsync(newParentId.Value);
            if (parentMode == null) throw new NotFoundException("Parent request", newParentId.Value);
            if (parentMode == PlanningMode.Leaf) throw new ConflictException("Cannot move a request under a leaf request");
        }
        return await _repository.MoveAsync(id, newParentId, sortOrder);
    }

    public Task<int> GetDescendantCountAsync(Guid id, CancellationToken ct = default)
        => _repository.GetDescendantCountAsync(id, ct);

    public Task<bool> WouldCreateCycleAsync(Guid requestId, Guid newParentId, CancellationToken ct = default)
        => _repository.WouldCreateCycleAsync(requestId, newParentId, ct);

    public Task<PlanningMode?> GetPlanningModeAsync(Guid id, CancellationToken ct = default)
        => _repository.GetPlanningModeAsync(id, ct);

    public Task<bool> HasChildrenAsync(Guid id, CancellationToken ct = default) => _repository.HasChildrenAsync(id, ct);

    public Task<int> DeleteSubtreeAsync(Guid id, CancellationToken ct = default) => _repository.DeleteSubtreeAsync(id, ct);
}
