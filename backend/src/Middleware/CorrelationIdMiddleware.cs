using System.Diagnostics.CodeAnalysis;
using Api.Constants;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Api.Middleware;

public class CorrelationIdMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationIdMiddleware> _logger;

    public CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var supplied = context.Request.Headers[HeaderConstants.CorrelationId].FirstOrDefault();
        var correlationId = IsWellFormed(supplied) ? supplied : Guid.NewGuid().ToString("D");

        context.TraceIdentifier = correlationId;

        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderConstants.CorrelationId] = correlationId;
            return Task.CompletedTask;
        });

        // ILogger scope — Serilog picks this up via its MEL adapter,
        // so every log line within this request will carry CorrelationId.
        using (_logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            await _next(context);
        }
    }

    // The client's id lands on every log line of the request and in the response header, so
    // anything but a short token of letters, digits and dashes is replaced. Both forms the
    // frontend's randomId() sends (a UUID, or a base-36 timestamp) pass.
    private static bool IsWellFormed([NotNullWhen(true)] string? id) =>
        id is { Length: > 0 and <= 64 } && id.All(c => char.IsAsciiLetterOrDigit(c) || c == '-');
}

public static class CorrelationIdMiddlewareExtensions
{
    public static IApplicationBuilder UseCorrelationId(this IApplicationBuilder builder)
        => builder.UseMiddleware<CorrelationIdMiddleware>();
}
