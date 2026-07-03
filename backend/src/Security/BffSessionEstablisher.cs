using System.Security.Cryptography;
using Api.Configuration;
using Api.Endpoints;
using Api.Integrations.Keycloak;
using Api.Services;
using Api.Services.BffSession;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Api.Security;

/// <summary>
/// Establishes a BFF session for an authenticated user: persists the session
/// record, writes the encrypted session + CSRF cookies, and captures the real
/// device/IP for the "Active Sessions" UI. This is the single seam every
/// session-creation path goes through (the OIDC callback and the SaaS demo
/// login), so the device capture lives here once rather than per login path.
/// </summary>
public interface IBffSessionEstablisher
{
    Task EstablishAsync(
        HttpContext ctx,
        Guid userId,
        KeycloakTokenProfile tokenProfile,
        BffAuthEndpoints.TokenResponse tokenResponse,
        TimeSpan? sessionLifetimeOverride = null);
}

public sealed class BffSessionEstablisher : IBffSessionEstablisher
{
    // Must match the purpose used by BffCookieAuthenticationHandler to decrypt.
    private const string DataProtectionPurpose = "BffSession";
    private const int CsrfTokenLength = 32;

    private readonly IBffSessionStore _sessionStore;
    private readonly IDataProtectionProvider _dataProtection;
    private readonly IUserSessionService _userSessionService;
    private readonly IClientIpAccessor _clientIpAccessor;
    private readonly ISignInAuditRecorder _signInAudit;
    private readonly BffOptions _bffOptions;
    private readonly ILogger<BffSessionEstablisher> _logger;

    public BffSessionEstablisher(
        IBffSessionStore sessionStore,
        IDataProtectionProvider dataProtection,
        IUserSessionService userSessionService,
        IClientIpAccessor clientIpAccessor,
        ISignInAuditRecorder signInAudit,
        IOptions<BffOptions> bffOptions,
        ILogger<BffSessionEstablisher> logger)
    {
        _sessionStore = sessionStore;
        _dataProtection = dataProtection;
        _userSessionService = userSessionService;
        _clientIpAccessor = clientIpAccessor;
        _signInAudit = signInAudit;
        _bffOptions = bffOptions.Value;
        _logger = logger;
    }

    public async Task EstablishAsync(
        HttpContext ctx,
        Guid userId,
        KeycloakTokenProfile tokenProfile,
        BffAuthEndpoints.TokenResponse tokenResponse,
        TimeSpan? sessionLifetimeOverride = null)
    {
        var lifetime = sessionLifetimeOverride ?? _bffOptions.SessionDuration;
        var sessionId = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;
        var session = new BffSessionRecord
        {
            SessionId = sessionId,
            UserId = userId.ToString(),
            ExternalSubject = tokenProfile.Subject!,
            AccessToken = tokenResponse.AccessToken,
            RefreshToken = tokenResponse.RefreshToken,
            IdToken = tokenResponse.IdToken,
            ExpiresAt = now.Add(lifetime),
            TokenExpiresAt = now.AddSeconds(tokenResponse.ExpiresInSeconds),
            CreatedAt = now,
            LastActivityAt = now,
        };

        await _sessionStore.SetAsync(session);

        var protector = _dataProtection.CreateProtector(DataProtectionPurpose);
        ctx.Response.Cookies.Append(_bffOptions.CookieName, protector.Protect(sessionId), new CookieOptions
        {
            HttpOnly = true,
            Secure = _bffOptions.CookieSecure,
            SameSite = SameSiteMode.Lax,
            Domain = _bffOptions.CookieDomain,
            Path = "/",
            MaxAge = lifetime,
        });

        // 32-byte (256-bit) CSRF token — NOT HttpOnly so JS can read it
        ctx.Response.Cookies.Append(_bffOptions.CsrfCookieName,
            Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(CsrfTokenLength)), new CookieOptions
            {
                HttpOnly = false,
                Secure = _bffOptions.CookieSecure,
                SameSite = SameSiteMode.Lax,
                Domain = _bffOptions.CookieDomain,
                Path = "/",
                MaxAge = lifetime,
            });

        await CaptureDeviceAsync(ctx, userId, tokenProfile);

        // Audit the sign-in once, here at the single session-creation seam, so every login path (OIDC
        // callback and SaaS demo login) is covered. Best-effort — the recorder swallows its own failures.
        await _signInAudit.RecordAsync(userId, tokenProfile.Email);
    }

    /// <summary>
    /// Records the real device/IP for this Keycloak session so the "Active
    /// Sessions" UI can show it (Keycloak only records the BFF client + proxy IP).
    /// Best-effort: a control-plane hiccup must never block login.
    /// </summary>
    private async Task CaptureDeviceAsync(HttpContext ctx, Guid userId, KeycloakTokenProfile tokenProfile)
    {
        if (string.IsNullOrEmpty(tokenProfile.SessionId))
            return;

        try
        {
            var clientIp = _clientIpAccessor.GetClientIp(ctx);
            var userAgent = ctx.Request.Headers.UserAgent.FirstOrDefault();
            await _userSessionService.UpsertAsync(userId, tokenProfile.SessionId, clientIp, userAgent);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to capture device metadata for session");
        }
    }
}
