using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Api.Integrations.Keycloak;
using Api.Services.BffSession;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Orkyo.Shared.Keycloak;

namespace Api.Security;

/// <summary>
/// Authentication handler that validates BFF session cookies.
/// Produces a <see cref="ClaimsPrincipal"/> with the same claims as JWT Bearer
/// so that <see cref="KeycloakTokenProfile.FromPrincipal"/> works identically.
/// </summary>
public sealed class BffCookieAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "BffCookie";

    /// <summary>How far before expiry to trigger a proactive token refresh.</summary>
    private static readonly TimeSpan RefreshWindow = TimeSpan.FromSeconds(60);

    private readonly IBffSessionStore _sessionStore;
    private readonly IDataProtector _protector;
    private readonly Configuration.BffOptions _bffOptions;
    private readonly KeycloakOptions _keycloakOptions;
    private readonly IHttpClientFactory _httpClientFactory;

    public BffCookieAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IBffSessionStore sessionStore,
        IDataProtectionProvider dataProtection,
        IOptions<Configuration.BffOptions> bffOptions,
        KeycloakOptions keycloakOptions,
        IHttpClientFactory httpClientFactory)
        : base(options, logger, encoder)
    {
        _sessionStore = sessionStore;
        _protector = dataProtection.CreateProtector("BffSession");
        _bffOptions = bffOptions.Value;
        _keycloakOptions = keycloakOptions;
        _httpClientFactory = httpClientFactory;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var cookieValue = Request.Cookies[_bffOptions.CookieName];
        if (string.IsNullOrEmpty(cookieValue))
            return AuthenticateResult.NoResult();

        // Decrypt the cookie to get the session ID
        string sessionId;
        try
        {
            sessionId = _protector.Unprotect(cookieValue);
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "BFF cookie decryption failed");
            return AuthenticateResult.Fail("Invalid session cookie");
        }

        // Look up the session
        var session = await _sessionStore.GetAsync(sessionId, Context.RequestAborted);
        if (session is null)
            return AuthenticateResult.Fail("Session not found or expired");

        // Proactive token refresh if access token is nearing expiry
        var accessToken = session.AccessToken;
        if (session.TokenExpiresAt - DateTimeOffset.UtcNow < RefreshWindow)
        {
            var refreshed = await TryRefreshTokensAsync(session);
            if (refreshed is not null)
            {
                accessToken = refreshed.AccessToken;
            }
            else
            {
                // Refresh failed — session is still valid until overall session expiry
                Logger.LogWarning("BFF token refresh failed for session {SessionIdPrefix}…", sessionId[..8]);
            }
        }

        // Parse the access token to extract claims (no signature validation —
        // the token was validated at exchange time and is stored server-side)
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        JwtSecurityToken jwt;
        try
        {
            jwt = handler.ReadJwtToken(accessToken);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to parse stored access token for session {SessionIdPrefix}…", sessionId[..8]);
            return AuthenticateResult.Fail("Invalid stored access token");
        }

        // Build claims identity matching JWT Bearer output exactly
        var claims = new List<Claim>();
        foreach (var claim in jwt.Claims)
        {
            // Skip JWT-internal claims that aren't useful for the principal
            if (claim.Type is "nbf" or "jti" or "iat" or "exp" or "typ")
                continue;
            claims.Add(new Claim(claim.Type, claim.Value, claim.ValueType));
        }

        var identity = new ClaimsIdentity(claims, SchemeName, "preferred_username", "realm_access.roles");
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        return AuthenticateResult.Success(ticket);
    }

    private async Task<BffSessionRecord?> TryRefreshTokensAsync(BffSessionRecord session)
    {
        try
        {
            var tokenEndpoint = $"{_keycloakOptions.InternalAuthority}/protocol/openid-connect/token";
            var client = _httpClientFactory.CreateClient("BffKeycloak");

            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["client_id"] = _keycloakOptions.BackendClientId,
                ["client_secret"] = _keycloakOptions.BackendClientSecret,
                ["refresh_token"] = session.RefreshToken,
            });

            var response = await client.PostAsync(tokenEndpoint, content, Context.RequestAborted);
            if (!response.IsSuccessStatusCode)
            {
                Logger.LogWarning("Keycloak token refresh returned {StatusCode}", response.StatusCode);
                return null;
            }

            using var doc = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(Context.RequestAborted),
                cancellationToken: Context.RequestAborted);

            var root = doc.RootElement;
            var newAccessToken = root.GetProperty("access_token").GetString()!;
            var newRefreshToken = root.GetProperty("refresh_token").GetString()!;
            var expiresIn = root.GetProperty("expires_in").GetInt32();
            var newTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn);

            await _sessionStore.RefreshTokensAsync(
                session.SessionId, newAccessToken, newRefreshToken, newTokenExpiresAt, Context.RequestAborted);

            return session with
            {
                AccessToken = newAccessToken,
                RefreshToken = newRefreshToken,
                TokenExpiresAt = newTokenExpiresAt,
            };
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error refreshing tokens for session {SessionIdPrefix}…",
                session.SessionId[..Math.Min(8, session.SessionId.Length)]);
            return null;
        }
    }
}
