using Api.Models;

namespace Api.Repositories;

public interface IAssetRepository
{
    Task<bool> OwnerExistsAsync(string ownerType, Guid ownerId, CancellationToken ct = default);

    Task<AssetInfo?> GetForOwnerAsync(
        Guid tenantId,
        string ownerType,
        Guid ownerId,
        string assetType,
        CancellationToken ct = default);

    Task<AssetDownloadInfo?> GetDownloadForOwnerAsync(
        Guid tenantId,
        string ownerType,
        Guid ownerId,
        string assetType,
        CancellationToken ct = default);

    Task<AssetInfo> UpsertPostgresAssetAsync(
        Guid tenantId,
        string ownerType,
        Guid ownerId,
        string assetType,
        string fileName,
        string contentType,
        byte[] data,
        string checksumSha256,
        int? widthPx,
        int? heightPx,
        Guid? userId,
        CancellationToken ct = default);

    Task<bool> DeleteForOwnerAsync(
        Guid tenantId,
        string ownerType,
        Guid ownerId,
        string assetType,
        CancellationToken ct = default);
}
