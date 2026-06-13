using Api.Helpers;
using Api.Middleware;
using Api.Models;
using Api.Security;
using Api.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Api.Endpoints;

public static class FloorplanEndpoints
{
    public static void MapFloorplanEndpoints(this WebApplication app)
    {
        var floorplan = app.MapGroup("/api/sites/{siteId:guid}/floorplan")
            .RequireAuthorization()
            .RequireTenantMembership();

        floorplan.MapPost("/", async (
            Guid siteId,
            HttpContext ctx,
            ICurrentPrincipal principal,
            IAssetStorageService assetStorage,
            ITenantUserService tenantAudit,
            CancellationToken ct,
            ILogger<EndpointLoggerCategory> logger) =>
        {
            var tenant = ctx.GetTenantContext();
            var file = ctx.Request.Form.Files.GetFile("file");
            if (file is null || file.Length == 0)
                return Results.BadRequest(new { error = "No file uploaded" });

            try
            {
                var userId = principal.IsAuthenticated ? principal.UserId : (Guid?)null;
                var metadata = await assetStorage.UploadSiteFloorplanAsync(
                    tenant.TenantId, siteId,
                    new UploadFloorplanRequest
                    {
                        Content = file.OpenReadStream(),
                        FileName = file.FileName,
                        ContentType = file.ContentType,
                        ContentLength = file.Length
                    },
                    userId, ct);

                await tenantAudit.RecordAuditEventAsync(
                    ctx.GetOrgContext(), "floorplan.upload", userId, "site", siteId.ToString(),
                    new { metadata.FileName, metadata.MimeType, metadata.FileSizeBytes }, ct);

                return Results.Ok(new { success = true, metadata });
            }
            catch (KeyNotFoundException)
            {
                return ErrorResponses.NotFound("Site", siteId);
            }
            catch (ArgumentException ex)
            {
                logger.LogInformation(ex, "Invalid floorplan upload for site {SiteId}", siteId);
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to upload floorplan for site {SiteId}", siteId);
                return Results.Problem("Failed to upload floorplan");
            }
        })
        .RequireEditAccess()
        .DisableAntiforgery()
        .Accepts<IFormFile>("multipart/form-data")
        .WithName("UploadFloorplan")
        .WithDescription("Upload a floorplan image for a site");

        floorplan.MapGet("/", async (
            Guid siteId,
            HttpContext ctx,
            ICurrentPrincipal principal,
            IAssetStorageService assetStorage,
            ITenantUserService tenantAudit,
            CancellationToken ct) =>
        {
            var tenant = ctx.GetTenantContext();

            try
            {
                var download = await assetStorage.GetSiteFloorplanDownloadAsync(
                    tenant.TenantId, siteId, ct);
                if (download is null)
                    return Results.NotFound(new { error = "No floorplan found for this site" });

                var asset = download.Metadata;
                var eTag = $"\"{asset.ChecksumSha256}\"";
                ctx.Response.Headers.ETag = eTag;
                ctx.Response.Headers.CacheControl = "private, max-age=300";
                ctx.Response.Headers.Append("Vary", "Cookie");
                ctx.Response.ContentLength = asset.SizeBytes;

                if (ctx.Request.Headers.TryGetValue("If-None-Match", out var ifNoneMatch)
                    && ifNoneMatch.Any(v => string.Equals(v, eTag, StringComparison.Ordinal)))
                    return Results.StatusCode(StatusCodes.Status304NotModified);

                // Audit actual content delivery (304 cache hits above are skipped to avoid noise).
                var userId = principal.IsAuthenticated ? principal.UserId : (Guid?)null;
                await tenantAudit.RecordAuditEventAsync(
                    ctx.GetOrgContext(), "floorplan.download", userId, "site", siteId.ToString(),
                    new { asset.FileName, asset.SizeBytes }, ct);

                return Results.Bytes(download.Data, asset.ContentType, asset.FileName);
            }
            catch (KeyNotFoundException)
            {
                return ErrorResponses.NotFound("Site", siteId);
            }
        })
        .WithName("GetFloorplan")
        .WithDescription("Get the floorplan image for a site");

        floorplan.MapGet("/metadata", async (
            Guid siteId,
            HttpContext ctx,
            IAssetStorageService assetStorage,
            CancellationToken ct) =>
        {
            var tenant = ctx.GetTenantContext();

            try
            {
                var metadata = await assetStorage.GetSiteFloorplanMetadataAsync(
                    tenant.TenantId, siteId, ct);
                return metadata is null
                    ? Results.Content("null", "application/json")
                    : Results.Ok(metadata);
            }
            catch (KeyNotFoundException)
            {
                return ErrorResponses.NotFound("Site", siteId);
            }
        })
        .WithName("GetFloorplanMetadata")
        .WithDescription("Get metadata information for a site's floorplan");

        floorplan.MapDelete("/", async (
            Guid siteId,
            HttpContext ctx,
            ICurrentPrincipal principal,
            IAssetStorageService assetStorage,
            ITenantUserService tenantAudit,
            CancellationToken ct,
            ILogger<EndpointLoggerCategory> logger) =>
        {
            var tenant = ctx.GetTenantContext();

            try
            {
                var deleted = await assetStorage.DeleteSiteFloorplanAsync(
                    tenant.TenantId, siteId, ct);
                if (!deleted)
                    return Results.NotFound(new { error = "No floorplan found for this site" });

                var userId = principal.IsAuthenticated ? principal.UserId : (Guid?)null;
                await tenantAudit.RecordAuditEventAsync(
                    ctx.GetOrgContext(), "floorplan.delete", userId, "site", siteId.ToString(), null, ct);

                logger.LogInformation("Deleted floorplan asset for site {SiteId}", siteId);
                return Results.NoContent();
            }
            catch (KeyNotFoundException)
            {
                return ErrorResponses.NotFound("Site", siteId);
            }
        })
        .RequireEditAccess()
        .WithName("DeleteFloorplan")
        .WithDescription("Delete a site's floorplan");
    }
}
