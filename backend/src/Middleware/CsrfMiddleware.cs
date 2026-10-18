using System.Security.Cryptography;
using System.Text;
using Api.Configuration;
using Api.Constants;
using Api.Helpers;
using Api.Security;
using Api.Services.BffSession;

namespace Api.Middleware;

/// <summary>
/// Synchronizer-token CSRF protection for BFF-authenticated requests: the CSRF header must
/// equal the token stored on the session (<see cref="BffSessionRecord.CsrfToken"/>, surfaced by
/// the cookie handler under <see cref="HttpContextItemKeys.BffCsrfToken"/>). The non-HttpOnly
/// CSRF cookie only carries that value to the SPA; it is never what the check compares against,
/// so a cookie planted from another host on the registrable domain cannot satisfy it.
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

        var sessionToken = context.Items[HttpContextItemKeys.BffCsrfToken] as string;
        var csrfHeader = context.Request.Headers[BffOptions.CsrfHeaderName].FirstOrDefault();

        if (string.IsNullOrEmpty(sessionToken) ||
            string.IsNullOrEmpty(csrfHeader) ||
            !CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(sessionToken), Encoding.UTF8.GetBytes(csrfHeader)))
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
