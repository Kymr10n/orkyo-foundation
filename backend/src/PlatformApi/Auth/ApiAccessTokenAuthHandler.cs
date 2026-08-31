using System.Security.Claims;
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
public sealed class ApiAccessTokenAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "ApiAccessToken";

    /// <summary>Authorization policy name gating API-access endpoints (same literal as the scheme).</summary>
    public const string PolicyName = SchemeName;

    public const string TokenScheme = "orkyo_api";
    public const string TokenPrefix = TokenScheme + "_";

    private readonly IApiAccessTokenService _tokenService;
    private readonly IServiceScopeFactory _scopeFactory;

    public ApiAccessTokenAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IApiAccessTokenService tokenService,
        IServiceScopeFactory scopeFactory)
        : base(options, logger, encoder)
    {
        _tokenService = tokenService;
        _scopeFactory = scopeFactory;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var authorization = Request.Headers.Authorization.FirstOrDefault();
        if (string.IsNullOrEmpty(authorization) || !authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.NoResult();

        var token = authorization["Bearer ".Length..].Trim();
        if (!token.StartsWith(TokenPrefix, StringComparison.Ordinal))
            return AuthenticateResult.NoResult();

        var record = await _tokenService.ValidateAsync(token, Context.RequestAborted);
        if (record is null)
        {
            Logger.LogWarning("API access token validation failed for prefix {Prefix}",
                TokenCredentialHelper.ExtractPrefix(token, TokenScheme));
            return AuthenticateResult.Fail("Invalid, expired, or revoked API access token.");
        }

        // Read downstream by ContextEnrichmentMiddleware (to build the authorization context) and
        // by the endpoint group's tenant-match filter.
        Context.Items[ApiAccessTokenContextKeys.TokenRecord] = record;

        // Touch last_used_at asynchronously — don't block the request. Through a fresh DI
        // scope, NOT the request's _tokenService: this task outlives the request, whose scope
        // (and its DB connection factory) is disposed the moment the response completes. Using
        // the scoped instance made the update race disposal and silently stop under load — and
        // last_used_at is the field an admin reads to spot a stale or stolen token.
        _ = TouchLastUsedInOwnScopeAsync(record.Id);

        var claims = new[]
        {
            new Claim(ApiAccessTokenContextKeys.TokenIdClaim, record.Id.ToString()),
            new Claim(ApiAccessTokenContextKeys.TenantIdClaim, record.TenantId.ToString()),
            new Claim(ApiAccessTokenContextKeys.ScopesClaim, record.Scopes),
            new Claim(ApiAccessTokenContextKeys.TokenPrefixClaim, record.TokenPrefix),
        };

        // nameType is the token-id claim so Identity.Name resolves to the token id: the rate
        // limiter partitions on UserOrIpKey, which would otherwise collapse every token behind one
        // NAT'd egress IP into a single bucket.
        var identity = new ClaimsIdentity(
            claims, SchemeName, ApiAccessTokenContextKeys.TokenIdClaim, ClaimTypes.Role);
        var principal = new ClaimsPrincipal(identity);
        return AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName));
    }

    /// <summary>Background update with its own scope, so it cannot race request disposal.</summary>
    private async Task TouchLastUsedInOwnScopeAsync(Guid tokenId)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            await scope.ServiceProvider
                .GetRequiredService<IApiAccessTokenService>()
                .TouchLastUsedAsync(tokenId);
        }
        catch (Exception ex)
        {
            // Nothing awaits this task; an escaped exception would only surface as an
            // unobserved-task event. The touch is best-effort by design.
            Logger.LogWarning(ex, "Background last_used_at update failed for API token {TokenId}", tokenId);
        }
    }

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
