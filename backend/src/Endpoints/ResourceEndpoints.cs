using System.Text.Json;
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

public static class ResourceEndpoints
{
    public static void MapResourceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/resources")
            .WithTags("Resources")
            .RequireAuthorization()
            .RequireTenantMembership();

        group.MapGet("/", async (
            IResourceService service,
            ILogger<EndpointLoggerCategory> logger,
            string? resourceTypeKey,
            bool? isActive,
            string? search) =>
            await EndpointHelpers.ExecuteAsync(async () =>
                Results.Ok(await service.GetAllAsync(new ResourceListFilter
                {
                    ResourceTypeKey = resourceTypeKey,
                    IsActive = isActive,
                    Search = search,
                })),
            logger, "list resources"))
            .WithName("GetResources")
            .WithSummary("Get all resources");

        group.MapGet("/{id:guid}", async (
            Guid id,
            IResourceService service,
            ILogger<EndpointLoggerCategory> logger) =>
            await EndpointHelpers.ExecuteAsync(async () =>
            {
                var r = await service.GetByIdAsync(id);
                return r is null ? ErrorResponses.NotFound("Resource", id) : Results.Ok(r);
            }, logger, "get resource", new { id }))
            .WithName("GetResourceById")
            .WithSummary("Get a resource by ID");

        group.MapPost("/", async (
            [FromBody] CreateResourceRequest request,
            IResourceService service,
            ILogger<EndpointLoggerCategory> logger) =>
            await EndpointHelpers.ExecuteAsync(async () =>
            {
                var r = await service.CreateAsync(request);
                return Results.Created($"/api/resources/{r.Id}", r);
            }, logger, "create resource"))
            .RequireAdminAccess()
            .WithName("CreateResource")
            .WithSummary("Create a new resource");

        group.MapPut("/{id:guid}", async (
            Guid id,
            [FromBody] UpdateResourceRequest request,
            IResourceService service,
            ILogger<EndpointLoggerCategory> logger) =>
            await EndpointHelpers.ExecuteAsync(async () =>
            {
                var r = await service.UpdateAsync(id, request);
                return r is null ? ErrorResponses.NotFound("Resource", id) : Results.Ok(r);
            }, logger, "update resource", new { id }))
            .RequireAdminAccess()
            .WithName("UpdateResource")
            .WithSummary("Update a resource");

        group.MapDelete("/{id:guid}", async (
            Guid id,
            IResourceService service,
            ILogger<EndpointLoggerCategory> logger) =>
            await EndpointHelpers.ExecuteAsync(async () =>
            {
                var deactivated = await service.DeactivateAsync(id);
                return deactivated ? Results.NoContent() : ErrorResponses.NotFound("Resource", id);
            }, logger, "deactivate resource", new { id }))
            .RequireAdminAccess()
            .WithName("DeactivateResource")
            .WithSummary("Deactivate (soft-delete) a resource");

        group.MapGet("/{id:guid}/assignments", async (
            Guid id,
            IResourceAssignmentService service,
            ILogger<EndpointLoggerCategory> logger,
            DateTime? from,
            DateTime? to) =>
            await EndpointHelpers.ExecuteAsync(async () =>
            {
                var fromUtc = from ?? DateTime.UtcNow.AddDays(-30);
                var toUtc = to ?? DateTime.UtcNow.AddDays(90);
                return Results.Ok(await service.GetByResourceAsync(id, fromUtc, toUtc));
            }, logger, "get resource assignments", new { id }))
            .WithName("GetResourceAssignments")
            .WithSummary("Get assignments for a resource");

        // ── Capabilities ──────────────────────────────────────────────

        group.MapGet("/{id:guid}/capabilities", async (
            Guid id,
            IResourceCapabilityRepository repository,
            ILogger<EndpointLoggerCategory> logger) =>
            await EndpointHelpers.ExecuteAsync(async () =>
                Results.Ok(await repository.GetByResourceAsync(id)),
            logger, "get resource capabilities", new { id }))
            .WithName("GetResourceCapabilities")
            .WithSummary("Get all capabilities for a resource");

        group.MapPost("/{id:guid}/capabilities", async (
            Guid id,
            AddResourceCapabilityRequest request,
            IResourceCapabilityRepository repository,
            ILogger<EndpointLoggerCategory> logger) =>
            await EndpointHelpers.ExecuteAsync(async () =>
            {
                var capability = await repository.UpsertAsync(id, request.CriterionId, request.Value);
                return Results.Created($"/api/resources/{id}/capabilities/{capability.Id}", capability);
            }, logger, "add resource capability", new { id, criterionId = request.CriterionId }))
            .WithName("AddResourceCapability")
            .WithSummary("Add or update a capability for a resource");

        group.MapDelete("/{id:guid}/capabilities/{capabilityId:guid}", async (
            Guid id,
            Guid capabilityId,
            IResourceCapabilityRepository repository,
            ILogger<EndpointLoggerCategory> logger) =>
            await EndpointHelpers.ExecuteAsync(async () =>
            {
                var deleted = await repository.DeleteAsync(id, capabilityId);
                return deleted ? Results.NoContent() : ErrorResponses.NotFound("Capability", capabilityId);
            }, logger, "delete resource capability", new { id, capabilityId }))
            .WithName("DeleteResourceCapability")
            .WithSummary("Remove a capability from a resource");
    }
}

public record AddResourceCapabilityRequest(Guid CriterionId, JsonElement Value);
