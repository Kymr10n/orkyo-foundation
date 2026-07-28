using Api.Middleware;
using Microsoft.AspNetCore.Builder;

namespace Api.Configuration;

/// <summary>
/// Parameters for <see cref="OrkyoPipelineExtensions.UseOrkyoPipeline"/> — the
/// points where the editions legitimately differ. Everything else in the
/// pipeline is shared and ordered here, once (foundation#97: the editions used
/// to hand-maintain parallel ~15-call sequences that silently diverged).
/// </summary>
public sealed class OrkyoPipelineOptions
{
    /// <summary>
    /// On by default (SaaS). Community turns this off deliberately: the
    /// single-tenant self-host serves plain HTTP on the LAN, where a redirect
    /// to a non-existent HTTPS endpoint would brick the deployment.
    /// </summary>
    public bool UseHttpsRedirection { get; init; } = true;

    /// <summary>Disables every rate-limiting pass (test environments — prevents spurious 429s).</summary>
    public bool RateLimitingDisabled { get; init; }

    /// <summary>
    /// Optional pass before authentication for Ip/Global dimensions (SaaS bot
    /// protection on anonymous traffic). Community intentionally has none: its
    /// deployment is LAN-scoped, and the middleware is SaaS-owned.
    /// </summary>
    public Action<WebApplication>? PreAuthRateLimiting { get; init; }

    /// <summary>
    /// Optional pass after tenant resolution for User/UserTenant/Tenant
    /// dimensions, which need the authenticated principal and the resolved
    /// tenant to build their keys. SaaS-only, like <see cref="PreAuthRateLimiting"/>.
    /// </summary>
    public Action<WebApplication>? PostTenantRateLimiting { get; init; }

    /// <summary>
    /// The edition's tenant-resolution middleware(s): SaaS mounts
    /// TenantMiddleware; Community mounts JIT provisioning + SingleTenantMiddleware.
    /// Runs after authorization, before ContextEnrichmentMiddleware.
    /// </summary>
    public required Action<WebApplication> TenantResolution { get; init; }
}

public static class OrkyoPipelineExtensions
{
    /// <summary>
    /// Composes the standard middleware pipeline shared by both editions, ending
    /// with <see cref="ContextEnrichmentMiddleware"/>. Endpoint mapping stays in
    /// each edition's Program.cs. Order is load-bearing throughout — notably
    /// UseOrkyoMetrics after UseRouting (route templates in metric labels), the
    /// two-pass rate limiting around authentication/tenant resolution, and
    /// context enrichment last so it sees the resolved tenant.
    /// </summary>
    public static WebApplication UseOrkyoPipeline(this WebApplication app, OrkyoPipelineOptions options)
    {
        app.UseFoundationMiddleware();
        app.UseResponseCompression();
        app.UseCors();
        if (options.UseHttpsRedirection)
            app.UseHttpsRedirection();
        app.UseMiddleware<SecurityHeadersMiddleware>();
        app.UseRouting();
        app.UseOrkyoMetrics();
        if (!options.RateLimitingDisabled)
            options.PreAuthRateLimiting?.Invoke(app);
        app.UseAuthentication();
        app.UseMiddleware<CsrfMiddleware>();
        app.UseAuthorization();
        if (!options.RateLimitingDisabled)
            app.UseRateLimiter();
        options.TenantResolution(app);
        if (!options.RateLimitingDisabled)
            options.PostTenantRateLimiting?.Invoke(app);
        app.UseMiddleware<ContextEnrichmentMiddleware>();
        return app;
    }
}
