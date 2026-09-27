using System.Text.Encodings.Web;
using Api.Constants;
using Api.Helpers;
using Api.Services.PlatformApi;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Api.PlatformApi.Auth;

/// <summary>
/// Authentication scheme that validates <c>orkyo_api_*</c> API access tokens — the write-capable
/// credential class behind the MCP server. Runs only when an endpoint requires the
/// "ApiAccessToken" policy, so it does not interfere with JWT Bearer / BFF cookie auth, and its
/// prefix check makes it cheaply ignore reporting tokens (and vice versa).
/// </summary>
public sealed class ApiAccessTokenAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IApiAccessTokenService tokenService,
    IServiceScopeFactory scopeFactory)
    : BearerTokenAuthHandler<ApiAccessTokenRecord, IApiAccessTokenService>(
        options, logger, encoder, tokenService, scopeFactory)
{
    public const string SchemeName = "ApiAccessToken";

    /// <summary>Authorization policy name gating API-access endpoints (same literal as the scheme).</summary>
    public const string PolicyName = SchemeName;

    protected override string TokenScheme => ApiAccessTokenService.TokenScheme;
    protected override string TokenKind => "API access token";

    // Read downstream by ContextEnrichmentMiddleware (to build the authorization context) and
    // by the endpoint group's tenant-match filter.
    protected override string RecordItemKey => ApiAccessTokenContextKeys.TokenRecord;
    protected override string TokenIdClaim => ApiAccessTokenContextKeys.TokenIdClaim;
    protected override string TenantIdClaim => ApiAccessTokenContextKeys.TenantIdClaim;
    protected override string ScopesClaim => ApiAccessTokenContextKeys.ScopesClaim;
    protected override string TokenPrefixClaim => ApiAccessTokenContextKeys.TokenPrefixClaim;

    // Identity.Name resolves to the token id: the rate limiter partitions on UserOrIpKey, which
    // would otherwise collapse every token behind one NAT'd egress IP into a single bucket.
    protected override string? NameClaimType => ApiAccessTokenContextKeys.TokenIdClaim;

    // Unlike the reporting surface — a versioned contract whose {error, message} bodies external
    // BI tools already depend on — this is a new surface with no frozen shape, so it emits the
    // canonical problem body every other endpoint does.
    protected override Task HandleChallengeAsync(AuthenticationProperties properties) =>
        ErrorResponses.Unauthorized(
            ApiErrorCodes.SessionExpired, "Invalid API access token.").ExecuteAsync(Context);

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties) =>
        ErrorResponses.Forbidden(
            message: "The API access token does not have access to this resource.")
            .ExecuteAsync(Context);
}

public static class ApiAccessTokenContextKeys
{
    public const string TokenRecord = "ApiAccessTokenRecord";
    public const string TokenIdClaim = "api_token_id";
    public const string TenantIdClaim = "api_tenant_id";
    public const string ScopesClaim = "api_scopes";
    public const string TokenPrefixClaim = "api_token_prefix";
}
