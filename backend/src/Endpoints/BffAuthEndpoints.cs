using System.Diagnostics.CodeAnalysis;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Api.Configuration;
using Api.Helpers;
using Api.Integrations.Keycloak;
using Api.Middleware;
using Api.Security;
using Orkyo.Shared;
using Api.Services;
using Api.Services.BffSession;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Orkyo.Shared.Keycloak;

namespace Api.Endpoints;

public static class BffAuthEndpoints
{
    private const int PkceVerifierLength = 32;
    private const int StateLength = 32;
    private const int CsrfTokenLength = 32;
    private static readonly TimeSpan StateTtl = TimePolicyConstants.BffPkceStateTtl;
    private const string DataProtectionPurpose = "BffSession";

    // ILogger<T> requires a non-static type argument. This private marker class
    // gives log entries the category "Api.Endpoints.BffAuthEndpoints+Log".
    private sealed class Log { }

    public static void MapBffAuthEndpoints(this WebApplication app)
    {
        if (!string.Equals(app.Configuration[ConfigKeys.BffEnabled], "true", StringComparison.OrdinalIgnoreCase))
            return;

        var bff = app.MapGroup("/api/auth/bff")
            .AllowAnonymous()
            .WithMetadata(new SkipTenantResolutionAttribute())
            .WithTags("BFF Auth");

        // GET /api/auth/bff/login?returnTo=
        bff.MapGet("/login", HandleLogin)
            .WithName("BffLogin")
            .WithSummary("Initiate BFF OIDC login");

        // GET /api/auth/bff/callback?code=&state=
        bff.MapGet("/callback", HandleCallback)
            .WithName("BffCallback")
            .WithSummary("OIDC callback handler for BFF flow");

        // GET-based logout — the SPA navigates here via window.location.href.
        // The server clears the session and issues a 302 redirect to Keycloak's
        // end-session endpoint directly. id_token_hint stays in the server-generated
        // redirect header and never surfaces in a JSON response visible to JS.
        bff.MapGet("/logout", HandleLogout)
            .WithName("BffLogout")
            .WithSummary("Logout: clears BFF session and redirects to Keycloak end-session");

        // Reject non-GET methods on /logout with 405
        bff.MapMethods("/logout", ["POST", "PUT", "PATCH", "DELETE"],
            () => Results.StatusCode(StatusCodes.Status405MethodNotAllowed))
            .WithName("BffLogoutMethodNotAllowed")
            .ExcludeFromDescription();

        // GET /api/auth/bff/me — anonymous; returns { authenticated: false } when no session
        bff.MapGet("/me", HandleMe)
            .WithName("BffMe")
            .WithSummary("Get current user info via BFF session");
    }

    // ── Handlers ─────────────────────────────────────────────────────────────

    private static Task<IResult> HandleLogin(
        string? returnTo,
        IOptions<BffOptions> bffOpts,
        KeycloakOptions keycloakOptions,
        IBffPkceStateStore pkceStore,
        ILogger<Log> logger)
    {
        return EndpointHelpers.ExecuteAsync(async () =>
        {
            var bffOptions = bffOpts.Value;

            returnTo ??= $"{bffOptions.GetDefaultReturnToBase()}/";

            if (!bffOptions.IsReturnToAllowed(returnTo))
            {
                logger.LogWarning("BFF login rejected: invalid returnTo={ReturnTo}", returnTo);
                return Results.BadRequest(new { error = "Invalid returnTo URL" });
            }

            var codeVerifier = GenerateRandomBase64Url(PkceVerifierLength);
            var codeChallenge = ComputeCodeChallenge(codeVerifier);
            var state = GenerateRandomHex(StateLength);

            await pkceStore.SetAsync(state, new PkceStateData(codeVerifier, returnTo), StateTtl);

            var queryParams = new Dictionary<string, string?>
            {
                ["response_type"] = "code",
                ["client_id"] = keycloakOptions.BackendClientId,
                ["redirect_uri"] = bffOptions.RedirectUri,
                ["scope"] = bffOptions.Scopes,
                ["state"] = state,
                ["code_challenge"] = codeChallenge,
                ["code_challenge_method"] = "S256",
            };
            var authUrl = QueryHelpers.AddQueryString(
                $"{keycloakOptions.Authority}/protocol/openid-connect/auth", queryParams);

            logger.LogDebug("BFF login redirect to Keycloak, state={StatePrefix}…", state[..8]);
            return Results.Redirect(authUrl);
        }, logger, "BFF login");
    }

    // Covered by the E2E suite (requires a real Keycloak token exchange).
    [ExcludeFromCodeCoverage]
    private static async Task<IResult> HandleCallback(
        string? code,
        string? state,
        HttpContext ctx,
        IOptions<BffOptions> bffOpts,
        KeycloakOptions keycloakOptions,
        IBffPkceStateStore pkceStore,
        IBffSessionStore sessionStore,
        IDataProtectionProvider dataProtection,
        IIdentityLinkService identityLinkService,
        ISessionService sessionService,
        IHttpClientFactory httpClientFactory,
        IOptions<TenantMiddlewareOptions> tenantMiddlewareOpts,
        ILogger<Log> logger)
    {
        var bffOptions = bffOpts.Value;

        try
        {
            if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state))
                return Results.BadRequest(new { error = "Missing code or state parameter" });

            // Atomic get-and-remove — a replayed state returns null immediately
            var pkceState = await pkceStore.GetAndRemoveAsync(state);
            if (pkceState is null)
            {
                logger.LogWarning("BFF callback: state not found, expired, or already consumed");
                return Results.BadRequest(new { error = "Invalid or expired state" });
            }

            var tokenResponse = await ExchangeCodeForTokensAsync(
                httpClientFactory, keycloakOptions, bffOptions, code, pkceState.CodeVerifier, logger);

            if (tokenResponse is null)
                return Results.BadRequest(new { error = "Token exchange failed" });

            // Build a ClaimsPrincipal from the token, then use KeycloakTokenProfile
            // for all claim extraction — the single authoritative claims mapper.
            var principal = BuildClaimsPrincipal(tokenResponse.AccessToken);
            var tokenProfile = KeycloakTokenProfile.FromPrincipal(principal);

            if (!tokenProfile.IsValid || string.IsNullOrEmpty(tokenProfile.Subject))
            {
                logger.LogError("BFF callback: access token missing 'sub' claim");
                return Results.BadRequest(new { error = "Invalid access token" });
            }

            var externalToken = tokenProfile.ToExternalIdentityToken();
            if (externalToken is null)
            {
                logger.LogError("BFF callback: could not build external identity token");
                return Results.BadRequest(new { error = "Invalid access token" });
            }

            var linkResult = await identityLinkService.LinkIdentityAsync(externalToken);
            if (!linkResult.Success || !linkResult.UserId.HasValue)
            {
                logger.LogWarning("BFF identity link failed: {Error}", linkResult.Error);
                return Results.Redirect($"{bffOptions.GetDefaultReturnToBase()}/login?error=identity_link_failed");
            }

            var sessionId = Guid.NewGuid().ToString("N");
            var now = DateTimeOffset.UtcNow;
            var session = new BffSessionRecord
            {
                SessionId = sessionId,
                UserId = linkResult.UserId.Value.ToString(),
                ExternalSubject = tokenProfile.Subject,
                AccessToken = tokenResponse.AccessToken,
                RefreshToken = tokenResponse.RefreshToken,
                IdToken = tokenResponse.IdToken,
                ExpiresAt = now.Add(bffOptions.SessionDuration),
                TokenExpiresAt = now.AddSeconds(tokenResponse.ExpiresInSeconds),
                CreatedAt = now,
                LastActivityAt = now,
            };

            await sessionStore.SetAsync(session);

            var protector = dataProtection.CreateProtector(DataProtectionPurpose);
            ctx.Response.Cookies.Append(bffOptions.CookieName, protector.Protect(sessionId), new CookieOptions
            {
                HttpOnly = true,
                Secure = bffOptions.CookieSecure,
                SameSite = SameSiteMode.Lax,
                Domain = bffOptions.CookieDomain,
                Path = "/",
                MaxAge = bffOptions.SessionDuration,
            });

            // 32-byte (256-bit) CSRF token — NOT HttpOnly so JS can read it
            ctx.Response.Cookies.Append(bffOptions.CsrfCookieName, GenerateRandomHex(CsrfTokenLength), new CookieOptions
            {
                HttpOnly = false,
                Secure = bffOptions.CookieSecure,
                SameSite = SameSiteMode.Lax,
                Domain = bffOptions.CookieDomain,
                Path = "/",
                MaxAge = bffOptions.SessionDuration,
            });

            var returnTo = await ResolvePostLoginRedirectAsync(
                pkceState.ReturnTo,
                linkResult.UserId.Value,
                tokenProfile.IsSiteAdmin,
                bffOptions,
                sessionService,
                tenantMiddlewareOpts.Value,
                logger);

            // Log only the host component — path may contain invite tokens or other PII
            logger.LogInformation("BFF session created for user {UserId}, redirecting to {ReturnToHost}",
                linkResult.UserId.Value, new Uri(returnTo).GetLeftPart(UriPartial.Authority));

            return Results.Redirect(returnTo);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "BFF callback failed unexpectedly");
            return Results.Redirect($"{bffOptions.GetDefaultReturnToBase()}/login?error=auth_failed");
        }
    }

    private static Task<IResult> HandleLogout(
        string? returnTo,
        HttpContext ctx,
        IOptions<BffOptions> bffOpts,
        KeycloakOptions keycloakOptions,
        IBffSessionStore sessionStore,
        IDataProtectionProvider dataProtection,
        ILogger<Log> logger)
    {
        return EndpointHelpers.ExecuteAsync(async () =>
        {
            var bffOptions = bffOpts.Value;
            var cookieValue = ctx.Request.Cookies[bffOptions.CookieName];
            string? idToken = null;

            if (!string.IsNullOrEmpty(cookieValue))
            {
                try
                {
                    var protector = dataProtection.CreateProtector(DataProtectionPurpose);
                    var sessionId = protector.Unprotect(cookieValue);
                    var session = await sessionStore.GetAsync(sessionId);
                    if (session is not null)
                    {
                        idToken = session.IdToken;
                        await sessionStore.RemoveAsync(sessionId);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Error reading session during logout");
                }
            }

            var clearOptions = new CookieOptions { Domain = bffOptions.CookieDomain, Path = "/" };
            ctx.Response.Cookies.Delete(bffOptions.CookieName, clearOptions);
            ctx.Response.Cookies.Delete(bffOptions.CsrfCookieName, clearOptions);

            returnTo ??= bffOptions.GetDefaultReturnToBase();

            var logoutParams = new Dictionary<string, string?>();
            if (!string.IsNullOrEmpty(idToken))
                logoutParams["id_token_hint"] = idToken;
            if (returnTo != null && bffOptions.IsReturnToAllowed(returnTo))
                logoutParams["post_logout_redirect_uri"] = returnTo;

            var endSessionBase = $"{keycloakOptions.Authority}/protocol/openid-connect/logout";
            var keycloakLogoutUrl = logoutParams.Count > 0
                ? QueryHelpers.AddQueryString(endSessionBase, logoutParams)
                : endSessionBase;

            // Server-side redirect — id_token_hint is in the HTTP Location header,
            // never in a JSON response body where JS or logs could capture it.
            return Results.Redirect(keycloakLogoutUrl);
        }, logger, "BFF logout");
    }

    private static Task<IResult> HandleMe(
        HttpContext ctx,
        IIdentityLinkService identityLinkService,
        ISessionService sessionService,
        ILogger<Log> logger)
    {
        return EndpointHelpers.ExecuteAsync(async () =>
        {
            var authResult = await ctx.AuthenticateAsync();
            if (!authResult.Succeeded)
                return Results.Ok(new { authenticated = false });

            var tokenProfile = KeycloakTokenProfile.FromPrincipal(authResult.Principal!);

            if (!tokenProfile.IsValid || string.IsNullOrEmpty(tokenProfile.Subject))
                return Results.Ok(new { authenticated = false });

            var externalToken = tokenProfile.ToExternalIdentityToken();
            if (externalToken is null)
                return Results.Ok(new { authenticated = false });

            // LinkIdentityAsync is intentionally called on this read endpoint.
            // It is idempotent for existing users and ensures the internal user record
            // exists for JWT-bearer callers (e.g. mobile / API clients) that have not
            // gone through the BFF callback flow.
            var linkResult = await identityLinkService.LinkIdentityAsync(externalToken);
            if (!linkResult.Success || !linkResult.UserId.HasValue)
                return Results.Ok(new { authenticated = false });

            var result = await sessionService.BuildSessionResponseAsync(linkResult.UserId.Value);
            if (result is null)
                return Results.Ok(new { authenticated = false });

            return Results.Ok(result with { IsSiteAdmin = tokenProfile.IsSiteAdmin });
        }, logger, "BFF me");
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    [ExcludeFromCodeCoverage]
    private static async Task<TokenResponse?> ExchangeCodeForTokensAsync(
        IHttpClientFactory httpClientFactory,
        KeycloakOptions keycloakOptions,
        BffOptions bffOptions,
        string code,
        string codeVerifier,
        ILogger logger)
    {
        var tokenEndpoint = $"{keycloakOptions.InternalAuthority}/protocol/openid-connect/token";
        var client = httpClientFactory.CreateClient("BffKeycloak");

        var tokenRequest = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = keycloakOptions.BackendClientId,
            ["client_secret"] = keycloakOptions.BackendClientSecret,
            ["code"] = code,
            ["redirect_uri"] = bffOptions.RedirectUri,
            ["code_verifier"] = codeVerifier,
        });

        var response = await client.PostAsync(tokenEndpoint, tokenRequest);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            logger.LogError("Keycloak token exchange failed: {StatusCode} {Body}", response.StatusCode, errorBody);
            return null;
        }

        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = doc.RootElement;

        if (!root.TryGetProperty("access_token", out var atProp) || atProp.ValueKind != JsonValueKind.String ||
            !root.TryGetProperty("refresh_token", out var rtProp) || rtProp.ValueKind != JsonValueKind.String ||
            !root.TryGetProperty("id_token", out var itProp) || itProp.ValueKind != JsonValueKind.String)
        {
            logger.LogError("Keycloak token response missing required fields (access_token / refresh_token / id_token)");
            return null;
        }

        var expiresInSeconds = root.TryGetProperty("expires_in", out var expProp) ? expProp.GetInt32() : 300;

        return new TokenResponse(
            atProp.GetString()!,
            rtProp.GetString()!,
            itProp.GetString()!,
            expiresInSeconds);
    }

    [ExcludeFromCodeCoverage]
    private static ClaimsPrincipal BuildClaimsPrincipal(string accessToken)
    {
        var jwtHandler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        var jwt = jwtHandler.ReadJwtToken(accessToken);
        return new ClaimsPrincipal(new ClaimsIdentity(jwt.Claims, "BffCallback"));
    }

    [ExcludeFromCodeCoverage]
    internal static async Task<string> ResolvePostLoginRedirectAsync(
        string returnTo,
        Guid userId,
        bool isSiteAdmin,
        BffOptions bffOptions,
        ISessionService sessionService,
        TenantMiddlewareOptions tenantOptions,
        ILogger logger)
    {
        var defaultBase = bffOptions.GetDefaultReturnToBase();

        if (isSiteAdmin || defaultBase is null ||
            !returnTo.StartsWith(defaultBase, StringComparison.OrdinalIgnoreCase))
        {
            return returnTo;
        }

        try
        {
            var bootstrap = await sessionService.BuildSessionResponseAsync(userId);
            if (bootstrap?.Tenants.Count != 1)
                return returnTo;

            var slug = bootstrap.Tenants[0].Slug;
            var tenantHost = tenantOptions.BuildTenantHostname(slug);
            if (tenantHost is null)
                return returnTo;

            var scheme = new Uri(defaultBase).Scheme;
            var tenantUrl = $"{scheme}://{tenantHost}/";

            if (!bffOptions.IsReturnToAllowed(tenantUrl))
                return returnTo;

            logger.LogInformation("Single-tenant redirect for user {UserId}: {TenantUrl}", userId, tenantUrl);
            return tenantUrl;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to resolve tenant redirect for user {UserId}, using default returnTo", userId);
            return returnTo;
        }
    }

    private static string GenerateRandomBase64Url(int byteLength)
    {
        var bytes = RandomNumberGenerator.GetBytes(byteLength);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static string GenerateRandomHex(int byteLength)
    {
        var bytes = RandomNumberGenerator.GetBytes(byteLength);
        return Convert.ToHexStringLower(bytes);
    }

    private static string ComputeCodeChallenge(string codeVerifier)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(codeVerifier));
        return Convert.ToBase64String(hash)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private sealed record TokenResponse(
        string AccessToken,
        string RefreshToken,
        string IdToken,
        int ExpiresInSeconds);
}
