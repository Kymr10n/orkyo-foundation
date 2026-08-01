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

    /// <summary>
    /// Orkyo-internal claim carrying <see cref="BffSessionRecord.AuthClient"/> — the secondary
    /// OAuth client a session was established through (null for ordinary logins). Namespaced so
    /// it can never collide with a Keycloak claim; deliberately not in <c>KeycloakClaims</c>,
    /// which is strictly the Keycloak JWT contract.
    /// </summary>
    public const string AuthClientClaim = "orkyo:auth_client";

    /// <summary>How far before expiry to trigger a proactive token refresh.</summary>
    private static readonly TimeSpan RefreshWindow = TimeSpan.FromSeconds(60);

    /// <summary>
    /// How long the per-session refresh lock is held. Comfortably covers a refresh round-trip yet
    /// stays well below <see cref="RefreshWindow"/> so that a failed refresh can be retried by a
    /// later request before the access token actually expires.
    /// </summary>
    private static readonly TimeSpan RefreshLockTtl = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Slide only once the session is past the half-way point of its idle window (standard
    /// ASP.NET sliding-expiration semantics). This is what keeps the slide cheap: at most one
    /// store write and one Set-Cookie per half-window per session, however many requests the SPA
    /// fires. With a 7-day window that is one write every ~3.5 days of continuous use.
    /// </summary>
    private const double SlideThreshold = 0.5;

    private readonly IBffSessionStore _sessionStore;
    private readonly IDataProtector _protector;
    private readonly Configuration.BffOptions _bffOptions;
    private readonly KeycloakOptions _keycloakOptions;
    private readonly IBffAuthClientRegistry _authClientRegistry;
    private readonly IHttpClientFactory _httpClientFactory;

    public BffCookieAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IBffSessionStore sessionStore,
        IDataProtectionProvider dataProtection,
        IOptions<Configuration.BffOptions> bffOptions,
        KeycloakOptions keycloakOptions,
        IBffAuthClientRegistry authClientRegistry,
        IHttpClientFactory httpClientFactory)
        : base(options, logger, encoder)
    {
        _sessionStore = sessionStore;
        _protector = dataProtection.CreateProtector("BffSession");
        _bffOptions = bffOptions.Value;
        _keycloakOptions = keycloakOptions;
        _authClientRegistry = authClientRegistry;
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

        // Proactive token refresh if access token is nearing expiry. A burst of concurrent
        // requests (e.g. the People tab firing many parallel API calls on a site switch) must
        // not each fire a refresh_token grant: Keycloak rotates refresh tokens and revokes the
        // session on reuse (revokeRefreshToken=true / refreshTokenMaxReuse=0). The session store
        // arbitrates a single-flight refresh across all instances — only the lock winner refreshes;
        // everyone else keeps using the current access token, which is safe here because claims are
        // read without expiry validation and the token is never forwarded downstream.
        var accessToken = session.AccessToken;
        if (session.TokenExpiresAt - DateTimeOffset.UtcNow < RefreshWindow
            && await _sessionStore.TryAcquireRefreshLockAsync(sessionId, RefreshLockTtl, Context.RequestAborted))
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

        await SlideSessionIfDueAsync(session);

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

        // Carry the session's originating client onto the principal so /me can tell the SPA that
        // this session is ephemeral (see BffAuthEndpoints.HandleMe).
        if (!string.IsNullOrEmpty(session.AuthClient))
            claims.Add(new Claim(AuthClientClaim, session.AuthClient));

        var identity = new ClaimsIdentity(claims, SchemeName, KeycloakClaims.PreferredUsername, KeycloakClaims.RealmRolesClaim);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        return AuthenticateResult.Success(ticket);
    }

    /// <summary>
    /// Extends an active session's idle deadline, clamped to its absolute cap, and re-issues both
    /// cookies to match. This is what makes an active user's session GitHub-like: it survives as
    /// long as they keep working, and stops dead at the cap regardless.
    /// </summary>
    private async Task SlideSessionIfDueAsync(BffSessionRecord session)
    {
        // Sessions written before sliding existed decode with AbsoluteExpiresAt = default and
        // SlidingEnabled = false, so they keep their original fixed deadline and simply run out —
        // no migration, no surprise extension of a session established under the old policy.
        if (!session.SlidingEnabled)
            return;

        var now = DateTimeOffset.UtcNow;

        // Already at the cap — nothing left to give.
        if (session.AbsoluteExpiresAt <= now)
            return;

        var remaining = session.ExpiresAt - now;
        if (remaining > _bffOptions.SessionIdleDuration * SlideThreshold)
            return;

        var target = now.Add(_bffOptions.SessionIdleDuration);
        if (target > session.AbsoluteExpiresAt)
            target = session.AbsoluteExpiresAt;

        // Clamping can leave nothing to do once the cap is close.
        if (target <= session.ExpiresAt)
            return;

        await _sessionStore.SlideExpiryAsync(session.SessionId, target, Context.RequestAborted);

        // The browser copy must move too, or the cookie would lapse while the server-side session
        // is still alive — the user would be logged out despite the extension.
        var cookieValue = Request.Cookies[_bffOptions.CookieName];
        var csrfValue = Request.Cookies[_bffOptions.CsrfCookieName];
        var lifetime = target - now;
        if (!string.IsNullOrEmpty(cookieValue))
            BffSessionCookies.WriteSessionCookie(Context, _bffOptions, cookieValue, lifetime);
        if (!string.IsNullOrEmpty(csrfValue))
            BffSessionCookies.WriteCsrfCookie(Context, _bffOptions, csrfValue, lifetime);
    }

    private async Task<BffSessionRecord?> TryRefreshTokensAsync(BffSessionRecord session)
    {
        try
        {
            var tokenEndpoint = $"{_keycloakOptions.InternalAuthority}/protocol/openid-connect/token";
            var client = _httpClientFactory.CreateClient("BffKeycloak");

            // A refresh token is bound to the client it was issued to — a session
            // established through a secondary client (session.AuthClient) must
            // refresh with that client's credentials or Keycloak rejects the grant.
            var (clientId, clientSecret) = _authClientRegistry.Resolve(session.AuthClient);
            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
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
