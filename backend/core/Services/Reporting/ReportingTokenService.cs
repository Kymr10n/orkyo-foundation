using System.Security.Cryptography;
using System.Text;
using Api.Models;
using Microsoft.Extensions.Configuration;
using Npgsql;
using NpgsqlTypes;

namespace Api.Services.Reporting;

public record ReportingTokenRecord
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }
    public string Name { get; init; } = "";
    public string TokenPrefix { get; init; } = "";
    public string TokenHash { get; init; } = "";
    public string Scopes { get; init; } = "reporting:read";
    public DateTime CreatedAtUtc { get; init; }
    public Guid? CreatedByUserId { get; init; }
    public DateTime? LastUsedAtUtc { get; init; }
    public DateTime? ExpiresAtUtc { get; init; }
    public DateTime? RevokedAtUtc { get; init; }

    public bool IsActive =>
        RevokedAtUtc is null && (ExpiresAtUtc is null || ExpiresAtUtc > DateTime.UtcNow);
}

/// <summary>DTO returned when listing tokens — never exposes hash or secret.</summary>
public record ReportingTokenSummary
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }
    public string Name { get; init; } = "";
    public string TokenPrefix { get; init; } = "";
    public string Scopes { get; init; } = "reporting:read";
    public DateTime CreatedAtUtc { get; init; }
    public Guid? CreatedByUserId { get; init; }
    public DateTime? LastUsedAtUtc { get; init; }
    public DateTime? ExpiresAtUtc { get; init; }
    public DateTime? RevokedAtUtc { get; init; }
    public bool IsActive { get; init; }
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
    private const int PrefixLength = 8;
    private const int SecretByteLength = 32;

    private readonly IDbConnectionFactory _db;
    private readonly byte[] _pepper;
    private readonly ILogger<ReportingTokenService> _logger;

    public ReportingTokenService(
        IDbConnectionFactory db,
        IConfiguration configuration,
        ILogger<ReportingTokenService> logger)
    {
        _db = db;
        _logger = logger;
        var pepperValue = configuration["REPORTING_TOKEN_PEPPER"]
            ?? configuration["KEYCLOAK_BACKEND_CLIENT_SECRET"]
            ?? "dev-only-insecure-pepper-change-in-prod";
        _pepper = Encoding.UTF8.GetBytes(pepperValue);
    }

    public async Task<CreatedReportingToken> CreateAsync(
        Guid tenantId, string name, DateTime? expiresAt, Guid? createdByUserId,
        CancellationToken ct = default)
    {
        var prefix = GeneratePrefix();
        var secretBytes = RandomNumberGenerator.GetBytes(SecretByteLength);
        var rawToken = $"{TokenScheme}_{prefix}_{Base64UrlEncode(secretBytes)}";
        var hash = ComputeHash(secretBytes);

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
        var id = reader.GetGuid(0);
        var createdAt = reader.GetDateTime(1);

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

        await using var cmd = new NpgsqlCommand(@"
            SELECT id, tenant_id, name, token_prefix, scopes,
                   created_at, created_by_user_id, last_used_at, expires_at, revoked_at
            FROM reporting_api_tokens
            WHERE tenant_id = @tenantId
            ORDER BY created_at DESC", conn);
        cmd.Parameters.AddWithValue("tenantId", tenantId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<ReportingTokenSummary>();
        while (await reader.ReadAsync(ct))
        {
            var revokedAt = reader.IsDBNull(9) ? (DateTime?)null : reader.GetDateTime(9);
            var expiresAt = reader.IsDBNull(8) ? (DateTime?)null : reader.GetDateTime(8);
            results.Add(new ReportingTokenSummary
            {
                Id = reader.GetGuid(0),
                TenantId = reader.GetGuid(1),
                Name = reader.GetString(2),
                TokenPrefix = reader.GetString(3),
                Scopes = reader.GetString(4),
                CreatedAtUtc = reader.GetDateTime(5),
                CreatedByUserId = reader.IsDBNull(6) ? null : reader.GetGuid(6),
                LastUsedAtUtc = reader.IsDBNull(7) ? null : reader.GetDateTime(7),
                ExpiresAtUtc = expiresAt,
                RevokedAtUtc = revokedAt,
                IsActive = revokedAt is null && (expiresAt is null || expiresAt > DateTime.UtcNow),
            });
        }
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
        if (!TryParseToken(rawToken, out var prefix, out var secretBytes))
            return null;

        await using var conn = _db.CreateControlPlaneConnection();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(@"
            SELECT id, tenant_id, name, token_prefix, token_hash, scopes,
                   created_at, created_by_user_id, last_used_at, expires_at, revoked_at
            FROM reporting_api_tokens
            WHERE token_prefix = @prefix", conn);
        cmd.Parameters.AddWithValue("prefix", prefix);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;

        var record = new ReportingTokenRecord
        {
            Id = reader.GetGuid(0),
            TenantId = reader.GetGuid(1),
            Name = reader.GetString(2),
            TokenPrefix = reader.GetString(3),
            TokenHash = reader.GetString(4),
            Scopes = reader.GetString(5),
            CreatedAtUtc = reader.GetDateTime(6),
            CreatedByUserId = reader.IsDBNull(7) ? null : reader.GetGuid(7),
            LastUsedAtUtc = reader.IsDBNull(8) ? null : reader.GetDateTime(8),
            ExpiresAtUtc = reader.IsDBNull(9) ? null : reader.GetDateTime(9),
            RevokedAtUtc = reader.IsDBNull(10) ? null : reader.GetDateTime(10),
        };

        if (!record.IsActive)
            return null;

        var expectedHash = ComputeHash(secretBytes);
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(expectedHash),
                Encoding.UTF8.GetBytes(record.TokenHash)))
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

    // ── Token format helpers ─────────────────────────────────────────────────

    private static bool TryParseToken(string raw, out string prefix, out byte[] secretBytes)
    {
        prefix = "";
        secretBytes = [];

        // Format: orkyo_rpt_{prefix}_{base64url-secret}
        if (!raw.StartsWith(TokenScheme + "_", StringComparison.Ordinal))
            return false;

        var rest = raw[(TokenScheme.Length + 1)..];
        var underscoreIdx = rest.IndexOf('_');
        if (underscoreIdx < 0) return false;

        prefix = rest[..underscoreIdx];
        var secretB64 = rest[(underscoreIdx + 1)..];

        try
        {
            secretBytes = Base64UrlDecode(secretB64);
            return secretBytes.Length == SecretByteLength;
        }
        catch
        {
            return false;
        }
    }

    private string ComputeHash(byte[] secretBytes)
    {
        using var hmac = new HMACSHA256(_pepper);
        var hashBytes = hmac.ComputeHash(secretBytes);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    private static string GeneratePrefix()
    {
        const string chars = "abcdefghijklmnopqrstuvwxyz0123456789";
        Span<byte> buf = stackalloc byte[PrefixLength];
        RandomNumberGenerator.Fill(buf);
        return new string(buf.ToArray().Select(b => chars[b % chars.Length]).ToArray());
    }

    private static string Base64UrlEncode(byte[] data)
        => Convert.ToBase64String(data)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static byte[] Base64UrlDecode(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
        }
        return Convert.FromBase64String(s);
    }
}
