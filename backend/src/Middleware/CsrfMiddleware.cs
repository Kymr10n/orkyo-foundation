using Api.Configuration;
using Api.Security;
using Microsoft.Extensions.Options;

namespace Api.Middleware;

/// <summary>
/// Double-submit cookie CSRF protection for BFF-authenticated requests.
///
/// Only activates for mutating requests (POST/PUT/PATCH/DELETE) authenticated
/// via the BFF cookie scheme. JWT Bearer requests pass through untouched
/// since bearer tokens aren't auto-attached by the browser.
/// </summary>
public sealed class CsrfMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<CsrfMiddleware> _logger;

    private static readonly HashSet<string> SafeMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "GET", "HEAD", "OPTIONS"
    };

    public CsrfMiddleware(RequestDelegate next, ILogger<CsrfMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, IOptions<BffOptions> bffOptions)
    {
        var options = bffOptions.Value;

        // Only enforce CSRF for BFF-authenticated requests
        if (context.User.Identity?.AuthenticationType != BffCookieAuthenticationHandler.SchemeName)
        {
            await _next(context);
            return;
        }

        // Safe methods don't need CSRF protection
        if (SafeMethods.Contains(context.Request.Method))
        {
            await _next(context);
            return;
        }

        // Validate double-submit: header must match cookie
        var csrfCookie = context.Request.Cookies[options.CsrfCookieName];
        var csrfHeader = context.Request.Headers[options.CsrfHeaderName].FirstOrDefault();

        if (string.IsNullOrEmpty(csrfCookie) ||
            string.IsNullOrEmpty(csrfHeader) ||
            !string.Equals(csrfCookie, csrfHeader, StringComparison.Ordinal))
        {
            _logger.LogWarning("CSRF validation failed for {Method} {Path}", context.Request.Method, context.Request.Path);
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new { error = "CSRF token mismatch" });
            return;
        }

        await _next(context);
    }
}
