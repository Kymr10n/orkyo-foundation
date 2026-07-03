using Api.Constants;
using Api.Helpers;
using Api.Middleware;
using Api.Models;
using Api.Security;
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
        var sites = app.MapGroup("/api/sites").RequireAuthorization().RequireMemberReadAdminWrite();

        sites.MapGet("/", async (ISiteService siteService, CancellationToken ct, int? page, int? pageSize) =>
        {
            if (page.HasValue || pageSize.HasValue)
            {
                var paged = await siteService.GetAllAsync(new PageRequest { Page = page ?? 1, PageSize = pageSize ?? PageRequest.DefaultPageSize }, ct);
                return Results.Ok(paged);
            }
            var sitesList = await siteService.GetAllAsync(ct);
            return Results.Ok(sitesList);
        })
        .WithName("GetSites")
        .WithDescription("Retrieves all sites for the current tenant. Supports ?page=1&pageSize=50 for pagination.")
        .Produces<PagedResult<SiteInfo>>(StatusCodes.Status200OK);

        sites.MapGet("/{siteId:guid}", async (Guid siteId, ISiteService siteService, CancellationToken ct) =>
        {
            var site = await siteService.GetByIdAsync(siteId, ct);
            return EndpointHelpers.OkOrNotFound(site, "Site", siteId);
        })
        .WithName("GetSiteById")
        .WithDescription("Retrieves a specific site by its ID")
        .Produces<SiteInfo>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        sites.MapPost("/", async (CreateSiteRequest request, HttpContext ctx, ICurrentPrincipal principal, ISiteService siteService, ITenantUserService tenantAudit, CancellationToken ct, ILogger<EndpointLoggerCategory> logger, IValidator<CreateSiteRequest> validator) =>
        {
            return await EndpointHelpers.ExecuteAsync(request, validator, async () =>
            {
                var site = await siteService.CreateAsync(request.Code, request.Name, request.Description, request.Address, ct);
                await tenantAudit.RecordAuditEventAsync(
                    ctx.GetOrgContext(), TenantAuditActions.SiteCreated, principal.UserId, "site", site.Id.ToString(),
                    new { site.Code, site.Name }, ct);
                return Results.Created($"/sites/{site.Id}", site);
            }, logger, "create site", new { code = request.Code });
        })
        .WithName("CreateSite")
        .WithDescription("Creates a new site")
        .Accepts<CreateSiteRequest>("application/json")
        .Produces<SiteInfo>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest);

        sites.MapPut("/{siteId:guid}", async (Guid siteId, UpdateSiteRequest request, HttpContext ctx, ICurrentPrincipal principal, ISiteService siteService, ITenantUserService tenantAudit, CancellationToken ct, ILogger<EndpointLoggerCategory> logger, IValidator<UpdateSiteRequest> validator) =>
        {
            return await EndpointHelpers.ExecuteAsync(request, validator, async () =>
            {
                var site = await siteService.UpdateAsync(siteId, request.Code, request.Name, request.Description, request.Address, ct);
                if (site is not null)
                    await tenantAudit.RecordAuditEventAsync(
                        ctx.GetOrgContext(), TenantAuditActions.SiteUpdated, principal.UserId, "site", siteId.ToString(),
                        new { site.Code, site.Name }, ct);
                return EndpointHelpers.OkOrNotFound(site, "Site", siteId);
            }, logger, "update site", new { siteId });
        })
        .WithName("UpdateSite")
        .WithDescription("Updates an existing site")
        .Accepts<UpdateSiteRequest>("application/json")
        .Produces<SiteInfo>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        sites.MapDelete("/{siteId:guid}", async (Guid siteId, HttpContext ctx, ICurrentPrincipal principal, ISiteService siteService, ITenantUserService tenantAudit, CancellationToken ct) =>
        {
            var deleted = await siteService.DeleteAsync(siteId, ct);
            if (deleted)
                await tenantAudit.RecordAuditEventAsync(
                    ctx.GetOrgContext(), TenantAuditActions.SiteDeleted, principal.UserId, "site", siteId.ToString(), null, ct);
            return deleted ? Results.NoContent() : ErrorResponses.NotFound("Site", siteId);
        })
        .WithName("DeleteSite")
        .WithDescription("Deletes a site and all its associated spaces")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status404NotFound);
    }
}
// ISiteRequest, CreateSiteRequest, UpdateSiteRequest moved to Api.Models (Core)
