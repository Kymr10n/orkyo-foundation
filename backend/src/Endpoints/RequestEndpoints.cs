using Api.Helpers;
using Api.Middleware;
using Api.Models;
using Api.Services;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Api.Endpoints;

public static class RequestEndpoints
{
    public static void MapRequestEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/requests").WithTags("Requests").RequireAuthorization().RequireTenantMembership();

        group.MapGet("/", async (IRequestService requestService, [FromServices] IConflictService conflictService, ILogger<EndpointLoggerCategory> logger, CancellationToken ct, bool includeRequirements = false, bool conflicted = false, bool? scheduled = null, int? page = null, int? pageSize = null) =>
        {
            return await EndpointHelpers.ExecuteAsync(async () =>
            {
                if (conflicted)
                {
                    // Tenant-wide: requests that currently have ≥1 conflict (the registry decides).
                    var registry = await conflictService.GetAllAsync(ct);
                    var ids = registry.Select(r => r.RequestId).ToList();
                    return Results.Ok(await requestService.GetByIdsAsync(ids, includeRequirements, ct));
                }
                if (scheduled == false)
                {
                    // The unscheduled backlog (drag-to-schedule source for the utilization panel).
                    return Results.Ok(await requestService.GetUnscheduledAsync(ct));
                }
                if (page.HasValue || pageSize.HasValue)
                {
                    var paged = await requestService.GetAllAsync(new PageRequest { Page = page ?? 1, PageSize = pageSize ?? PageRequest.DefaultPageSize }, includeRequirements, ct);
                    return Results.Ok(paged);
                }
                return Results.Ok(await requestService.GetAllAsync(includeRequirements, ct));
            }, logger, "list requests");
        })
        .WithName("GetRequests")
        .WithSummary("Get all requests");

        group.MapGet("/{id:guid}", async (Guid id, IRequestService requestService, ILogger<EndpointLoggerCategory> logger, CancellationToken ct, bool includeRequirements = true) =>
        {
            return await EndpointHelpers.ExecuteAsync(async () =>
            {
                var request = await requestService.GetByIdAsync(id, includeRequirements, ct);
                return EndpointHelpers.OkOrNotFound(request, "Request", id);
            }, logger, "get request", new { id });
        })
        .WithName("GetRequestById")
        .WithSummary("Get a specific request by ID");

        group.MapPost("/", async (CreateRequestRequest request, IRequestService requestService, ISchedulingService schedulingService, ILogger<EndpointLoggerCategory> logger, IValidator<CreateRequestRequest> validator, CancellationToken ct) =>
        {
            return await EndpointHelpers.ExecuteAsync(request, validator, async () =>
            {
                var adjusted = await schedulingService.ApplySchedulingToCreateAsync(request, ct);
                var created = await requestService.CreateAsync(adjusted, ct);
                return Results.Created($"/requests/{created.Id}", created);
            }, logger, "create request", new { name = request.Name });
        })
        .WithName("CreateRequest")
        .WithSummary("Create a new request");

        group.MapPut("/{id:guid}", async (Guid id, UpdateRequestRequest request, IRequestService requestService, ISchedulingService schedulingService, ILogger<EndpointLoggerCategory> logger, IValidator<UpdateRequestRequest> validator, CancellationToken ct) =>
        {
            return await EndpointHelpers.ExecuteAsync(request, validator, async () =>
            {
                var adjusted = await schedulingService.ApplySchedulingToUpdateAsync(id, request, ct);
                var updated = await requestService.UpdateAsync(id, adjusted, ct);
                return EndpointHelpers.OkOrNotFound(updated, "Request", id);
            }, logger, "update request", new { id });
        })
        .WithName("UpdateRequest")
        .WithSummary("Update an existing request");

        group.MapDelete("/{id:guid}", async (Guid id, IRequestService requestService, CancellationToken ct, ILogger<EndpointLoggerCategory> logger) =>
        {
            return await EndpointHelpers.ExecuteAsync(async () =>
            {
                var deleted = await requestService.DeleteAsync(id, ct);
                return deleted ? Results.NoContent() : ErrorResponses.NotFound("Request", id);
            }, logger, "delete request", new { id });
        })
        .WithName("DeleteRequest")
        .WithSummary("Delete a request");

        group.MapPatch("/{id:guid}/schedule", async (Guid id, ScheduleRequestRequest request, IRequestService requestService, ISchedulingService schedulingService, ILogger<EndpointLoggerCategory> logger, IValidator<ScheduleRequestRequest> validator, CancellationToken ct) =>
        {
            return await EndpointHelpers.ExecuteAsync(request, validator, async () =>
            {
                var adjusted = await schedulingService.ApplySchedulingToScheduleAsync(id, request, ct);
                var updated = await requestService.UpdateScheduleAsync(id, adjusted, ct);
                return EndpointHelpers.OkOrNotFound(updated, "Request", id);
            }, logger, "schedule request", new { id });
        })
        .WithName("ScheduleRequest")
        .WithSummary("Schedule or unschedule a request");

        group.MapPost("/{id:guid}/requirements", async (Guid id, AddRequirementRequest requirement, IRequestService requestService, CancellationToken ct, ILogger<EndpointLoggerCategory> logger) =>
        {
            return await EndpointHelpers.ExecuteAsync(async () =>
            {
                var created = await requestService.AddRequirementAsync(id, requirement, ct);
                return Results.Created($"/requests/{id}/requirements/{created.Id}", created);
            }, logger, "add request requirement", new { id, criterionId = requirement.CriterionId });
        })
        .WithName("AddRequestRequirement")
        .WithSummary("Add a requirement to a request");

        group.MapDelete("/{id:guid}/requirements/{requirementId:guid}", async (Guid id, Guid requirementId, IRequestService requestService, CancellationToken ct, ILogger<EndpointLoggerCategory> logger) =>
        {
            return await EndpointHelpers.ExecuteAsync(async () =>
            {
                var deleted = await requestService.DeleteRequirementAsync(id, requirementId, ct);
                return deleted ? Results.NoContent() : ErrorResponses.NotFound("Requirement", requirementId);
            }, logger, "delete request requirement", new { id, requirementId });
        })
        .WithName("DeleteRequestRequirement")
        .WithSummary("Remove a requirement from a request");

        group.MapGet("/{id:guid}/children", async (Guid id, IRequestService requestService, CancellationToken ct, ILogger<EndpointLoggerCategory> logger) =>
        {
            return await EndpointHelpers.ExecuteAsync(async () =>
            {
                if (!await requestService.ExistsAsync(id, ct)) return ErrorResponses.NotFound("Request", id);
                return Results.Ok(await requestService.GetChildrenAsync(id, ct));
            }, logger, "get request children", new { id });
        })
        .WithName("GetRequestChildren")
        .WithSummary("Get child requests");

        group.MapPatch("/{id:guid}/move", async (Guid id, MoveRequestRequest request, IRequestService requestService, ILogger<EndpointLoggerCategory> logger, IValidator<MoveRequestRequest> validator, CancellationToken ct) =>
        {
            return await EndpointHelpers.ExecuteAsync(request, validator, async () =>
            {
                var moved = await requestService.MoveAsync(id, request.NewParentRequestId, request.SortOrder, ct);
                return EndpointHelpers.OkOrNotFound(moved, "Request", id);
            }, logger, "move request", new { id });
        })
        .WithName("MoveRequest")
        .WithSummary("Move or reparent a request in the tree");

        group.MapDelete("/{id:guid}/subtree", async (Guid id, IRequestService requestService, CancellationToken ct, ILogger<EndpointLoggerCategory> logger) =>
        {
            return await EndpointHelpers.ExecuteAsync(async () =>
            {
                if (!await requestService.ExistsAsync(id, ct)) return ErrorResponses.NotFound("Request", id);
                var deletedCount = await requestService.DeleteSubtreeAsync(id, ct);
                return Results.Ok(new DeleteSubtreeResponse { DeletedCount = deletedCount });
            }, logger, "delete request subtree", new { id });
        })
        .WithName("DeleteRequestSubtree")
        .WithSummary("Delete a request and all its descendants");

        group.MapGet("/{id:guid}/descendants/count", async (Guid id, IRequestService requestService, CancellationToken ct, ILogger<EndpointLoggerCategory> logger) =>
        {
            return await EndpointHelpers.ExecuteAsync(async () =>
            {
                if (!await requestService.ExistsAsync(id, ct)) return ErrorResponses.NotFound("Request", id);
                return Results.Ok(new { count = await requestService.GetDescendantCountAsync(id, ct) });
            }, logger, "get descendant count", new { id });
        })
        .WithName("GetDescendantCount")
        .WithSummary("Get count of all descendants");

        // Site + time-window scoped scheduled requests — the utilization grid's bar feed.
        var siteRequests = app.MapGroup("/api/sites/{siteId:guid}/requests")
            .WithTags("Requests").RequireAuthorization().RequireTenantMembership();

        siteRequests.MapGet("/", async (Guid siteId, DateTime from, DateTime to, IRequestService requestService, ILogger<EndpointLoggerCategory> logger, CancellationToken ct) =>
            await EndpointHelpers.ExecuteAsync(
                async () => Results.Ok(await requestService.GetScheduledBySiteWindowAsync(siteId, from, to, ct)),
                logger, "list site scheduled requests in window", new { siteId }))
            .WithName("GetSiteScheduledRequests")
            .WithSummary("Scheduled requests for a site whose bar overlaps [from,to]");
    }
}
