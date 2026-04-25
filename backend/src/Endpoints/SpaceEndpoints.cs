using Api.Middleware;
using Api.Helpers;
using Api.Models;
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

        spaces.MapGet("/{spaceId:guid}", async (Guid siteId, Guid spaceId, ISpaceService spaceService, ILogger<EndpointLoggerCategory> logger) =>
        {
            return await EndpointHelpers.ExecuteAsync(async () =>
            {
                var space = await spaceService.GetByIdAsync(siteId, spaceId);
                return space == null ? ErrorResponses.NotFound("Space", spaceId) : Results.Ok(space);
            }, logger, "get space", new { siteId, spaceId });
        })
        .WithName("GetSpaceById")
        .WithSummary("Get a specific space by ID");

        spaces.MapPost("/", async (Guid siteId, [FromBody] CreateSpaceRequest request, ISpaceService spaceService, ILogger<EndpointLoggerCategory> logger, IValidator<CreateSpaceRequest> validator) =>
        {
            return await EndpointHelpers.ExecuteAsync(request, validator, async () =>
            {
                var space = await spaceService.CreateAsync(siteId, request.Name, request.Code, request.Description, request.IsPhysical, request.Geometry, request.Properties, request.Capacity);
                logger.LogInformation("Created space {SpaceId} for site {SiteId}", space.Id, siteId);
                return Results.Created($"/sites/{siteId}/spaces/{space.Id}", space);
            }, logger, "create space", new { siteId });
        })
        .WithName("CreateSpace")
        .WithSummary("Create a new space");

        spaces.MapPut("/{spaceId:guid}", async (Guid siteId, Guid spaceId, [FromBody] UpdateSpaceRequest request, ISpaceService spaceService, ILogger<EndpointLoggerCategory> logger, IValidator<UpdateSpaceRequest> validator) =>
        {
            return await EndpointHelpers.ExecuteAsync(request, validator, async () =>
            {
                var space = await spaceService.UpdateAsync(siteId, spaceId, request.Name, request.Code, request.Description, request.Geometry, request.Properties, request.GroupId, request.Capacity);
                if (space == null) return ErrorResponses.NotFound("Space", spaceId);
                logger.LogInformation("Updated space {SpaceId} for site {SiteId}", spaceId, siteId);
                return Results.Ok(space);
            }, logger, "update space", new { siteId, spaceId });
        })
        .WithName("UpdateSpace")
        .WithSummary("Update an existing space");

        spaces.MapDelete("/{spaceId:guid}", async (Guid siteId, Guid spaceId, ISpaceService spaceService, ILogger<EndpointLoggerCategory> logger) =>
        {
            return await EndpointHelpers.ExecuteAsync(async () =>
            {
                var deleted = await spaceService.DeleteAsync(siteId, spaceId);
                if (!deleted) return ErrorResponses.NotFound("Space", spaceId);
                logger.LogInformation("Deleted space {SpaceId} from site {SiteId}", spaceId, siteId);
                return Results.NoContent();
            }, logger, "delete space", new { siteId, spaceId });
        })
        .WithName("DeleteSpace")
        .WithSummary("Delete a space");
    }
}
