using Api.Configuration;
using Api.Helpers;
using Api.Models;
using Api.Services.PlatformApi;
using Api.Services.Tokens;
using Microsoft.Extensions.Configuration;
using Npgsql;
using NpgsqlTypes;
using Orkyo.Shared;

namespace Api.Services.Reporting;

public record ReportingTokenRecord : TokenRecordBase
{
    /// <summary>Reporting tokens are read-only by construction; the column still drives it.</summary>
    public ReportingTokenRecord() => Scopes = "reporting:read";
}

/// <summary>DTO returned when listing tokens — never exposes hash or secret.</summary>
public record ReportingTokenSummary : TokenSummaryBase
{
    public ReportingTokenSummary() => Scopes = "reporting:read";
}

/// <summary>Returned once at creation — the raw secret is never stored.</summary>
public record CreatedReportingToken
{
    public ReportingTokenSummary Summary { get; init; } = null!;
    /// <summary>Full token string: <c>orkyo_rpt_{prefix}_{secret}</c>. Show once, never again.</summary>
    public string RawToken { get; init; } = "";
}

public interface IReportingTokenService
{
    Task<CreatedReportingToken> CreateAsync(
        Guid tenantId,
        string name,
        DateTime? expiresAt,
        Guid? createdByUserId,
        CancellationToken ct = default);

    Task<IReadOnlyList<ReportingTokenSummary>> ListForTenantAsync(
        Guid tenantId,
        CancellationToken ct = default);

    Task<bool> RevokeAsync(
        Guid tokenId,
        Guid tenantId,
        Guid? revokedByUserId,
        CancellationToken ct = default);

    /// <summary>
    /// Validates a raw token string. Returns the record on success, null otherwise.
    /// Called by the auth handler on every reporting request.
    /// </summary>
    Task<ReportingTokenRecord?> ValidateAsync(
        string rawToken,
        CancellationToken ct = default);

    /// <summary>Updates last_used_at asynchronously (fire-and-forget from auth handler).</summary>
    Task TouchLastUsedAsync(Guid tokenId, CancellationToken ct = default);
}

public sealed class ReportingTokenService : IReportingTokenService
{
    private const string TokenScheme = "orkyo_rpt";

    private readonly IDbConnectionFactory _db;
    private readonly byte[] _pepper;
    private readonly ILogger<ReportingTokenService> _logger;
    private readonly TimeProvider _time;

    public ReportingTokenService(
        IDbConnectionFactory db,
        IConfiguration configuration,
        ILogger<ReportingTokenService> logger,
        TimeProvider time)
    {
        _db = db;
        _logger = logger;
        _time = time;
        // Fail early, never fall back: the pepper keys token hashes, and the old chain
        // ended in a literal from this file — a publicly known pepper. An empty value
        // counts as absent (the deploy pipeline writes KEY= for unset keys).
        _pepper = TokenCredentialHelper.ResolvePepper(
            configuration.IsSet(ConfigKeys.ReportingTokenPepper)
                ? configuration[ConfigKeys.ReportingTokenPepper]
                : null,
            configuration[ConfigKeys.KeycloakBackendClientSecret],
            $"ReportingTokenService: neither '{ConfigKeys.ReportingTokenPepper}' nor "
            + $"'{ConfigKeys.KeycloakBackendClientSecret}' is set");
    }

    public async Task<CreatedReportingToken> CreateAsync(
        Guid tenantId, string name, DateTime? expiresAt, Guid? createdByUserId,
        CancellationToken ct = default)
    {
        var (rawToken, prefix, hash) = TokenCredentialHelper.Generate(TokenScheme, _pepper);

        await using var conn = _db.CreateControlPlaneConnection();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(@"
            INSERT INTO reporting_api_tokens
                (tenant_id, name, token_prefix, token_hash, scopes, created_by_user_id, expires_at)
            VALUES (@tenantId, @name, @prefix, @hash, 'reporting:read', @createdBy, @expires)
            RETURNING id, created_at", conn);

        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("name", name);
        cmd.Parameters.AddWithValue("prefix", prefix);
        cmd.Parameters.AddWithValue("hash", hash);
        cmd.Parameters.AddWithValue("createdBy", createdByUserId.HasValue ? (object)createdByUserId.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("expires", expiresAt.HasValue ? (object)expiresAt.Value : DBNull.Value);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        var id = reader.GetGuid("id");
        var createdAt = reader.GetDateTime("created_at");

        var summary = new ReportingTokenSummary
        {
            Id = id,
            TenantId = tenantId,
            Name = name,
            TokenPrefix = prefix,
            Scopes = "reporting:read",
            CreatedAtUtc = createdAt,
            CreatedByUserId = createdByUserId,
            ExpiresAtUtc = expiresAt,
            IsActive = true,
        };

        return new CreatedReportingToken { Summary = summary, RawToken = rawToken };
    }

    public async Task<IReadOnlyList<ReportingTokenSummary>> ListForTenantAsync(
        Guid tenantId, CancellationToken ct = default)
    {
        await using var conn = _db.CreateControlPlaneConnection();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($@"
            SELECT {TokenRowMapper.SummaryColumns}
            FROM reporting_api_tokens
            WHERE tenant_id = @tenantId
            ORDER BY created_at DESC", conn);
        cmd.Parameters.AddWithValue("tenantId", tenantId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<ReportingTokenSummary>();
        while (await reader.ReadAsync(ct))
            results.Add(TokenRowMapper.MapSummary<ReportingTokenSummary>(reader, _time.GetUtcNow().UtcDateTime));
        return results;
    }

    public async Task<bool> RevokeAsync(
        Guid tokenId, Guid tenantId, Guid? revokedByUserId, CancellationToken ct = default)
    {
        await using var conn = _db.CreateControlPlaneConnection();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(@"
            UPDATE reporting_api_tokens
            SET revoked_at = NOW(), revoked_by_user_id = @revokedBy
            WHERE id = @id AND tenant_id = @tenantId AND revoked_at IS NULL", conn);
        cmd.Parameters.AddWithValue("id", tokenId);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("revokedBy", revokedByUserId.HasValue ? (object)revokedByUserId.Value : DBNull.Value);

        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    public async Task<ReportingTokenRecord?> ValidateAsync(string rawToken, CancellationToken ct = default)
    {
        if (!TokenCredentialHelper.TryParse(rawToken, TokenScheme, out var prefix, out var secretBytes))
            return null;

        await using var conn = _db.CreateControlPlaneConnection();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($@"
            SELECT {TokenRowMapper.RecordColumns}
            FROM reporting_api_tokens
            WHERE token_prefix = @prefix", conn);
        cmd.Parameters.AddWithValue("prefix", prefix);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;

        var record = TokenRowMapper.MapRecord<ReportingTokenRecord>(reader);

        if (!record.IsActive)
            return null;

        var expectedHash = TokenCredentialHelper.ComputeHash(secretBytes, _pepper);
        if (!TokenCredentialHelper.HashesMatch(expectedHash, record.TokenHash))
            return null;

        return record;
    }

    public async Task TouchLastUsedAsync(Guid tokenId, CancellationToken ct = default)
    {
        try
        {
            await using var conn = _db.CreateControlPlaneConnection();
            await conn.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand(
                "UPDATE reporting_api_tokens SET last_used_at = NOW() WHERE id = @id", conn);
            cmd.Parameters.AddWithValue("id", tokenId);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update last_used_at for reporting token {TokenId}", tokenId);
        }
    }

}
