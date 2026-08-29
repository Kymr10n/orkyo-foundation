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
        var group = app.MapGroup("/api/requests").WithTags("Requests").RequireAuthorization().RequireMemberReadEditorWrite();

        group.MapGet("/", async (IRequestService requestService, [FromServices] IConflictService conflictService, CancellationToken ct, bool includeRequirements = false, bool conflicted = false, bool? scheduled = null, Guid? siteId = null, int? page = null, int? pageSize = null) =>
        {
            if (conflicted)
            {
                // Tenant-wide / all-time: requests that currently have ≥1 conflict (the registry decides).
                var registry = await conflictService.GetAllAsync(ct: ct);
                var ids = registry.Select(r => r.RequestId).ToList();
                return Results.Ok(await requestService.GetByIdsAsync(ids, includeRequirements, ct));
            }
            if (scheduled == false)
            {
                // The unscheduled backlog (drag-to-schedule source). When a site is given it is
                // scoped to that site plus site-neutral rows; otherwise tenant-wide.
                return Results.Ok(await requestService.GetUnscheduledAsync(siteId, includeSiteNeutral: true, ct));
            }
            if (page.HasValue || pageSize.HasValue)
            {
                var paged = await requestService.GetAllAsync(new PageRequest { Page = page ?? 1, PageSize = pageSize ?? PageRequest.DefaultPageSize }, includeRequirements, ct);
                return Results.Ok(paged);
            }
            return Results.Ok(await requestService.GetAllAsync(includeRequirements, ct));
        })
        .WithName("GetRequests")
        .WithSummary("Get all requests");

        group.MapGet("/{id:guid}", async (Guid id, IRequestService requestService, CancellationToken ct, bool includeRequirements = true) =>
        {
            var request = await requestService.GetByIdAsync(id, includeRequirements, ct);
            return EndpointHelpers.OkOrNotFound(request, "Request", id);
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

        group.MapDelete("/{id:guid}", async (Guid id, IRequestService requestService, CancellationToken ct) =>
        {
            var deleted = await requestService.DeleteAsync(id, ct);
            return EndpointHelpers.NoContentOrNotFound(deleted, "Request", id);
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

        group.MapPost("/{id:guid}/requirements", async (Guid id, AddRequirementRequest requirement,
            IValidator<AddRequirementRequest> validator, IRequestService requestService, CancellationToken ct) =>
            await EndpointHelpers.ExecuteAsync(requirement, validator, async () =>
            {
                var created = await requestService.AddRequirementAsync(id, requirement, ct);
                return Results.Created($"/requests/{id}/requirements/{created.Id}", created);
            }))
        .WithName("AddRequestRequirement")
        .WithSummary("Add a requirement to a request");

        group.MapDelete("/{id:guid}/requirements/{requirementId:guid}", async (Guid id, Guid requirementId, IRequestService requestService, CancellationToken ct) =>
        {
            var deleted = await requestService.DeleteRequirementAsync(id, requirementId, ct);
            return EndpointHelpers.NoContentOrNotFound(deleted, "Requirement", requirementId);
        })
        .WithName("DeleteRequestRequirement")
        .WithSummary("Remove a requirement from a request");

        // ── Dependencies ──────────────────────────────────────────────────────────────
        // Precedence edges, not tree edges. The literal "dependencies" and "critical-path"
        // segments never collide with /{id:guid} because the guid constraint rejects them —
        // route selection is by precedence, not declaration order.

        group.MapGet("/dependencies", async (IRequestDependencyService dependencyService, CancellationToken ct, Guid? siteId = null) =>
            Results.Ok(await dependencyService.GetAllAsync(siteId, ct)))
        .WithName("GetRequestDependencies")
        .WithSummary("Get every precedence edge, optionally scoped to a site");

        group.MapGet("/critical-path", async (ICriticalPathService criticalPathService, CancellationToken ct, Guid? siteId = null) =>
            Results.Ok(await criticalPathService.ComputeAsync(siteId, ct)))
        .WithName("GetCriticalPath")
        .WithSummary("Compute the critical path over the dependency network");

        group.MapGet("/{id:guid}/dependencies", async (Guid id, IRequestService requestService, IRequestDependencyService dependencyService, CancellationToken ct) =>
        {
            if (!await requestService.ExistsAsync(id, ct)) return ErrorResponses.NotFound("Request", id);
            return Results.Ok(await dependencyService.GetForRequestAsync(id, ct));
        })
        .WithName("GetDependenciesForRequest")
        .WithSummary("Get the predecessors and successors of a request");

        group.MapPost("/{id:guid}/dependencies", async (Guid id, CreateDependencyRequest request,
            IRequestDependencyService dependencyService, IValidator<CreateDependencyRequest> validator,
            ILogger<EndpointLoggerCategory> logger, CancellationToken ct) =>
            await EndpointHelpers.ExecuteAsync(request, validator, async () =>
            {
                var created = await dependencyService.CreateAsync(id, request, ct);
                logger.LogInformation("Added dependency {DependencyId}: {Predecessor} precedes {Successor}",
                    created.Id, created.PredecessorRequestId, created.SuccessorRequestId);
                return Results.Created($"/requests/{id}/dependencies/{created.Id}", created);
            }, logger, "add request dependency", new { id }))
        .WithName("AddRequestDependency")
        .WithSummary("Make a request wait for another to finish");

        group.MapDelete("/{id:guid}/dependencies/{dependencyId:guid}", async (Guid id, Guid dependencyId, IRequestDependencyService dependencyService, CancellationToken ct) =>
        {
            var deleted = await dependencyService.DeleteAsync(id, dependencyId, ct);
            return EndpointHelpers.NoContentOrNotFound(deleted, "Dependency", dependencyId);
        })
        .WithName("DeleteRequestDependency")
        .WithSummary("Remove a precedence edge");

        group.MapGet("/{id:guid}/children", async (Guid id, IRequestService requestService, CancellationToken ct) =>
        {
            if (!await requestService.ExistsAsync(id, ct)) return ErrorResponses.NotFound("Request", id);
            return Results.Ok(await requestService.GetChildrenAsync(id, ct));
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

        group.MapDelete("/{id:guid}/subtree", async (Guid id, IRequestService requestService, CancellationToken ct) =>
        {
            if (!await requestService.ExistsAsync(id, ct)) return ErrorResponses.NotFound("Request", id);
            var deletedCount = await requestService.DeleteSubtreeAsync(id, ct);
            return Results.Ok(new DeleteSubtreeResponse { DeletedCount = deletedCount });
        })
        .WithName("DeleteRequestSubtree")
        .WithSummary("Delete a request and all its descendants");

        group.MapGet("/{id:guid}/descendants/count", async (Guid id, IRequestService requestService, CancellationToken ct) =>
        {
            if (!await requestService.ExistsAsync(id, ct)) return ErrorResponses.NotFound("Request", id);
            return Results.Ok(new { count = await requestService.GetDescendantCountAsync(id, ct) });
        })
        .WithName("GetDescendantCount")
        .WithSummary("Get count of all descendants");

        // Site + time-window scoped scheduled requests — the utilization grid's bar feed.
        var siteRequests = app.MapGroup("/api/sites/{siteId:guid}/requests")
            .WithTags("Requests").RequireAuthorization().RequireMemberReadEditorWrite();

        siteRequests.MapGet("/", async (Guid siteId, DateTime from, DateTime to, IRequestService requestService, CancellationToken ct) =>
            Results.Ok(await requestService.GetScheduledBySiteWindowAsync(siteId, from, to, ct)))
            .WithName("GetSiteScheduledRequests")
            .WithSummary("Scheduled requests for a site whose bar overlaps [from,to]");
    }
}
