using Api.Middleware;
using Api.Helpers;
using Api.Models;
using Api.Services;
using Api.Validators;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Api.Endpoints;

public static class SiteEndpoints
{
    public static void MapSiteEndpoints(this WebApplication app)
    {
        var sites = app.MapGroup("/api/sites").RequireAuthorization().RequireTenantMembership();

        sites.MapGet("/", async (ISiteService siteService, ILogger<EndpointLoggerCategory> logger, int? page, int? pageSize) =>
        {
            return await EndpointHelpers.ExecuteAsync(async () =>
            {
                if (page.HasValue || pageSize.HasValue)
                {
                    var paged = await siteService.GetAllAsync(new PageRequest { Page = page ?? 1, PageSize = pageSize ?? PageRequest.DefaultPageSize });
                    return Results.Ok(paged);
                }
                var sitesList = await siteService.GetAllAsync();
                return Results.Ok(sitesList);
            }, logger, "list sites");
        })
        .WithName("GetSites")
        .WithDescription("Retrieves all sites for the current tenant. Supports ?page=1&pageSize=50 for pagination.")
        .Produces<PagedResult<SiteInfo>>(StatusCodes.Status200OK);

        sites.MapGet("/{siteId:guid}", async (Guid siteId, ISiteService siteService, ILogger<EndpointLoggerCategory> logger) =>
        {
            return await EndpointHelpers.ExecuteAsync(async () =>
            {
                var site = await siteService.GetByIdAsync(siteId);
                return site == null ? ErrorResponses.NotFound("Site", siteId) : Results.Ok(site);
            }, logger, "get site", new { siteId });
        })
        .WithName("GetSiteById")
        .WithDescription("Retrieves a specific site by its ID")
        .Produces<SiteInfo>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        sites.MapPost("/", async (CreateSiteRequest request, ISiteService siteService, ILogger<EndpointLoggerCategory> logger, IValidator<CreateSiteRequest> validator) =>
        {
            return await EndpointHelpers.ExecuteAsync(request, validator, async () =>
            {
                var site = await siteService.CreateAsync(request.Code, request.Name, request.Description, request.Address);
                return Results.Created($"/sites/{site.Id}", site);
            }, logger, "create site", new { code = request.Code });
        })
        .WithName("CreateSite")
        .WithDescription("Creates a new site")
        .Accepts<CreateSiteRequest>("application/json")
        .Produces<SiteInfo>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest);

        sites.MapPut("/{siteId:guid}", async (Guid siteId, UpdateSiteRequest request, ISiteService siteService, ILogger<EndpointLoggerCategory> logger, IValidator<UpdateSiteRequest> validator) =>
        {
            return await EndpointHelpers.ExecuteAsync(request, validator, async () =>
            {
                var site = await siteService.UpdateAsync(siteId, request.Code, request.Name, request.Description, request.Address);
                return site == null ? ErrorResponses.NotFound("Site", siteId) : Results.Ok(site);
            }, logger, "update site", new { siteId });
        })
        .WithName("UpdateSite")
        .WithDescription("Updates an existing site")
        .Accepts<UpdateSiteRequest>("application/json")
        .Produces<SiteInfo>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        sites.MapDelete("/{siteId:guid}", async (Guid siteId, ISiteService siteService, ILogger<EndpointLoggerCategory> logger) =>
        {
            return await EndpointHelpers.ExecuteAsync(async () =>
            {
                var deleted = await siteService.DeleteAsync(siteId);
                return deleted ? Results.NoContent() : ErrorResponses.NotFound("Site", siteId);
            }, logger, "delete site", new { siteId });
        })
        .WithName("DeleteSite")
        .WithDescription("Deletes a site and all its associated spaces")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status404NotFound);
    }
}

public interface ISiteRequest
{
    string Code { get; }
    string Name { get; }
}

public record CreateSiteRequest(string Code, string Name, string? Description, string? Address) : ISiteRequest;

public record UpdateSiteRequest(string Code, string Name, string? Description, string? Address) : ISiteRequest;
