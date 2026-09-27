using Api.Configuration;
using Api.Models;
using Api.Repositories;
using Api.Security;
using Api.Services.Tokens;
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

public interface IApiAccessTokenService : ITokenVerifier<ApiAccessTokenRecord>
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
    /// <summary>The token scheme: every API access token reads <c>orkyo_api_{prefix}_{secret}</c>.</summary>
    public const string TokenScheme = "orkyo_api";

    private readonly TokenStore<ApiAccessTokenRecord, ApiAccessTokenSummary> _store;

    public ApiAccessTokenService(
        IDbConnectionFactory db,
        IConfiguration configuration,
        ILogger<ApiAccessTokenService> logger,
        TimeProvider time)
    {
        // Its own pepper, falling back to the Keycloak client secret exactly as reporting does.
        // Keeping the keys distinct means a leak of one credential class's pepper does not also
        // make the write-capable class's stored hashes forgeable.
        var pepper = TokenCredentialHelper.ResolvePepper(
            configuration.IsSet(ConfigKeys.ApiAccessTokenPepper)
                ? configuration[ConfigKeys.ApiAccessTokenPepper]
                : null,
            configuration[ConfigKeys.KeycloakBackendClientSecret],
            $"ApiAccessTokenService: neither '{ConfigKeys.ApiAccessTokenPepper}' nor "
            + $"'{ConfigKeys.KeycloakBackendClientSecret}' is set");
        _store = new(db, time, logger, "api_access_tokens", TokenScheme, pepper);
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
        var (rawToken, summary) = await _store.CreateAsync(tenantId, name, scopeString, expiresAt, createdByUserId, ct);
        return new CreatedApiAccessToken { Summary = summary, RawToken = rawToken };
    }

    public Task<IReadOnlyList<ApiAccessTokenSummary>> ListForTenantAsync(
        Guid tenantId, CancellationToken ct = default) => _store.ListForTenantAsync(tenantId, ct);

    public Task<bool> RevokeAsync(
        Guid tokenId, Guid tenantId, Guid? revokedByUserId, CancellationToken ct = default)
        => _store.RevokeAsync(tokenId, tenantId, revokedByUserId, ct);

    public Task<ApiAccessTokenRecord?> ValidateAsync(string rawToken, CancellationToken ct = default)
        => _store.ValidateAsync(rawToken, ct);

    public Task TouchLastUsedAsync(Guid tokenId, CancellationToken ct = default)
        => _store.TouchLastUsedAsync(tokenId, ct);
}
