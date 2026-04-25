using Microsoft.AspNetCore.Http;

namespace Api.Middleware;

public class CacheControlMiddleware
{
    private readonly RequestDelegate _next;

    private static readonly string[] CacheablePrefixes =
    [
        "/api/criteria",
        "/api/groups",
        "/api/settings",
    ];

    private const string CacheableHeader = "private, max-age=60";
    private const string NoCacheHeader = "no-store, no-cache";

    public CacheControlMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Method == HttpMethods.Get)
        {
            context.Response.OnStarting(() =>
            {
                if (context.Response.Headers.ContainsKey("Cache-Control"))
                    return Task.CompletedTask;
                if (context.Response.StatusCode < 200 || context.Response.StatusCode >= 300)
                    return Task.CompletedTask;

                var path = context.Request.Path.Value ?? string.Empty;
                var isCacheable = false;
                foreach (var prefix in CacheablePrefixes)
                {
                    if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        isCacheable = true;
                        break;
                    }
                }

                context.Response.Headers["Cache-Control"] = isCacheable ? CacheableHeader : NoCacheHeader;
                return Task.CompletedTask;
            });
        }

        await _next(context);
    }
}
