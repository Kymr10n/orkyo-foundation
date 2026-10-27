using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Api.Middleware;

public static class HttpContextLoggingExtensions
{
    /// <summary>
    /// The matched route template, so a secret carried in the path — the anonymous calendar
    /// feed token, say — never reaches the log. The raw path only for a request no route matched;
    /// PathString converts to its escaped form, so a CR/LF in it logs as <c>%0D%0A</c> and cannot
    /// split the line.
    /// </summary>
    public static string LoggablePath(this HttpContext context) =>
        context.GetEndpoint() is RouteEndpoint { RoutePattern.RawText: { } template } ? template : context.Request.Path;
}
