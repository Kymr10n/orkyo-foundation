using Api.Configuration;
using Microsoft.AspNetCore.Http;

namespace Api.Security;

/// <summary>
/// The one place BFF cookies are written. Two callers need identical attributes:
/// <see cref="BffSessionEstablisher"/> at login and <see cref="BffCookieAuthenticationHandler"/>
/// when it slides an active session forward. If the two ever disagreed on Domain, SameSite or
/// Secure, a slide would silently orphan the original cookie and log the user out instead of
/// keeping them in — so the attributes live here rather than being duplicated.
/// </summary>
public static class BffSessionCookies
{
    /// <summary>
    /// The Data Protection purpose the session cookie is sealed under. Login seals, the handler
    /// and logout unseal, so all three must use this one value.
    /// </summary>
    public const string DataProtectionPurpose = "BffSession";

    /// <summary>The session cookie: HttpOnly, so JS can never read the session id.</summary>
    public static void WriteSessionCookie(HttpContext ctx, BffOptions options, string value, TimeSpan lifetime) =>
        ctx.Response.Cookies.Append(options.CookieName, value, Build(options, httpOnly: true, lifetime));

    /// <summary>The CSRF double-submit cookie: deliberately NOT HttpOnly — the SPA reads it.</summary>
    public static void WriteCsrfCookie(HttpContext ctx, BffOptions options, string value, TimeSpan lifetime) =>
        ctx.Response.Cookies.Append(BffOptions.CsrfCookieName, value, Build(options, httpOnly: false, lifetime));

    /// <summary>The path the login binding cookie is sent on: the BFF auth routes only.</summary>
    public const string LoginBindingPath = "/api/auth/bff";

    /// <summary>
    /// The login binding cookie: HttpOnly, sent back only to the BFF auth routes. Lax, because the
    /// return from Keycloak is a cross-site top-level navigation that Strict would strip it from.
    /// </summary>
    public static void WriteLoginBindingCookie(HttpContext ctx, BffOptions options, string value, TimeSpan lifetime) =>
        ctx.Response.Cookies.Append(BffOptions.LoginBindingCookieName, value,
            Build(options, httpOnly: true, lifetime, LoginBindingPath));

    public static void DeleteLoginBindingCookie(HttpContext ctx, BffOptions options) =>
        ctx.Response.Cookies.Delete(BffOptions.LoginBindingCookieName,
            Build(options, httpOnly: true, lifetime: null, LoginBindingPath));

    private static CookieOptions Build(BffOptions options, bool httpOnly, TimeSpan? lifetime, string path = "/") => new()
    {
        HttpOnly = httpOnly,
        Secure = options.CookieSecure,
        SameSite = SameSiteMode.Lax,
        Domain = options.CookieDomain,
        Path = path,
        MaxAge = lifetime,
    };
}
