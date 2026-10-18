using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Api.Middleware;

public class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            await _next(context);
            sw.Stop();

            var statusCode = context.Response.StatusCode;
            var level = statusCode >= 400 ? LogLevel.Warning : LogLevel.Information;

            _logger.Log(level, "HTTP {Method} {Path} responded {StatusCode} in {Duration}ms",
                context.Request.Method, Route(context), statusCode, sw.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            sw.Stop();
            // The client disconnected (browser navigated away / aborted the fetch). This is not a
            // server fault — log it quietly as a 499 ("client closed request") so a genuine
            // server-side timeout or error stands out instead of being buried in cancellation
            // noise. A cancellation NOT driven by RequestAborted (e.g. a server-side timeout token)
            // falls through to the error branch below and is surfaced loudly.
            _logger.LogInformation("HTTP {Method} {Path} cancelled by client (499) after {Duration}ms",
                context.Request.Method, Route(context), sw.ElapsedMilliseconds);
            throw;
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "HTTP {Method} {Path} failed after {Duration}ms",
                context.Request.Method, Route(context), sw.ElapsedMilliseconds);
            throw;
        }
    }
    /// <summary>
    /// The matched route template, so a secret carried in the path — the anonymous calendar
    /// feed token, say — never reaches the log. The raw path only for a request no route matched.
    /// </summary>
    private static string Route(HttpContext context) =>
        context.GetEndpoint() is RouteEndpoint { RoutePattern.RawText: { } template } ? template : context.Request.Path;
}

public static class RequestLoggingMiddlewareExtensions
{
    public static IApplicationBuilder UseRequestLogging(this IApplicationBuilder builder)
        => builder.UseMiddleware<RequestLoggingMiddleware>();
}
