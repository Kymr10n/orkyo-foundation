using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Api.Middleware;

public static class HttpContextLoggingExtensions
{
    /// <summary>
    /// The matched route template, so a secret carried in the path — the anonymous calendar
    /// feed token, say — never reaches the log. The raw path only for a request no route matched.
    /// </summary>
    public static string LoggablePath(this HttpContext context) =>
        context.GetEndpoint() is RouteEndpoint { RoutePattern.RawText: { } template } ? template : context.Request.Path;
}
