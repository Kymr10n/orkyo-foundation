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
    /// <summary>The session cookie: HttpOnly, so JS can never read the session id.</summary>
    public static void WriteSessionCookie(HttpContext ctx, BffOptions options, string value, TimeSpan lifetime) =>
        ctx.Response.Cookies.Append(options.CookieName, value, Build(options, httpOnly: true, lifetime));

    /// <summary>The CSRF double-submit cookie: deliberately NOT HttpOnly — the SPA reads it.</summary>
    public static void WriteCsrfCookie(HttpContext ctx, BffOptions options, string value, TimeSpan lifetime) =>
        ctx.Response.Cookies.Append(options.CsrfCookieName, value, Build(options, httpOnly: false, lifetime));

    private static CookieOptions Build(BffOptions options, bool httpOnly, TimeSpan lifetime) => new()
    {
        HttpOnly = httpOnly,
        Secure = options.CookieSecure,
        SameSite = SameSiteMode.Lax,
        Domain = options.CookieDomain,
        Path = "/",
        MaxAge = lifetime,
    };
}
