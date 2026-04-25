using Api.Helpers;
using Api.Middleware;
using Api.Models;
using Api.Services;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Api.Endpoints;

public static class RequestEndpoints
{
    public static void MapRequestEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/requests").WithTags("Requests").RequireAuthorization().RequireTenantMembership();

        group.MapGet("/", async (IRequestService requestService, ILogger<EndpointLoggerCategory> logger, bool includeRequirements = false, int? page = null, int? pageSize = null) =>
        {
            return await EndpointHelpers.ExecuteAsync(async () =>
            {
                if (page.HasValue || pageSize.HasValue)
                {
                    var paged = await requestService.GetAllAsync(new PageRequest { Page = page ?? 1, PageSize = pageSize ?? PageRequest.DefaultPageSize }, includeRequirements);
                    return Results.Ok(paged);
                }
                return Results.Ok(await requestService.GetAllAsync(includeRequirements));
            }, logger, "list requests");
        })
        .WithName("GetRequests")
        .WithSummary("Get all requests");

        group.MapGet("/{id:guid}", async (Guid id, IRequestService requestService, ILogger<EndpointLoggerCategory> logger, bool includeRequirements = true) =>
        {
            return await EndpointHelpers.ExecuteAsync(async () =>
            {
                var request = await requestService.GetByIdAsync(id, includeRequirements);
                return request == null ? ErrorResponses.NotFound("Request", id) : Results.Ok(request);
            }, logger, "get request", new { id });
        })
        .WithName("GetRequestById")
        .WithSummary("Get a specific request by ID");

        group.MapPost("/", async (CreateRequestRequest request, IRequestService requestService, ISchedulingService schedulingService, ILogger<EndpointLoggerCategory> logger, IValidator<CreateRequestRequest> validator) =>
        {
            return await EndpointHelpers.ExecuteAsync(request, validator, async () =>
            {
                if (request.ParentRequestId.HasValue)
                {
                    var parentMode = await requestService.GetPlanningModeAsync(request.ParentRequestId.Value);
                    if (parentMode == null) return ErrorResponses.NotFound("Parent request", request.ParentRequestId.Value);
                    if (parentMode == PlanningMode.Leaf) return Results.BadRequest(new { error = "Cannot add children to a leaf request" });
                }
                var adjusted = await schedulingService.ApplySchedulingToCreateAsync(request);
                var created = await requestService.CreateAsync(adjusted);
                return Results.Created($"/requests/{created.Id}", created);
            }, logger, "create request", new { name = request.Name });
        })
        .WithName("CreateRequest")
        .WithSummary("Create a new request");

        group.MapPut("/{id:guid}", async (Guid id, UpdateRequestRequest request, IRequestService requestService, ISchedulingService schedulingService, ILogger<EndpointLoggerCategory> logger, IValidator<UpdateRequestRequest> validator) =>
        {
            return await EndpointHelpers.ExecuteAsync(request, validator, async () =>
            {
                if (request.ParentRequestId.HasValue)
                {
                    if (request.ParentRequestId.Value == id) return Results.BadRequest(new { error = "A request cannot be its own parent" });
                    var wouldCycle = await requestService.WouldCreateCycleAsync(id, request.ParentRequestId.Value);
                    if (wouldCycle) return Results.BadRequest(new { error = "This change would create a circular reference" });
                    var parentMode = await requestService.GetPlanningModeAsync(request.ParentRequestId.Value);
                    if (parentMode == null) return ErrorResponses.NotFound("Parent request", request.ParentRequestId.Value);
                    if (parentMode == PlanningMode.Leaf) return Results.BadRequest(new { error = "Cannot add children to a leaf request" });
                }
                if (request.PlanningMode == PlanningMode.Leaf)
                {
                    var hasChildren = await requestService.HasChildrenAsync(id);
                    if (hasChildren) return Results.BadRequest(new { error = "Cannot change to leaf mode while request has children" });
                }
                var existingMode = await requestService.GetPlanningModeAsync(id);
                var effectiveMode = request.PlanningMode ?? existingMode;
                if (effectiveMode != PlanningMode.Leaf && (request.SpaceId.HasValue || request.StartTs.HasValue || request.EndTs.HasValue))
                    return Results.BadRequest(new { error = "Only leaf requests can be directly scheduled to a space" });
                var adjusted = await schedulingService.ApplySchedulingToUpdateAsync(id, request);
                var updated = await requestService.UpdateAsync(id, adjusted);
                return updated == null ? ErrorResponses.NotFound("Request", id) : Results.Ok(updated);
            }, logger, "update request", new { id });
        })
        .WithName("UpdateRequest")
        .WithSummary("Update an existing request");

        group.MapDelete("/{id:guid}", async (Guid id, IRequestService requestService, ILogger<EndpointLoggerCategory> logger) =>
        {
            return await EndpointHelpers.ExecuteAsync(async () =>
            {
                var deleted = await requestService.DeleteAsync(id);
                return deleted ? Results.NoContent() : ErrorResponses.NotFound("Request", id);
            }, logger, "delete request", new { id });
        })
        .WithName("DeleteRequest")
        .WithSummary("Delete a request");

        group.MapPatch("/{id:guid}/schedule", async (Guid id, ScheduleRequestRequest request, IRequestService requestService, ISchedulingService schedulingService, ILogger<EndpointLoggerCategory> logger, IValidator<ScheduleRequestRequest> validator) =>
        {
            return await EndpointHelpers.ExecuteAsync(request, validator, async () =>
            {
                var mode = await requestService.GetPlanningModeAsync(id);
                if (mode == null) return ErrorResponses.NotFound("Request", id);
                var isSchedulingPayload = request.SpaceId.HasValue || request.StartTs.HasValue || request.EndTs.HasValue;
                if (mode != PlanningMode.Leaf && isSchedulingPayload)
                    return Results.BadRequest(new { error = "Only leaf requests can be directly scheduled to a space" });
                var adjusted = await schedulingService.ApplySchedulingToScheduleAsync(id, request);
                var updated = await requestService.UpdateScheduleAsync(id, adjusted);
                return updated == null ? ErrorResponses.NotFound("Request", id) : Results.Ok(updated);
            }, logger, "schedule request", new { id });
        })
        .WithName("ScheduleRequest")
        .WithSummary("Schedule or unschedule a request");

        group.MapPost("/{id:guid}/requirements", async (Guid id, AddRequirementRequest requirement, IRequestService requestService, ILogger<EndpointLoggerCategory> logger) =>
        {
            return await EndpointHelpers.ExecuteAsync(async () =>
            {
                var created = await requestService.AddRequirementAsync(id, requirement);
                return Results.Created($"/requests/{id}/requirements/{created.Id}", created);
            }, logger, "add request requirement", new { id, criterionId = requirement.CriterionId });
        })
        .WithName("AddRequestRequirement")
        .WithSummary("Add a requirement to a request");

        group.MapDelete("/{id:guid}/requirements/{requirementId:guid}", async (Guid id, Guid requirementId, IRequestService requestService, ILogger<EndpointLoggerCategory> logger) =>
        {
            return await EndpointHelpers.ExecuteAsync(async () =>
            {
                var deleted = await requestService.DeleteRequirementAsync(id, requirementId);
                return deleted ? Results.NoContent() : ErrorResponses.NotFound("Requirement", requirementId);
            }, logger, "delete request requirement", new { id, requirementId });
        })
        .WithName("DeleteRequestRequirement")
        .WithSummary("Remove a requirement from a request");

        group.MapGet("/{id:guid}/children", async (Guid id, IRequestService requestService, ILogger<EndpointLoggerCategory> logger) =>
        {
            return await EndpointHelpers.ExecuteAsync(async () =>
            {
                if (!await requestService.ExistsAsync(id)) return ErrorResponses.NotFound("Request", id);
                return Results.Ok(await requestService.GetChildrenAsync(id));
            }, logger, "get request children", new { id });
        })
        .WithName("GetRequestChildren")
        .WithSummary("Get child requests");

        group.MapPatch("/{id:guid}/move", async (Guid id, MoveRequestRequest request, IRequestService requestService, ILogger<EndpointLoggerCategory> logger, IValidator<MoveRequestRequest> validator) =>
        {
            return await EndpointHelpers.ExecuteAsync(request, validator, async () =>
            {
                if (request.NewParentRequestId.HasValue)
                {
                    if (request.NewParentRequestId.Value == id) return Results.BadRequest(new { error = "A request cannot be its own parent" });
                    if (await requestService.WouldCreateCycleAsync(id, request.NewParentRequestId.Value)) return Results.BadRequest(new { error = "Moving this request would create a circular reference" });
                    var parentMode = await requestService.GetPlanningModeAsync(request.NewParentRequestId.Value);
                    if (parentMode == null) return ErrorResponses.NotFound("Parent request", request.NewParentRequestId.Value);
                    if (parentMode == PlanningMode.Leaf) return Results.BadRequest(new { error = "Cannot move a request under a leaf request" });
                }
                var moved = await requestService.MoveAsync(id, request.NewParentRequestId, request.SortOrder);
                return moved == null ? ErrorResponses.NotFound("Request", id) : Results.Ok(moved);
            }, logger, "move request", new { id });
        })
        .WithName("MoveRequest")
        .WithSummary("Move or reparent a request in the tree");

        group.MapDelete("/{id:guid}/subtree", async (Guid id, IRequestService requestService, ILogger<EndpointLoggerCategory> logger) =>
        {
            return await EndpointHelpers.ExecuteAsync(async () =>
            {
                if (!await requestService.ExistsAsync(id)) return ErrorResponses.NotFound("Request", id);
                var deletedCount = await requestService.DeleteSubtreeAsync(id);
                return Results.Ok(new DeleteSubtreeResponse { DeletedCount = deletedCount });
            }, logger, "delete request subtree", new { id });
        })
        .WithName("DeleteRequestSubtree")
        .WithSummary("Delete a request and all its descendants");

        group.MapGet("/{id:guid}/descendants/count", async (Guid id, IRequestService requestService, ILogger<EndpointLoggerCategory> logger) =>
        {
            return await EndpointHelpers.ExecuteAsync(async () =>
            {
                if (!await requestService.ExistsAsync(id)) return ErrorResponses.NotFound("Request", id);
                return Results.Ok(new { count = await requestService.GetDescendantCountAsync(id) });
            }, logger, "get descendant count", new { id });
        })
        .WithName("GetDescendantCount")
        .WithSummary("Get count of all descendants");
    }
}
