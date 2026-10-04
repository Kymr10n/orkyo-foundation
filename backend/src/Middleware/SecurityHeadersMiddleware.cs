using Api.Constants;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

namespace Api.Middleware;

public sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;
    private readonly bool _isProduction;

    public SecurityHeadersMiddleware(RequestDelegate next, IWebHostEnvironment env)
    {
        _next = next;
        _isProduction = env.IsProduction();
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;
        headers[HeaderConstants.XContentTypeOptions] = "nosniff";
        headers[HeaderConstants.XFrameOptions] = "DENY";
        headers[HeaderConstants.ReferrerPolicy] = "strict-origin-when-cross-origin";
        headers[HeaderConstants.XPermittedCrossDomainPolicies] = "none";
        headers[HeaderConstants.ContentSecurityPolicy] = "default-src 'none'; frame-ancestors 'none'";
        headers[HeaderConstants.PermissionsPolicy] = "camera=(), microphone=(), geolocation=(), payment=()";
        // API and auth responses are per-caller. Without this a CDN in front may store one
        // visitor's answer and replay it to the next — a cached anonymous /me looped every
        // sign-in on orkyo.com. An endpoint that may be cached sets its own value after this.
        headers.CacheControl = "no-store";
        if (_isProduction)
            headers[HeaderConstants.StrictTransportSecurity] = "max-age=31536000; includeSubDomains";

        await _next(context);
    }
}
