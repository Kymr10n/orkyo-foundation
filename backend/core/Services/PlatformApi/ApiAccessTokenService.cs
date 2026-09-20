using Api.Configuration;
using Api.Helpers;
using Api.Models;
using Api.Security;
using Api.Services.Tokens;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Orkyo.Shared;

namespace Api.Services.PlatformApi;

public record ApiAccessTokenRecord : TokenRecordBase
{
    /// <summary>The tenant role this token acts with — see <see cref="PlatformApiScopes"/>.</summary>
    public TenantRole EffectiveRole => PlatformApiScopes.ScopeToRole(Scopes);
}

/// <summary>DTO returned when listing tokens — never exposes hash or secret.</summary>
public record ApiAccessTokenSummary : TokenSummaryBase
{
}

/// <summary>Returned once at creation — the raw secret is never stored.</summary>
public record CreatedApiAccessToken
{
    public ApiAccessTokenSummary Summary { get; init; } = null!;
    /// <summary>Full token string: <c>orkyo_api_{prefix}_{secret}</c>. Show once, never again.</summary>
    public string RawToken { get; init; } = "";
}

public interface IApiAccessTokenService
{
    /// <summary>Creates a token. Throws <see cref="ArgumentException"/> on an unknown scope.</summary>
    Task<CreatedApiAccessToken> CreateAsync(
        Guid tenantId,
        string name,
        IReadOnlyList<string> scopes,
        DateTime? expiresAt,
        Guid? createdByUserId,
        CancellationToken ct = default);

    Task<IReadOnlyList<ApiAccessTokenSummary>> ListForTenantAsync(
        Guid tenantId,
        CancellationToken ct = default);

    Task<bool> RevokeAsync(
        Guid tokenId,
        Guid tenantId,
        Guid? revokedByUserId,
        CancellationToken ct = default);

    /// <summary>Validates a raw token string. Returns the record on success, null otherwise.</summary>
    Task<ApiAccessTokenRecord?> ValidateAsync(string rawToken, CancellationToken ct = default);

    /// <summary>Updates last_used_at asynchronously (fire-and-forget from the auth handler).</summary>
    Task TouchLastUsedAsync(Guid tokenId, CancellationToken ct = default);
}

/// <summary>
/// Write-capable, per-tenant API credentials — the credential class behind the MCP server, and the
/// general mechanism for any future automated integration.
///
/// Deliberately separate from <c>ReportingTokenService</c> rather than a scope added to it: the two
/// are different trust classes. Reporting tokens are audited, revoked and reasoned about as
/// read-only, and folding a write-capable credential into the same table and scheme would mean an
/// auditor could no longer answer "can this token change anything?" from the credential's class
/// alone. The token format and crypto are shared through <see cref="TokenCredentialHelper"/>; the
/// trust boundary is not.
/// </summary>
public sealed class ApiAccessTokenService : IApiAccessTokenService
{
    private const string TokenScheme = "orkyo_api";

    private readonly IDbConnectionFactory _db;
    private readonly byte[] _pepper;
    private readonly ILogger<ApiAccessTokenService> _logger;
    private readonly TimeProvider _time;

    public ApiAccessTokenService(
        IDbConnectionFactory db,
        IConfiguration configuration,
        ILogger<ApiAccessTokenService> logger,
        TimeProvider time)
    {
        _db = db;
        _logger = logger;
        _time = time;
        // Its own pepper, falling back to the Keycloak client secret exactly as reporting does.
        // Keeping the keys distinct means a leak of one credential class's pepper does not also
        // make the write-capable class's stored hashes forgeable.
        _pepper = TokenCredentialHelper.ResolvePepper(
            configuration.IsSet(ConfigKeys.ApiAccessTokenPepper)
                ? configuration[ConfigKeys.ApiAccessTokenPepper]
                : null,
            configuration[ConfigKeys.KeycloakBackendClientSecret],
            $"ApiAccessTokenService: neither '{ConfigKeys.ApiAccessTokenPepper}' nor "
            + $"'{ConfigKeys.KeycloakBackendClientSecret}' is set");
    }

    public async Task<CreatedApiAccessToken> CreateAsync(
        Guid tenantId, string name, IReadOnlyList<string> scopes, DateTime? expiresAt,
        Guid? createdByUserId, CancellationToken ct = default)
    {
        if (scopes.Count == 0)
            throw new ArgumentException("At least one scope is required.", nameof(scopes));
        if (!PlatformApiScopes.AreAllKnown(scopes))
            throw new ArgumentException(
                $"Unknown scope(s): {string.Join(", ", scopes.Where(s => !PlatformApiScopes.All.Contains(s)))}",
                nameof(scopes));

        var scopeString = PlatformApiScopes.Join(scopes.Distinct(StringComparer.Ordinal));
        var (rawToken, prefix, hash) = TokenCredentialHelper.Generate(TokenScheme, _pepper);

        await using var conn = _db.CreateControlPlaneConnection();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(@"
            INSERT INTO api_access_tokens
                (tenant_id, name, token_prefix, token_hash, scopes, created_by_user_id, expires_at)
            VALUES (@tenantId, @name, @prefix, @hash, @scopes, @createdBy, @expires)
            RETURNING id, created_at", conn);

        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("name", name);
        cmd.Parameters.AddWithValue("prefix", prefix);
        cmd.Parameters.AddWithValue("hash", hash);
        cmd.Parameters.AddWithValue("scopes", scopeString);
        cmd.Parameters.AddWithValue("createdBy", createdByUserId.HasValue ? (object)createdByUserId.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("expires", expiresAt.HasValue ? (object)expiresAt.Value : DBNull.Value);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        var id = reader.GetGuid("id");
        var createdAt = reader.GetDateTime("created_at");

        var summary = new ApiAccessTokenSummary
        {
            Id = id,
            TenantId = tenantId,
            Name = name,
            TokenPrefix = prefix,
            Scopes = scopeString,
            CreatedAtUtc = createdAt,
            CreatedByUserId = createdByUserId,
            ExpiresAtUtc = expiresAt,
            IsActive = true,
        };

        return new CreatedApiAccessToken { Summary = summary, RawToken = rawToken };
    }

    public async Task<IReadOnlyList<ApiAccessTokenSummary>> ListForTenantAsync(
        Guid tenantId, CancellationToken ct = default)
    {
        await using var conn = _db.CreateControlPlaneConnection();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($@"
            SELECT {TokenRowMapper.SummaryColumns}
            FROM api_access_tokens
            WHERE tenant_id = @tenantId
            ORDER BY created_at DESC", conn);
        cmd.Parameters.AddWithValue("tenantId", tenantId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<ApiAccessTokenSummary>();
        while (await reader.ReadAsync(ct))
            results.Add(TokenRowMapper.MapSummary<ApiAccessTokenSummary>(reader, _time.GetUtcNow().UtcDateTime));
        return results;
    }

    public async Task<bool> RevokeAsync(
        Guid tokenId, Guid tenantId, Guid? revokedByUserId, CancellationToken ct = default)
    {
        await using var conn = _db.CreateControlPlaneConnection();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(@"
            UPDATE api_access_tokens
            SET revoked_at = NOW(), revoked_by_user_id = @revokedBy
            WHERE id = @id AND tenant_id = @tenantId AND revoked_at IS NULL", conn);
        cmd.Parameters.AddWithValue("id", tokenId);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("revokedBy", revokedByUserId.HasValue ? (object)revokedByUserId.Value : DBNull.Value);

        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    public async Task<ApiAccessTokenRecord?> ValidateAsync(string rawToken, CancellationToken ct = default)
    {
        if (!TokenCredentialHelper.TryParse(rawToken, TokenScheme, out var prefix, out var secretBytes))
            return null;

        await using var conn = _db.CreateControlPlaneConnection();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($@"
            SELECT {TokenRowMapper.RecordColumns}
            FROM api_access_tokens
            WHERE token_prefix = @prefix", conn);
        cmd.Parameters.AddWithValue("prefix", prefix);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;

        var record = TokenRowMapper.MapRecord<ApiAccessTokenRecord>(reader);

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
                "UPDATE api_access_tokens SET last_used_at = NOW() WHERE id = @id", conn);
            cmd.Parameters.AddWithValue("id", tokenId);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update last_used_at for API access token {TokenId}", tokenId);
        }
    }
}
