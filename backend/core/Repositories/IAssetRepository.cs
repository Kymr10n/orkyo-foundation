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

    /// <summary>
    /// Returns the sum of size_bytes for all assets belonging to the tenant.
    /// Used for storage quota enforcement at upload time (live, not from rollup).
    /// </summary>
    Task<long> GetTotalSizeBytesAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Encrypts any legacy plaintext blobs (enc_algorithm IS NULL) in the current tenant
    /// database, in place. Idempotent — a second run finds nothing. Returns the number of
    /// rows encrypted. Reused for key rotation (decrypt-old → encrypt-new) later.
    /// </summary>
    Task<int> EncryptUnencryptedBlobsAsync(CancellationToken ct = default);
}
