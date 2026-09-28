using Api.Configuration;
using Api.Repositories;
using Api.Services.PlatformApi;
using Api.Services.Tokens;
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

public interface IReportingTokenService : ITokenVerifier<ReportingTokenRecord>
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
}

public sealed class ReportingTokenService : IReportingTokenService
{
    /// <summary>The token scheme: every reporting token reads <c>orkyo_rpt_{prefix}_{secret}</c>.</summary>
    public const string TokenScheme = "orkyo_rpt";

    private const string Scopes = "reporting:read";

    private readonly TokenStore<ReportingTokenRecord, ReportingTokenSummary> _store;

    public ReportingTokenService(
        IDbConnectionFactory db,
        IConfiguration configuration,
        ILogger<ReportingTokenService> logger,
        TimeProvider time)
    {
        // The pepper keys token hashes. It falls back to the Keycloak backend client secret and
        // fails at startup when neither is set — never to a literal: the old chain ended in one
        // from this file, a publicly known pepper. An empty value counts as absent (the deploy
        // pipeline writes KEY= for unset keys).
        var pepper = TokenCredentialHelper.ResolvePepper(
            configuration.GetNonEmptyOrNull(ConfigKeys.ReportingTokenPepper),
            configuration[ConfigKeys.KeycloakBackendClientSecret],
            $"ReportingTokenService: neither '{ConfigKeys.ReportingTokenPepper}' nor "
            + $"'{ConfigKeys.KeycloakBackendClientSecret}' is set");
        _store = new(db, time, logger, "reporting_api_tokens", TokenScheme, pepper);
    }

    public async Task<CreatedReportingToken> CreateAsync(
        Guid tenantId, string name, DateTime? expiresAt, Guid? createdByUserId,
        CancellationToken ct = default)
    {
        var (rawToken, summary) = await _store.CreateAsync(tenantId, name, Scopes, expiresAt, createdByUserId, ct);
        return new CreatedReportingToken { Summary = summary, RawToken = rawToken };
    }

    public Task<IReadOnlyList<ReportingTokenSummary>> ListForTenantAsync(
        Guid tenantId, CancellationToken ct = default) => _store.ListForTenantAsync(tenantId, ct);

    public Task<bool> RevokeAsync(
        Guid tokenId, Guid tenantId, Guid? revokedByUserId, CancellationToken ct = default)
        => _store.RevokeAsync(tokenId, tenantId, revokedByUserId, ct);

    public Task<ReportingTokenRecord?> ValidateAsync(string rawToken, CancellationToken ct = default)
        => _store.ValidateAsync(rawToken, ct);

    public Task TouchLastUsedAsync(Guid tokenId, CancellationToken ct = default)
        => _store.TouchLastUsedAsync(tokenId, ct);
}
