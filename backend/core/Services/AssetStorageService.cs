using System.Security.Cryptography;
using Api.Models;
using Api.Repositories;
using Api.Security.Quotas;

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
    IQuotaEnforcer quotaEnforcer,
    IQuotaUsageRollup quotaUsageRollup,
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

        var settings = await settingsService.GetSettingsAsync(ct);
        var validated = await ValidateFloorplanAsync(request, settings, ct);

        // Storage quota: delta-aware (upsert may replace an existing asset).
        // currentTotal - existingSize gives the baseline; requestedIncrement = new file size.
        var existingAsset = await assetRepository.GetForOwnerAsync(
            tenantId, AssetOwnerTypes.Site, siteId, AssetTypes.Floorplan, ct);
        var existingBytes = existingAsset?.SizeBytes ?? 0L;
        var currentTotal = await assetRepository.GetTotalSizeBytesAsync(tenantId, ct);
        await quotaEnforcer.EnsureWithinLimitAsync(
            QuotaResourceTypes.StorageBytes,
            currentTotal - existingBytes,
            validated.Data.LongLength,
            ct);

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

        await quotaUsageRollup.RecordDeltaAsync(QuotaResourceTypes.StorageBytes, asset.SizeBytes - existingBytes, ct);

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

        var existingAsset = await assetRepository.GetForOwnerAsync(
            tenantId, AssetOwnerTypes.Site, siteId, AssetTypes.Floorplan, ct);
        var deleted = await assetRepository.DeleteForOwnerAsync(
            tenantId, AssetOwnerTypes.Site, siteId, AssetTypes.Floorplan, ct);
        if (deleted && existingAsset is not null)
            await quotaUsageRollup.RecordDeltaAsync(QuotaResourceTypes.StorageBytes, -existingAsset.SizeBytes, ct);
        return deleted;
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

        var detectedMimeType = ImageHeaderReader.DetectMimeType(data)
            ?? throw new ArgumentException("File is not a recognised image format");

        var allowedMimeTypes = FloorplanUploadValidationPolicy.ParseAllowedMimeTypes(settings.Upload_AllowedMimeTypes);
        FloorplanUploadValidationPolicy.AssertMimeAllowed(detectedMimeType, allowedMimeTypes);

        if (!FloorplanMimeExtensionPolicy.TryGetExtensionForMime(detectedMimeType, out var extension))
            throw new ArgumentException($"Unsupported image format: {detectedMimeType}");

        if (!ImageHeaderReader.TryGetDimensions(data, out var width, out var height))
            throw new ArgumentException("Could not read image dimensions");

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
