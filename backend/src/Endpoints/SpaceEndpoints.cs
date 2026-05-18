using Api.Helpers;
using Api.Middleware;
using Api.Models;
using Api.Repositories;
using Api.Services;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Api.Endpoints;

public static class SpaceEndpoints
{
    public static void MapSpaceEndpoints(this WebApplication app)
    {
        var spaces = app.MapGroup("/api/sites/{siteId:guid}/spaces")
            .WithTags("Spaces")
            .RequireAuthorization()
            .RequireTenantMembership();

        spaces.MapGet("/", async (Guid siteId, ISpaceService spaceService, ILogger<EndpointLoggerCategory> logger, int? page, int? pageSize) =>
        {
            return await EndpointHelpers.ExecuteAsync(async () =>
            {
                if (page.HasValue || pageSize.HasValue)
                {
                    var paged = await spaceService.GetAllAsync(siteId, new PageRequest { Page = page ?? 1, PageSize = pageSize ?? PageRequest.DefaultPageSize });
                    return Results.Ok(paged);
                }
                var spacesList = await spaceService.GetAllAsync(siteId);
                return Results.Ok(spacesList);
            }, logger, "list spaces", new { siteId });
        })
        .WithName("GetSpaces")
        .WithSummary("Get all spaces for a site");

        spaces.MapGet("/{resourceId:guid}", async (Guid siteId, Guid resourceId, ISpaceService spaceService, ILogger<EndpointLoggerCategory> logger) =>
        {
            return await EndpointHelpers.ExecuteAsync(async () =>
            {
                var space = await spaceService.GetByIdAsync(siteId, resourceId);
                return space == null ? ErrorResponses.NotFound("Space", resourceId) : Results.Ok(space);
            }, logger, "get space", new { siteId, resourceId });
        })
        .WithName("GetSpaceById")
        .WithSummary("Get a specific space by ID");

        spaces.MapPost("/", async (Guid siteId, [FromBody] CreateSpaceRequest request, ISpaceService spaceService, ILogger<EndpointLoggerCategory> logger, IValidator<CreateSpaceRequest> validator) =>
        {
            return await EndpointHelpers.ExecuteAsync(request, validator, async () =>
            {
                var space = await spaceService.CreateAsync(siteId, request.Name, request.Code, request.Description, request.IsPhysical, request.Geometry, request.Properties, request.Capacity);
                logger.LogInformation("Created space {ResourceId} for site {SiteId}", space.Id, siteId);
                return Results.Created($"/sites/{siteId}/spaces/{space.Id}", space);
            }, logger, "create space", new { siteId });
        })
        .WithName("CreateSpace")
        .WithSummary("Create a new space");

        spaces.MapPut("/{resourceId:guid}", async (Guid siteId, Guid resourceId, [FromBody] UpdateSpaceRequest request, ISpaceService spaceService, ILogger<EndpointLoggerCategory> logger, IValidator<UpdateSpaceRequest> validator) =>
        {
            return await EndpointHelpers.ExecuteAsync(request, validator, async () =>
            {
                var space = await spaceService.UpdateAsync(siteId, resourceId, request.Name, request.Code, request.Description, request.Geometry, request.Properties, request.GroupId, request.Capacity);
                if (space == null) return ErrorResponses.NotFound("Space", resourceId);
                logger.LogInformation("Updated space {ResourceId} for site {SiteId}", resourceId, siteId);
                return Results.Ok(space);
            }, logger, "update space", new { siteId, resourceId });
        })
        .WithName("UpdateSpace")
        .WithSummary("Update an existing space");

        spaces.MapDelete("/{resourceId:guid}", async (Guid siteId, Guid resourceId, ISpaceService spaceService, ILogger<EndpointLoggerCategory> logger) =>
        {
            return await EndpointHelpers.ExecuteAsync(async () =>
            {
                var deleted = await spaceService.DeleteAsync(siteId, resourceId);
                if (!deleted) return ErrorResponses.NotFound("Space", resourceId);
                logger.LogInformation("Deleted space {ResourceId} from site {SiteId}", resourceId, siteId);
                return Results.NoContent();
            }, logger, "delete space", new { siteId, resourceId });
        })
        .WithName("DeleteSpace")
        .WithSummary("Delete a space");

        spaces.MapGet("/{resourceId:guid}/capabilities", async (
            Guid siteId, Guid resourceId,
            ISpaceService spaceService,
            IResourceCapabilityRepository capabilityRepository,
            ILogger<EndpointLoggerCategory> logger) =>
            await EndpointHelpers.ExecuteAsync(async () =>
            {
                if (await spaceService.GetByIdAsync(siteId, resourceId) is null)
                    return ErrorResponses.NotFound("Space", resourceId);
                return Results.Ok(await capabilityRepository.GetByResourceAsync(resourceId));
            }, logger, "get space capabilities", new { siteId, resourceId }))
            .WithName("GetSpaceCapabilities")
            .WithSummary("Get all capabilities for a space");

        spaces.MapPost("/{resourceId:guid}/capabilities", async (
            Guid siteId, Guid resourceId,
            [FromBody] AddResourceCapabilityRequest request,
            ISpaceService spaceService,
            IResourceCapabilityRepository capabilityRepository,
            ILogger<EndpointLoggerCategory> logger) =>
            await EndpointHelpers.ExecuteAsync(async () =>
            {
                if (await spaceService.GetByIdAsync(siteId, resourceId) is null)
                    return ErrorResponses.NotFound("Space", resourceId);
                var capability = await capabilityRepository.UpsertAsync(resourceId, request.CriterionId, request.Value);
                return Results.Created($"/api/sites/{siteId}/spaces/{resourceId}/capabilities/{capability.Id}", capability);
            }, logger, "add space capability", new { siteId, resourceId, criterionId = request.CriterionId }))
            .WithName("AddSpaceCapability")
            .WithSummary("Add or update a capability for a space");

        spaces.MapDelete("/{resourceId:guid}/capabilities/{capabilityId:guid}", async (
            Guid siteId, Guid resourceId, Guid capabilityId,
            IResourceCapabilityRepository capabilityRepository,
            ILogger<EndpointLoggerCategory> logger) =>
            await EndpointHelpers.ExecuteAsync(async () =>
            {
                var deleted = await capabilityRepository.DeleteAsync(resourceId, capabilityId);
                return deleted ? Results.NoContent() : ErrorResponses.NotFound("Capability", capabilityId);
            }, logger, "delete space capability", new { siteId, resourceId, capabilityId }))
            .WithName("DeleteSpaceCapability")
            .WithSummary("Remove a capability from a space");
    }
}
