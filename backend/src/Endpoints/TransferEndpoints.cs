using Api.Configuration;
using Api.Constants;
using Api.Helpers;
using Api.Middleware;
using Api.Models;
using Api.Security;
using Api.Services.Transfer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;

namespace Api.Endpoints;

/// <summary>
/// Tenant data transfer: download the tenant's data as one archive, and load an archive in
/// place of the tenant's data. Administration area, every plan.
/// </summary>
public static class TransferEndpoints
{
    private const string ArchiveContentType = "application/zip";

    public static void MapTransferEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/admin/transfer")
            .RequireAuthorization()
            .RequireAdminArea()
            .RequireRateLimiting(FoundationRateLimitPolicies.AdminOperations)
            .WithTags("Transfer");

        // Built in a temporary file and sent from there: an export that fails half-way is
        // then an error response, not a download that ends early and looks complete.
        group.MapGet("/export", async (
            TenantTransferService transfer, ICurrentPrincipal principal, CancellationToken ct) =>
        {
            var file = CreateTempFile();
            try
            {
                var fileName = await transfer.ExportAsync(file, principal.UserIdOrNull, ct);
                file.Position = 0;
                return Results.Stream(file, ArchiveContentType, fileName);
            }
            catch
            {
                await file.DisposeAsync();
                throw;
            }
        })
        .WithName("ExportTenantArchive")
        .WithDescription("Downloads the tenant's data as a transfer archive")
        .Produces(200, contentType: ArchiveContentType);

        group.MapPost("/import/validate", async (
            HttpContext ctx, TenantTransferService transfer, CancellationToken ct) =>
        {
            await using var archive = await ReceiveArchiveAsync(ctx, transfer.MaxArchiveBytes, ct);
            return Results.Ok(await transfer.ValidateAsync(archive, ct));
        })
        .WithName("ValidateTenantArchive")
        .WithDescription("Reports whether a transfer archive can be imported into this tenant")
        .Produces<TransferValidationResult>(200);

        group.MapPost("/import", async (
            string? confirmation, HttpContext ctx, TenantTransferService transfer,
            ICurrentPrincipal principal, CancellationToken ct) =>
        {
            await using var archive = await ReceiveArchiveAsync(ctx, transfer.MaxArchiveBytes, ct);
            return Results.Ok(await transfer.ImportAsync(archive, confirmation, principal.UserIdOrNull, ct));
        })
        .DenyLockedAccount()
        .WithName("ImportTenantArchive")
        .WithDescription("Replaces the tenant's data with a transfer archive")
        .Produces<TransferImportResult>(200);
    }

    /// <summary>
    /// Copies the request body — the archive itself, not a multipart form — into a temporary
    /// file, which is what reading a ZIP needs. The size ceiling is enforced here, by count:
    /// the server's own body limit is lifted for this request so there is one limit, and an
    /// upload past it is answered with 413 and the transfer's own code.
    /// </summary>
    internal static async Task<FileStream> ReceiveArchiveAsync(HttpContext ctx, long maxBytes, CancellationToken ct)
    {
        if (ctx.Request.ContentLength > maxBytes) throw TooLarge(maxBytes);

        // Absent under the in-memory test server. Read-only once something has read the body:
        // the server's limit would then cut the upload short, so that is an error, not a skip.
        if (ctx.Features.Get<IHttpMaxRequestBodySizeFeature>() is { } bodySize)
        {
            if (bodySize.IsReadOnly)
                throw new InvalidOperationException(
                    "The request body was read before the transfer route could lift the server's body size limit.");
            bodySize.MaxRequestBodySize = null;
        }

        var file = CreateTempFile();
        try
        {
            var buffer = new byte[81920];
            long total = 0;
            int read;
            while ((read = await ctx.Request.Body.ReadAsync(buffer, ct)) > 0)
            {
                total += read;
                if (total > maxBytes) throw TooLarge(maxBytes);
                await file.WriteAsync(buffer.AsMemory(0, read), ct);
            }
            file.Position = 0;
            return file;
        }
        catch
        {
            await file.DisposeAsync();
            throw;
        }
    }

    private static TransferRejectedException TooLarge(long maxBytes) =>
        new(StatusCodes.Status413PayloadTooLarge, ApiErrorCodes.TransferArchiveTooLarge,
            $"The archive is larger than this installation accepts ({maxBytes} bytes).");

    // Deleted by the operating system when the stream closes, whatever path closes it.
    private static FileStream CreateTempFile() =>
        new(Path.Combine(Path.GetTempPath(), $"orkyo-transfer-{Guid.NewGuid():N}.tmp"),
            FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, bufferSize: 81920,
            FileOptions.DeleteOnClose | FileOptions.Asynchronous);
}
