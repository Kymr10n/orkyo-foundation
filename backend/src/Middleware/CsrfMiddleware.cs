using Api.Configuration;
using Api.Constants;
using Api.Helpers;
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

    public async Task InvokeAsync(HttpContext context)
    {
        // Only enforce CSRF for BFF-authenticated requests
        if (context.User.Identity?.AuthenticationType != BffCookieAuthenticationHandler.SchemeName)
        {
            await _next(context);
            return;
        }

        // Safe methods don't need CSRF protection, and neither does an endpoint that acts on a
        // secret in its own request rather than on the session (see CsrfExemptAttribute).
        if (SafeMethods.Contains(context.Request.Method)
            || context.GetEndpoint()?.Metadata.GetMetadata<CsrfExemptAttribute>() is not null)
        {
            await _next(context);
            return;
        }

        // Validate double-submit: header must match cookie
        var csrfCookie = context.Request.Cookies[BffOptions.CsrfCookieName];
        var csrfHeader = context.Request.Headers[BffOptions.CsrfHeaderName].FirstOrDefault();

        if (string.IsNullOrEmpty(csrfCookie) ||
            string.IsNullOrEmpty(csrfHeader) ||
            !string.Equals(csrfCookie, csrfHeader, StringComparison.Ordinal))
        {
            _logger.LogWarning("CSRF validation failed for {Method} {Path}", context.Request.Method, context.Request.Path);
            await ProblemResults.Problem(
                StatusCodes.Status403Forbidden,
                ApiErrorCodes.CsrfTokenMismatch,
                detail: "CSRF token mismatch",
                title: "Forbidden").ExecuteAsync(context);
            return;
        }

        await _next(context);
    }
}

/// <summary>
/// Marks an anonymous endpoint whose POST acts only on a secret carried in the request itself (a
/// plain HTML form posting an emailed token), never on the caller's session. Such a form cannot send
/// the double-submit header, and a signed-in recipient's BFF cookie would otherwise make it fail.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class CsrfExemptAttribute : Attribute;
