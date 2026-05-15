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
            .RequireTenantMembership();

        group.MapGet("/{id:guid}", async (
            Guid id,
            IResourceAssignmentRepository repo,
            ILogger<EndpointLoggerCategory> logger) =>
            await EndpointHelpers.ExecuteAsync(async () =>
            {
                var a = await repo.GetByIdAsync(id);
                return a is null ? ErrorResponses.NotFound("ResourceAssignment", id) : Results.Ok(a);
            }, logger, "get resource assignment", new { id }))
            .WithName("GetResourceAssignmentById")
            .WithSummary("Get a resource assignment by ID");

        group.MapPost("/", async (
            [FromBody] CreateResourceAssignmentRequest request,
            IResourceAssignmentService service,
            ILogger<EndpointLoggerCategory> logger) =>
            await EndpointHelpers.ExecuteAsync(async () =>
            {
                var (assignment, conflict) = await service.CreateAsync(request);
                if (conflict is not null)
                    return Results.Json(new[] { conflict }, statusCode: StatusCodes.Status409Conflict);
                return Results.Created($"/api/resource-assignments/{assignment!.Id}", assignment);
            }, logger, "create resource assignment"))
            .WithName("CreateResourceAssignment")
            .WithSummary("Create a resource assignment");

        group.MapDelete("/{id:guid}", async (
            Guid id,
            IResourceAssignmentService service,
            ILogger<EndpointLoggerCategory> logger) =>
            await EndpointHelpers.ExecuteAsync(async () =>
            {
                var cancelled = await service.CancelAsync(id);
                return cancelled ? Results.NoContent() : ErrorResponses.NotFound("ResourceAssignment", id);
            }, logger, "cancel resource assignment", new { id }))
            .WithName("CancelResourceAssignment")
            .WithSummary("Cancel a resource assignment");

        group.MapPost("/validate", async (
            [FromBody] ValidateResourceAssignmentRequest request,
            IResourceAssignmentValidator validator,
            ILogger<EndpointLoggerCategory> logger) =>
            await EndpointHelpers.ExecuteAsync(async () =>
            {
                var result = await validator.ValidateAsync(request);
                return Results.Ok(result);
            }, logger, "validate resource assignment"))
            .WithName("ValidateResourceAssignment")
            .WithSummary("Validate a resource assignment without creating it");
    }
}
