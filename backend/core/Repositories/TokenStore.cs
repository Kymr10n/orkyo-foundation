using Api.Helpers;
using Api.Services;
using Api.Services.PlatformApi;
using Api.Services.Tokens;
using Npgsql;

namespace Api.Repositories;

/// <summary>
/// Storage and verification for one bearer-token credential class: create, list, revoke,
/// validate and touch rows in that class's own table, with that class's own scheme and pepper.
/// </summary>
/// <remarks>
/// The reporting and API-access token services each own one instance. They stay separate trust
/// classes — separate tables, schemes, peppers and public services — and share only these
/// mechanics. <paramref name="table"/> is a compile-time constant of the owning service, never
/// caller input.
/// </remarks>
internal sealed class TokenStore<TRecord, TSummary>(
    IDbConnectionFactory connectionFactory,
    TimeProvider time,
    ILogger logger,
    string table,
    string scheme,
    byte[] pepper)
    where TRecord : TokenRecordBase, new()
    where TSummary : TokenSummaryBase, new()
{
    public async Task<(string RawToken, TSummary Summary)> CreateAsync(
        Guid tenantId, string name, string scopes, DateTime? expiresAt, Guid? createdByUserId,
        CancellationToken ct)
    {
        var (rawToken, prefix, hash) = TokenCredentialHelper.Generate(scheme, pepper);

        await using var conn = connectionFactory.CreateControlPlaneConnection();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($@"
            INSERT INTO {table}
                (tenant_id, name, token_prefix, token_hash, scopes, created_by_user_id, expires_at)
            VALUES (@tenantId, @name, @prefix, @hash, @scopes, @createdBy, @expires)
            RETURNING id, created_at", conn);

        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("name", name);
        cmd.Parameters.AddWithValue("prefix", prefix);
        cmd.Parameters.AddWithValue("hash", hash);
        cmd.Parameters.AddWithValue("scopes", scopes);
        cmd.Parameters.AddNullable("createdBy", createdByUserId);
        cmd.Parameters.AddNullable("expires", expiresAt);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);

        var summary = new TSummary
        {
            Id = reader.GetGuid("id"),
            TenantId = tenantId,
            Name = name,
            TokenPrefix = prefix,
            Scopes = scopes,
            CreatedAtUtc = reader.GetDateTime("created_at"),
            CreatedByUserId = createdByUserId,
            ExpiresAtUtc = expiresAt,
            IsActive = true,
        };
        return (rawToken, summary);
    }

    public async Task<IReadOnlyList<TSummary>> ListForTenantAsync(Guid tenantId, CancellationToken ct)
    {
        await using var conn = connectionFactory.CreateControlPlaneConnection();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($@"
            SELECT {TokenRowMapper.SummaryColumns}
            FROM {table}
            WHERE tenant_id = @tenantId
            ORDER BY created_at DESC", conn);
        cmd.Parameters.AddWithValue("tenantId", tenantId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var now = time.GetUtcNow().UtcDateTime;
        var results = new List<TSummary>();
        while (await reader.ReadAsync(ct))
            results.Add(TokenRowMapper.MapSummary<TSummary>(reader, now));
        return results;
    }

    public async Task<bool> RevokeAsync(
        Guid tokenId, Guid tenantId, Guid? revokedByUserId, CancellationToken ct)
    {
        await using var conn = connectionFactory.CreateControlPlaneConnection();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($@"
            UPDATE {table}
            SET revoked_at = NOW(), revoked_by_user_id = @revokedBy
            WHERE id = @id AND tenant_id = @tenantId AND revoked_at IS NULL", conn);
        cmd.Parameters.AddWithValue("id", tokenId);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddNullable("revokedBy", revokedByUserId);

        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    public async Task<TRecord?> ValidateAsync(string rawToken, CancellationToken ct)
    {
        if (!TokenCredentialHelper.TryParse(rawToken, scheme, out var prefix, out var secretBytes))
            return null;

        await using var conn = connectionFactory.CreateControlPlaneConnection();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($@"
            SELECT {TokenRowMapper.RecordColumns}
            FROM {table}
            WHERE token_prefix = @prefix", conn);
        cmd.Parameters.AddWithValue("prefix", prefix);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;

        var record = TokenRowMapper.MapRecord<TRecord>(reader);

        if (!record.IsActive)
            return null;

        var expectedHash = TokenCredentialHelper.ComputeHash(secretBytes, pepper);
        if (!TokenCredentialHelper.HashesMatch(expectedHash, record.TokenHash))
            return null;

        return record;
    }

    public async Task TouchLastUsedAsync(Guid tokenId, CancellationToken ct)
    {
        try
        {
            await using var conn = connectionFactory.CreateControlPlaneConnection();
            await conn.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand(
                $"UPDATE {table} SET last_used_at = NOW() WHERE id = @id", conn);
            cmd.Parameters.AddWithValue("id", tokenId);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        // A cancelled touch is not a failure worth a warning; anything else is best-effort.
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to update last_used_at for token {TokenId}", tokenId);
        }
    }
}
