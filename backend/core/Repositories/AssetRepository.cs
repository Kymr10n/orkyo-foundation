using Api.Models;
using Api.Security.Encryption;
using Api.Services;
using Npgsql;

namespace Api.Repositories;

public class AssetRepository(
    OrgContext orgContext,
    IOrgDbConnectionFactory connectionFactory,
    IEncryptionService encryption)
    : IAssetRepository
{
    private const string AssetCols =
        "id, tenant_id, owner_type, owner_id, asset_type, file_name, content_type, " +
        "size_bytes, checksum_sha256, width_px, height_px, storage_kind, external_uri, " +
        "created_at, updated_at, created_by_user_id, updated_by_user_id";

    public async Task<bool> OwnerExistsAsync(string ownerType, Guid ownerId, CancellationToken ct = default)
    {
        if (ownerType != AssetOwnerTypes.Site)
            throw new ArgumentException($"Unsupported asset owner type: {ownerType}", nameof(ownerType));

        await using var conn = connectionFactory.CreateOrgConnection(orgContext);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand("SELECT 1 FROM sites WHERE id = @ownerId", conn);
        cmd.Parameters.AddWithValue("ownerId", ownerId);
        return await cmd.ExecuteScalarAsync(ct) is not null;
    }

    public async Task<AssetInfo?> GetForOwnerAsync(
        Guid tenantId,
        string ownerType,
        Guid ownerId,
        string assetType,
        CancellationToken ct = default)
    {
        await using var conn = connectionFactory.CreateOrgConnection(orgContext);
        await conn.OpenAsync(ct);

        await using var cmd = BuildOwnerAssetCommand(
            $"SELECT {AssetCols} FROM assets", conn, tenantId, ownerType, ownerId, assetType);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? MapAsset(reader) : null;
    }

    public async Task<AssetDownloadInfo?> GetDownloadForOwnerAsync(
        Guid tenantId,
        string ownerType,
        Guid ownerId,
        string assetType,
        CancellationToken ct = default)
    {
        await using var conn = connectionFactory.CreateOrgConnection(orgContext);
        await conn.OpenAsync(ct);

        await using var cmd = BuildOwnerAssetCommand(
            $"SELECT {AssetCols}, data, enc_algorithm FROM assets", conn, tenantId, ownerType, ownerId, assetType);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;

        var dataOrdinal = reader.GetOrdinal("data");
        if (reader.IsDBNull(dataOrdinal)) return null;

        var stored = (byte[])reader.GetValue(dataOrdinal);
        // Encrypted rows (enc_algorithm set) are decrypted; legacy plaintext rows
        // (NULL) are returned as-is until the backfill encrypts them.
        var encrypted = !reader.IsDBNull(reader.GetOrdinal("enc_algorithm"));
        var data = encrypted ? encryption.UnprotectBytes(stored, tenantId) : stored;

        return new AssetDownloadInfo
        {
            Metadata = MapAsset(reader),
            Data = data,
        };
    }

    public async Task<AssetInfo> UpsertPostgresAssetAsync(
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
        CancellationToken ct = default)
    {
        await using var conn = connectionFactory.CreateOrgConnection(orgContext);
        await conn.OpenAsync(ct);

        // Encrypt the blob at rest. size_bytes / checksum stay over PLAINTEXT (user-facing
        // size + ETag/integrity); the stored bytes are the Orkyo binary envelope.
        var plaintextLength = (long)data.Length;
        var encryptedData = encryption.ProtectBytes(data, tenantId);

        await using var cmd = new NpgsqlCommand($@"
            INSERT INTO assets (
                tenant_id, owner_type, owner_id, asset_type, file_name, content_type,
                size_bytes, checksum_sha256, width_px, height_px, storage_kind, data,
                enc_algorithm, enc_key_version,
                external_uri, created_by_user_id, updated_by_user_id
            )
            VALUES (
                @tenantId, @ownerType, @ownerId, @assetType, @fileName, @contentType,
                @sizeBytes, @checksumSha256, @widthPx, @heightPx, @storageKind, @data,
                @encAlgorithm, @encKeyVersion,
                NULL, @userId, @userId
            )
            ON CONFLICT (tenant_id, owner_type, owner_id, asset_type)
            WHERE asset_type = 'floorplan'
            DO UPDATE SET
                file_name = EXCLUDED.file_name,
                content_type = EXCLUDED.content_type,
                size_bytes = EXCLUDED.size_bytes,
                checksum_sha256 = EXCLUDED.checksum_sha256,
                width_px = EXCLUDED.width_px,
                height_px = EXCLUDED.height_px,
                storage_kind = EXCLUDED.storage_kind,
                data = EXCLUDED.data,
                enc_algorithm = EXCLUDED.enc_algorithm,
                enc_key_version = EXCLUDED.enc_key_version,
                external_uri = NULL,
                updated_by_user_id = EXCLUDED.updated_by_user_id,
                updated_at = NOW()
            RETURNING {AssetCols}", conn);

        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("ownerType", ownerType);
        cmd.Parameters.AddWithValue("ownerId", ownerId);
        cmd.Parameters.AddWithValue("assetType", assetType);
        cmd.Parameters.AddWithValue("fileName", fileName);
        cmd.Parameters.AddWithValue("contentType", contentType);
        cmd.Parameters.AddWithValue("sizeBytes", plaintextLength);
        cmd.Parameters.AddWithValue("checksumSha256", checksumSha256);
        cmd.Parameters.AddWithValue("widthPx", (object?)widthPx ?? DBNull.Value);
        cmd.Parameters.AddWithValue("heightPx", (object?)heightPx ?? DBNull.Value);
        cmd.Parameters.AddWithValue("storageKind", AssetStorageKinds.Postgres);
        cmd.Parameters.AddWithValue("data", encryptedData);
        cmd.Parameters.AddWithValue("encAlgorithm", encryption.Algorithm);
        cmd.Parameters.AddWithValue("encKeyVersion", encryption.KeyVersion);
        cmd.Parameters.AddWithValue("userId", (object?)userId ?? DBNull.Value);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return MapAsset(reader);
    }

    public async Task<bool> DeleteForOwnerAsync(
        Guid tenantId,
        string ownerType,
        Guid ownerId,
        string assetType,
        CancellationToken ct = default)
    {
        await using var conn = connectionFactory.CreateOrgConnection(orgContext);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(@"
            DELETE FROM assets
            WHERE tenant_id = @tenantId
              AND owner_type = @ownerType
              AND owner_id = @ownerId
              AND asset_type = @assetType", conn);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("ownerType", ownerType);
        cmd.Parameters.AddWithValue("ownerId", ownerId);
        cmd.Parameters.AddWithValue("assetType", assetType);
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    public async Task<long> GetTotalSizeBytesAsync(Guid tenantId, CancellationToken ct = default)
    {
        await using var conn = connectionFactory.CreateOrgConnection(orgContext);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(
            "SELECT COALESCE(SUM(size_bytes), 0)::bigint FROM assets WHERE tenant_id = @tenantId", conn);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is DBNull or null ? 0L : Convert.ToInt64(result);
    }

    public async Task<int> EncryptUnencryptedBlobsAsync(CancellationToken ct = default)
    {
        await using var conn = connectionFactory.CreateOrgConnection(orgContext);
        await conn.OpenAsync(ct);

        // Read legacy plaintext rows.
        var rows = new List<(Guid id, Guid tenantId, byte[] data)>();
        await using (var select = new NpgsqlCommand(
            "SELECT id, tenant_id, data FROM assets WHERE enc_algorithm IS NULL AND data IS NOT NULL", conn))
        await using (var reader = await select.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
                rows.Add((reader.GetGuid(0), reader.GetGuid(1), (byte[])reader.GetValue(2)));
        }

        var count = 0;
        foreach (var (id, tenantId, data) in rows)
        {
            var encrypted = encryption.ProtectBytes(data, tenantId);
            await using var update = new NpgsqlCommand(
                @"UPDATE assets
                  SET data = @data, enc_algorithm = @encAlgorithm, enc_key_version = @encKeyVersion
                  WHERE id = @id AND enc_algorithm IS NULL", conn);
            update.Parameters.AddWithValue("data", encrypted);
            update.Parameters.AddWithValue("encAlgorithm", encryption.Algorithm);
            update.Parameters.AddWithValue("encKeyVersion", encryption.KeyVersion);
            update.Parameters.AddWithValue("id", id);
            count += await update.ExecuteNonQueryAsync(ct);
        }
        return count;
    }

    private static NpgsqlCommand BuildOwnerAssetCommand(
        string selectClause,
        NpgsqlConnection conn,
        Guid tenantId,
        string ownerType,
        Guid ownerId,
        string assetType)
    {
        var cmd = new NpgsqlCommand($@"
            {selectClause}
            WHERE tenant_id = @tenantId
              AND owner_type = @ownerType
              AND owner_id = @ownerId
              AND asset_type = @assetType", conn);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("ownerType", ownerType);
        cmd.Parameters.AddWithValue("ownerId", ownerId);
        cmd.Parameters.AddWithValue("assetType", assetType);
        return cmd;
    }

    private static AssetInfo MapAsset(NpgsqlDataReader reader) => new()
    {
        Id = reader.GetGuid(reader.GetOrdinal("id")),
        TenantId = reader.GetGuid(reader.GetOrdinal("tenant_id")),
        OwnerType = reader.GetString(reader.GetOrdinal("owner_type")),
        OwnerId = reader.GetGuid(reader.GetOrdinal("owner_id")),
        AssetType = reader.GetString(reader.GetOrdinal("asset_type")),
        FileName = reader.GetString(reader.GetOrdinal("file_name")),
        ContentType = reader.GetString(reader.GetOrdinal("content_type")),
        SizeBytes = reader.GetInt64(reader.GetOrdinal("size_bytes")),
        ChecksumSha256 = reader.GetString(reader.GetOrdinal("checksum_sha256")),
        WidthPx = reader.IsDBNull(reader.GetOrdinal("width_px")) ? null : reader.GetInt32(reader.GetOrdinal("width_px")),
        HeightPx = reader.IsDBNull(reader.GetOrdinal("height_px")) ? null : reader.GetInt32(reader.GetOrdinal("height_px")),
        StorageKind = reader.GetString(reader.GetOrdinal("storage_kind")),
        ExternalUri = reader.IsDBNull(reader.GetOrdinal("external_uri")) ? null : reader.GetString(reader.GetOrdinal("external_uri")),
        CreatedAt = DateTime.SpecifyKind(reader.GetDateTime(reader.GetOrdinal("created_at")), DateTimeKind.Utc),
        UpdatedAt = DateTime.SpecifyKind(reader.GetDateTime(reader.GetOrdinal("updated_at")), DateTimeKind.Utc),
        CreatedByUserId = reader.IsDBNull(reader.GetOrdinal("created_by_user_id")) ? null : reader.GetGuid(reader.GetOrdinal("created_by_user_id")),
        UpdatedByUserId = reader.IsDBNull(reader.GetOrdinal("updated_by_user_id")) ? null : reader.GetGuid(reader.GetOrdinal("updated_by_user_id")),
    };
}
