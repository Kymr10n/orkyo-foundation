using Api.Models;
using Api.Security;
using Api.Services;
using Api.Middleware;
using Api.Helpers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Npgsql;

namespace Api.Endpoints;

public static class FloorplanEndpoints
{
    public static void MapFloorplanEndpoints(this WebApplication app)
    {
        var floorplan = app.MapGroup("/api/sites/{siteId:guid}/floorplan").RequireAuthorization().RequireTenantMembership();

        floorplan.MapPost("/", async (
            Guid siteId,
            HttpContext ctx,
            ICurrentPrincipal principal,
            IFileStorageService fileStorage,
            ILogger<EndpointLoggerCategory> logger) =>
        {
            logger.LogInformation("Upload endpoint called for site {SiteId}", siteId);
            var tenant = ctx.GetTenantContext();

            var file = ctx.Request.Form.Files.GetFile("file");
            if (file == null || file.Length == 0)
                return Results.BadRequest(new { error = "No file uploaded" });

            try
            {
                var (filePath, detectedMimeType, width, height) = await fileStorage.SaveFloorplanAsync(file, siteId, tenant.TenantId);
                Guid? userId = principal.IsAuthenticated ? principal.UserId : null;

                await using var conn = await ctx.OpenTenantConnectionAsync();
                await using var selectCmd = new NpgsqlCommand("SELECT floorplan_image_path FROM sites WHERE id = @siteId", conn);
                selectCmd.Parameters.AddWithValue("siteId", siteId);
                var oldPath = await selectCmd.ExecuteScalarAsync() as string;

                if (!string.IsNullOrEmpty(oldPath))
                {
                    try { await fileStorage.DeleteFloorplanAsync(oldPath); }
                    catch (Exception ex) { logger.LogWarning(ex, "Failed to delete old floorplan: {Path}", oldPath); }
                }

                await using var updateCmd = new NpgsqlCommand(@"
                    UPDATE sites
                    SET floorplan_image_path = @path, floorplan_mime_type = @mimeType,
                        floorplan_file_size_bytes = @fileSize, floorplan_width_px = @width,
                        floorplan_height_px = @height, floorplan_uploaded_at = NOW(),
                        floorplan_uploaded_by_user_id = @userId, updated_at = NOW()
                    WHERE id = @siteId
                    RETURNING id, name, code", conn);

                updateCmd.Parameters.AddWithValue("siteId", siteId);
                updateCmd.Parameters.AddWithValue("path", filePath);
                updateCmd.Parameters.AddWithValue("mimeType", detectedMimeType);
                updateCmd.Parameters.AddWithValue("fileSize", file.Length);
                updateCmd.Parameters.AddWithValue("width", width);
                updateCmd.Parameters.AddWithValue("height", height);
                updateCmd.Parameters.AddWithValue("userId", userId.HasValue ? (object)userId.Value : DBNull.Value);

                await using var reader = await updateCmd.ExecuteReaderAsync();
                if (!await reader.ReadAsync())
                    return ErrorResponses.NotFound("Site", siteId);

                logger.LogInformation("Uploaded floorplan for site {SiteId}: {Path}, {Width}x{Height}px", siteId, filePath, width, height);
                return Results.Ok(new
                {
                    success = true,
                    metadata = new { imagePath = filePath, mimeType = detectedMimeType, fileSizeBytes = file.Length, widthPx = width, heightPx = height, uploadedAt = DateTime.UtcNow }
                });
            }
            catch (ArgumentException ex)
            {
                logger.LogWarning(ex, "Invalid floorplan upload for site {SiteId}", siteId);
                return Results.BadRequest(new { error = "Invalid file: unsupported format or dimensions" });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to upload floorplan for site {SiteId}", siteId);
                return Results.Problem("Failed to upload floorplan");
            }
        })
        .DisableAntiforgery()
        .Accepts<IFormFile>("multipart/form-data")
        .WithName("UploadFloorplan")
        .WithDescription("Upload a floorplan image for a site");

        floorplan.MapGet("/", async (Guid siteId, HttpContext ctx, IFileStorageService fileStorage, ILogger<EndpointLoggerCategory> logger) =>
        {
            await using var conn = await ctx.OpenTenantConnectionAsync();
            await using var cmd = new NpgsqlCommand(
                "SELECT floorplan_image_path, floorplan_mime_type, floorplan_file_size_bytes, floorplan_width_px, floorplan_height_px, floorplan_uploaded_at FROM sites WHERE id = @siteId", conn);
            cmd.Parameters.AddWithValue("siteId", siteId);
            await using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) return Results.NotFound(new { error = "Site not found" });
            if (reader.IsDBNull(0)) return Results.NotFound(new { error = "No floorplan found for this site" });

            var imagePath = reader.GetString(0);
            var fileSize = reader.IsDBNull(2) ? 0L : reader.GetInt64(2);
            var uploadedAtUtc = reader.IsDBNull(5) ? DateTime.UnixEpoch : DateTime.SpecifyKind(reader.GetDateTime(5), DateTimeKind.Utc);
            var eTag = $"\"{siteId:N}-{uploadedAtUtc.Ticks}-{fileSize}\"";

            ctx.Response.Headers.ETag = eTag;
            ctx.Response.Headers.CacheControl = "private, max-age=300";
            ctx.Response.Headers.LastModified = uploadedAtUtc.ToString("R");
            ctx.Response.Headers.Append("Vary", "Cookie");

            if (ctx.Request.Headers.TryGetValue("If-None-Match", out var ifNoneMatch)
                && ifNoneMatch.Any(v => string.Equals(v, eTag, StringComparison.Ordinal)))
                return Results.StatusCode(StatusCodes.Status304NotModified);

            var result = await fileStorage.GetFloorplanAsync(imagePath);
            if (result == null) { logger.LogWarning("Floorplan file not found: {Path}", imagePath); return Results.NotFound(new { error = "Floorplan file not found" }); }

            var (stream, mimeType) = result.Value;
            return Results.Stream(stream, mimeType);
        })
        .WithName("GetFloorplan")
        .WithDescription("Get the floorplan image for a site");

        floorplan.MapGet("/metadata", async (Guid siteId, HttpContext ctx) =>
        {
            await using var conn = await ctx.OpenTenantConnectionAsync();
            await using var cmd = new NpgsqlCommand(
                "SELECT floorplan_image_path, floorplan_mime_type, floorplan_file_size_bytes, floorplan_width_px, floorplan_height_px, floorplan_uploaded_at, floorplan_uploaded_by_user_id FROM sites WHERE id = @siteId", conn);
            cmd.Parameters.AddWithValue("siteId", siteId);
            await using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) return ErrorResponses.NotFound("Site", siteId);
            if (reader.IsDBNull(0)) return Results.Content("null", "application/json");
            return Results.Ok(new
            {
                imagePath = reader.GetString(0),
                mimeType = reader.GetString(1),
                fileSizeBytes = reader.GetInt64(2),
                widthPx = reader.GetInt32(3),
                heightPx = reader.GetInt32(4),
                uploadedAt = reader.GetDateTime(5),
                uploadedByUserId = reader.IsDBNull(6) ? (Guid?)null : reader.GetGuid(6)
            });
        })
        .WithName("GetFloorplanMetadata")
        .WithDescription("Get metadata information for a site's floorplan");

        floorplan.MapDelete("/", async (Guid siteId, HttpContext ctx, IFileStorageService fileStorage, ILogger<EndpointLoggerCategory> logger) =>
        {
            await using var conn = await ctx.OpenTenantConnectionAsync();
            await using var selectCmd = new NpgsqlCommand("SELECT floorplan_image_path FROM sites WHERE id = @siteId", conn);
            selectCmd.Parameters.AddWithValue("siteId", siteId);
            var filePath = await selectCmd.ExecuteScalarAsync() as string;
            if (string.IsNullOrEmpty(filePath)) return Results.NotFound(new { error = "No floorplan found for this site" });

            try { await fileStorage.DeleteFloorplanAsync(filePath); }
            catch (Exception ex) { logger.LogError(ex, "Failed to delete floorplan file: {Path}", filePath); }

            await using var updateCmd = new NpgsqlCommand(@"
                UPDATE sites SET floorplan_image_path = NULL, floorplan_mime_type = NULL,
                    floorplan_file_size_bytes = NULL, floorplan_width_px = NULL, floorplan_height_px = NULL,
                    floorplan_uploaded_at = NULL, floorplan_uploaded_by_user_id = NULL, updated_at = NOW()
                WHERE id = @siteId", conn);
            updateCmd.Parameters.AddWithValue("siteId", siteId);
            await updateCmd.ExecuteNonQueryAsync();
            logger.LogInformation("Deleted floorplan for site {SiteId}", siteId);
            return Results.Ok(new { message = "Floorplan deleted successfully" });
        })
        .WithName("DeleteFloorplan")
        .WithDescription("Delete a site's floorplan and its metadata");
    }
}
