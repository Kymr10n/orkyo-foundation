using Api.Helpers;
using Api.Middleware;
using Api.Models;
using Api.Repositories;
using Api.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Api.Endpoints;

public static class ResourceAssignmentEndpoints
{
    public static void MapResourceAssignmentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/resource-assignments")
            .WithTags("ResourceAssignments")
            .RequireAuthorization()
            .RequireMemberReadEditorWrite();

        group.MapGet("/", async (
            [FromQuery] Guid? requestId,
            [FromQuery] string? resourceTypeKey,
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to,
            IResourceAssignmentRepository repo,
            CancellationToken ct) =>
        {
            // Bulk window query (drives the People grid in one request) takes
            // precedence over the per-request lookup.
            if (resourceTypeKey is not null)
            {
                if (from is null || to is null)
                    return ErrorResponses.BadRequest("from and to query parameters are required with resourceTypeKey");
                var byType = await repo.GetByResourceTypeAsync(resourceTypeKey, from.Value, to.Value, ct);
                return Results.Ok(byType);
            }
            if (requestId is null)
                return ErrorResponses.BadRequest("requestId query parameter is required");
            var assignments = await repo.GetByRequestAsync(requestId.Value);
            return Results.Ok(assignments);
        })
            .WithName("ListResourceAssignmentsByRequest")
            .WithSummary("List resource assignments for a request, or in bulk by resource type + window");

        group.MapGet("/{id:guid}", async (
            Guid id,
            IResourceAssignmentRepository repo,
            CancellationToken ct) =>
        {
            var a = await repo.GetByIdAsync(id);
            return EndpointHelpers.OkOrNotFound(a, "ResourceAssignment", id);
        })
            .WithName("GetResourceAssignmentById")
            .WithSummary("Get a resource assignment by ID");

        group.MapPost("/", async (
            [FromBody] CreateResourceAssignmentRequest request,
            IResourceAssignmentService service,
            CancellationToken ct) =>
        {
            var (assignment, conflict) = await service.CreateAsync(request);
            if (conflict is not null)
                return Results.Json(new[] { conflict }, statusCode: StatusCodes.Status409Conflict);
            return Results.Created($"/api/resource-assignments/{assignment!.Id}", assignment);
        })
            .WithName("CreateResourceAssignment")
            .WithSummary("Create a resource assignment");

        group.MapDelete("/{id:guid}", async (
            Guid id,
            IResourceAssignmentService service,
            CancellationToken ct) =>
        {
            var cancelled = await service.CancelAsync(id);
            return cancelled ? Results.NoContent() : ErrorResponses.NotFound("ResourceAssignment", id);
        })
            .WithName("CancelResourceAssignment")
            .WithSummary("Cancel a resource assignment");

        group.MapPost("/validate", async (
            [FromBody] ValidateResourceAssignmentRequest request,
            IResourceAssignmentValidator validator,
            CancellationToken ct) =>
        {
            var result = await validator.ValidateAsync(request);
            return Results.Ok(result);
        })
            .WithName("ValidateResourceAssignment")
            .WithSummary("Validate a resource assignment without creating it")
            .AllowMemberWrite();

        group.MapPost("/validate-batch", async (
            [FromBody] ValidateResourceAssignmentBatchRequest request,
            IResourceAssignmentValidator validator,
            CancellationToken ct) =>
        {
            var results = await validator.ValidateBatchAsync(request.Items, ct);
            return Results.Ok(results);
        })
            .WithName("ValidateResourceAssignmentBatch")
            .WithSummary("Validate many resource assignments without creating them")
            .AllowMemberWrite();
    }
}
