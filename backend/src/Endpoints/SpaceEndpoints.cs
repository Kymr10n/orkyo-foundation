using Api.Constants;
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

/// <summary>
/// The published space API. A space is a resource whose type declares geometry, so these routes
/// are a site-scoped projection of the generic resource stack — they own the DTO shape and
/// nothing else.
/// </summary>
public static class SpaceEndpoints
{
    public static void MapSpaceEndpoints(this WebApplication app)
    {
        var spaces = app.MapGroup("/api/sites/{siteId:guid}/spaces")
            .WithTags("Spaces")
            .RequireAuthorization()
            .RequireMemberReadEditorWrite();

        spaces.MapGet("/", async (Guid siteId, IResourceRepository resources, CancellationToken ct, int? page, int? pageSize) =>
        {
            if (page.HasValue || pageSize.HasValue)
            {
                var paged = await resources.GetPlaceableBySiteAsync(siteId, new PageRequest { Page = page ?? 1, PageSize = pageSize ?? PageRequest.DefaultPageSize }, ct);
                return Results.Ok(new PagedResult<SpaceInfo>
                {
                    Items = [.. paged.Items.Select(SpaceInfo.FromResource)],
                    Page = paged.Page,
                    PageSize = paged.PageSize,
                    TotalItems = paged.TotalItems,
                });
            }
            var spacesList = await resources.GetPlaceableBySiteAsync(siteId, ct);
            return Results.Ok(spacesList.Select(SpaceInfo.FromResource).ToList());
        })
        .WithName("GetSpaces")
        .WithSummary("Get all spaces for a site");

        spaces.MapGet("/{resourceId:guid}", async (Guid siteId, Guid resourceId, IResourceRepository resources, CancellationToken ct) =>
        {
            var space = await resources.GetPlaceableAsync(siteId, resourceId, ct);
            return EndpointHelpers.OkOrNotFound(space is null ? null : SpaceInfo.FromResource(space), "Space", resourceId);
        })
        .WithName("GetSpaceById")
        .WithSummary("Get a specific space by ID");

        spaces.MapPost("/", async (Guid siteId, [FromBody] CreateSpaceRequest request, IResourceService resourceService, CancellationToken ct, ILogger<EndpointLoggerCategory> logger, IValidator<CreateSpaceRequest> validator) =>
        {
            return await EndpointHelpers.ExecuteAsync(request, validator, async () =>
            {
                var space = await resourceService.CreateAsync(new CreateResourceRequest
                {
                    ResourceTypeKey = ResourceTypeKeys.Space,
                    Name = request.Name,
                    Description = request.Description,
                    AllocationMode = AllocationModes.Exclusive,
                    // A space is immovable: it is at its site and cannot be assigned elsewhere.
                    HomeSiteId = siteId,
                    CrossSiteAllowed = false,
                    Code = request.Code,
                    IsPhysical = request.IsPhysical,
                    Geometry = request.Geometry,
                    Properties = request.Properties,
                    Capacity = request.Capacity,
                }, ct);
                logger.LogInformation("Created space {ResourceId} for site {SiteId}", space.Id, siteId);
                return Results.Created($"/sites/{siteId}/spaces/{space.Id}", SpaceInfo.FromResource(space));
            }, logger, "create space", new { siteId });
        })
        .WithName("CreateSpace")
        .WithSummary("Create a new space");

        spaces.MapPut("/{resourceId:guid}", async (Guid siteId, Guid resourceId, [FromBody] UpdateSpaceRequest request, IResourceRepository resources, IResourceService resourceService, CancellationToken ct, ILogger<EndpointLoggerCategory> logger, IValidator<UpdateSpaceRequest> validator) =>
        {
            return await EndpointHelpers.ExecuteAsync(request, validator, async () =>
            {
                // Scoped read first: the generic update knows nothing about the site in the route,
                // and a space belonging to another site must read as absent from this one.
                if (await resources.GetPlaceableAsync(siteId, resourceId, ct) is null)
                    return ErrorResponses.NotFound("Space", resourceId);

                var space = await resourceService.UpdateAsync(resourceId, new UpdateResourceRequest
                {
                    Name = request.Name,
                    Description = request.Description,
                    Code = request.Code,
                    Geometry = request.Geometry,
                    Properties = request.Properties,
                    Capacity = request.Capacity,
                }, ct);
                if (space == null) return ErrorResponses.NotFound("Space", resourceId);
                logger.LogInformation("Updated space {ResourceId} for site {SiteId}", resourceId, siteId);
                return Results.Ok(SpaceInfo.FromResource(space));
            }, logger, "update space", new { siteId, resourceId });
        })
        .WithName("UpdateSpace")
        .WithSummary("Update an existing space");

        spaces.MapDelete("/{resourceId:guid}", async (Guid siteId, Guid resourceId, IResourceRepository resources, IResourceService resourceService, CancellationToken ct, ILogger<EndpointLoggerCategory> logger) =>
        {
            if (await resources.GetPlaceableAsync(siteId, resourceId, ct) is null)
                return ErrorResponses.NotFound("Space", resourceId);

            // Deactivation, not deletion — the resource stays for its assignment history.
            if (!await resourceService.DeactivateAsync(resourceId, ct))
                return ErrorResponses.NotFound("Space", resourceId);
            logger.LogInformation("Deleted space {ResourceId} from site {SiteId}", resourceId, siteId);
            return Results.NoContent();
        })
        .WithName("DeleteSpace")
        .WithSummary("Delete a space");

        spaces.MapGet("/{resourceId:guid}/capabilities", async (
            Guid siteId, Guid resourceId,
            IResourceRepository resources,
            IResourceCapabilityRepository capabilityRepository,
            CancellationToken ct) =>
        {
            if (await resources.GetPlaceableAsync(siteId, resourceId, ct) is null)
                return ErrorResponses.NotFound("Space", resourceId);
            return Results.Ok(await capabilityRepository.GetByResourceAsync(resourceId, ct));
        })
            .WithName("GetSpaceCapabilities")
            .WithSummary("Get all capabilities for a space");

        spaces.MapPost("/{resourceId:guid}/capabilities", async (
            Guid siteId, Guid resourceId,
            [FromBody] AddResourceCapabilityRequest request,
            IValidator<AddResourceCapabilityRequest> validator,
            IResourceRepository resources,
            IResourceCapabilityRepository capabilityRepository,
            CancellationToken ct) =>
            await EndpointHelpers.ExecuteAsync(request, validator, async () =>
            {
                if (await resources.GetPlaceableAsync(siteId, resourceId, ct) is null)
                    return ErrorResponses.NotFound("Space", resourceId);
                var capability = await capabilityRepository.UpsertAsync(resourceId, request.CriterionId, request.Value, ct);
                return Results.Created($"/api/sites/{siteId}/spaces/{resourceId}/capabilities/{capability.Id}", capability);
            }))
            .WithName("AddSpaceCapability")
            .WithSummary("Add or update a capability for a space");

        spaces.MapDelete("/{resourceId:guid}/capabilities/{capabilityId:guid}", async (
            Guid siteId, Guid resourceId, Guid capabilityId,
            IResourceCapabilityRepository capabilityRepository,
            CancellationToken ct) =>
        {
            var deleted = await capabilityRepository.DeleteAsync(resourceId, capabilityId, ct);
            return deleted ? Results.NoContent() : ErrorResponses.NotFound("Capability", capabilityId);
        })
            .WithName("DeleteSpaceCapability")
            .WithSummary("Remove a capability from a space");
    }
}
