using System.Security.Cryptography;
using Api.Models;
using Api.Repositories;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;

namespace Api.Services;

public interface IAssetStorageService
{
    Task<FloorplanMetadataInfo?> GetSiteFloorplanMetadataAsync(
        Guid tenantId,
        Guid siteId,
        CancellationToken ct = default);

    Task<AssetDownloadInfo?> GetSiteFloorplanDownloadAsync(
        Guid tenantId,
        Guid siteId,
        CancellationToken ct = default);

    Task<FloorplanMetadataInfo> UploadSiteFloorplanAsync(
        Guid tenantId,
        Guid siteId,
        UploadFloorplanRequest request,
        Guid? userId,
        CancellationToken ct = default);

    Task<bool> DeleteSiteFloorplanAsync(
        Guid tenantId,
        Guid siteId,
        CancellationToken ct = default);
}

public class AssetStorageService(
    IAssetRepository assetRepository,
    ITenantSettingsService settingsService,
    ILogger<AssetStorageService> logger) : IAssetStorageService
{
    public async Task<FloorplanMetadataInfo?> GetSiteFloorplanMetadataAsync(
        Guid tenantId,
        Guid siteId,
        CancellationToken ct = default)
    {
        if (!await assetRepository.OwnerExistsAsync(AssetOwnerTypes.Site, siteId, ct))
            throw new KeyNotFoundException($"Site {siteId} not found");

        var asset = await assetRepository.GetForOwnerAsync(
            tenantId, AssetOwnerTypes.Site, siteId, AssetTypes.Floorplan, ct);
        return asset is null ? null : ToFloorplanMetadata(asset);
    }

    public async Task<AssetDownloadInfo?> GetSiteFloorplanDownloadAsync(
        Guid tenantId,
        Guid siteId,
        CancellationToken ct = default)
    {
        if (!await assetRepository.OwnerExistsAsync(AssetOwnerTypes.Site, siteId, ct))
            throw new KeyNotFoundException($"Site {siteId} not found");

        return await assetRepository.GetDownloadForOwnerAsync(
            tenantId, AssetOwnerTypes.Site, siteId, AssetTypes.Floorplan, ct);
    }

    public async Task<FloorplanMetadataInfo> UploadSiteFloorplanAsync(
        Guid tenantId,
        Guid siteId,
        UploadFloorplanRequest request,
        Guid? userId,
        CancellationToken ct = default)
    {
        if (!await assetRepository.OwnerExistsAsync(AssetOwnerTypes.Site, siteId, ct))
            throw new KeyNotFoundException($"Site {siteId} not found");

        var settings = await settingsService.GetSettingsAsync();
        var validated = await ValidateFloorplanAsync(request, settings, ct);

        var asset = await assetRepository.UpsertPostgresAssetAsync(
            tenantId,
            AssetOwnerTypes.Site,
            siteId,
            AssetTypes.Floorplan,
            SanitizeFileName(request.FileName, validated.Extension),
            validated.ContentType,
            validated.Data,
            validated.ChecksumSha256,
            validated.WidthPx,
            validated.HeightPx,
            userId,
            ct);

        logger.LogInformation(
            "Stored floorplan asset {AssetId} for site {SiteId}: {Size} bytes, {ContentType}, {Width}x{Height}",
            asset.Id, siteId, asset.SizeBytes, asset.ContentType, asset.WidthPx, asset.HeightPx);

        return ToFloorplanMetadata(asset);
    }

    public async Task<bool> DeleteSiteFloorplanAsync(
        Guid tenantId,
        Guid siteId,
        CancellationToken ct = default)
    {
        if (!await assetRepository.OwnerExistsAsync(AssetOwnerTypes.Site, siteId, ct))
            throw new KeyNotFoundException($"Site {siteId} not found");

        return await assetRepository.DeleteForOwnerAsync(
            tenantId, AssetOwnerTypes.Site, siteId, AssetTypes.Floorplan, ct);
    }

    private static async Task<ValidatedFloorplan> ValidateFloorplanAsync(
        UploadFloorplanRequest file,
        TenantSettings settings,
        CancellationToken ct)
    {
        FloorplanUploadValidationPolicy.AssertNonEmpty(file.ContentLength);
        FloorplanUploadValidationPolicy.AssertWithinSizeLimit(file.ContentLength, settings.Upload_MaxFileSizeMb);

        byte[] data;
        await using (var input = file.Content)
        using (var ms = new MemoryStream())
        {
            await input.CopyToAsync(ms, ct);
            data = ms.ToArray();
        }

        await using var detectStream = new MemoryStream(data);
        IImageFormat format;
        try
        {
            format = await Image.DetectFormatAsync(detectStream, ct)
                ?? throw new ArgumentException("File is not a recognised image format");
        }
        catch (Exception ex) when (ex is not ArgumentException)
        {
            throw new ArgumentException("File is not a valid image", ex);
        }

        var detectedMimeType = format.DefaultMimeType.ToLowerInvariant();
        var allowedMimeTypes = FloorplanUploadValidationPolicy.ParseAllowedMimeTypes(settings.Upload_AllowedMimeTypes);
        FloorplanUploadValidationPolicy.AssertMimeAllowed(detectedMimeType, allowedMimeTypes);

        if (!FloorplanMimeExtensionPolicy.TryGetExtensionForMime(detectedMimeType, out var extension))
            throw new ArgumentException($"Unsupported image format: {detectedMimeType}");

        int width;
        int height;
        await using (var dimensionsStream = new MemoryStream(data))
        {
            try
            {
                var info = await Image.IdentifyAsync(dimensionsStream, ct)
                    ?? throw new ArgumentException("Could not read image dimensions");
                width = info.Width;
                height = info.Height;
            }
            catch (Exception ex) when (ex is not ArgumentException)
            {
                throw new ArgumentException("File is not a valid image", ex);
            }
        }

        var checksum = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
        return new ValidatedFloorplan(data, detectedMimeType, extension, checksum, width, height);
    }

    private static string SanitizeFileName(string fileName, string fallbackExtension)
    {
        var name = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(name))
            return $"floorplan{fallbackExtension}";

        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        if (string.IsNullOrWhiteSpace(sanitized))
            return $"floorplan{fallbackExtension}";

        return sanitized;
    }

    public static FloorplanMetadataInfo ToFloorplanMetadata(AssetInfo asset) => new()
    {
        AssetId = asset.Id,
        FileName = asset.FileName,
        MimeType = asset.ContentType,
        FileSizeBytes = asset.SizeBytes,
        ChecksumSha256 = asset.ChecksumSha256,
        WidthPx = asset.WidthPx ?? 0,
        HeightPx = asset.HeightPx ?? 0,
        UploadedAt = asset.UpdatedAt,
        UploadedByUserId = asset.UpdatedByUserId ?? asset.CreatedByUserId,
    };

    private sealed record ValidatedFloorplan(
        byte[] Data,
        string ContentType,
        string Extension,
        string ChecksumSha256,
        int WidthPx,
        int HeightPx);
}
