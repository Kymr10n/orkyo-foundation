using Api.Configuration;
using Api.Middleware;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Api.Endpoints.Admin;

/// <summary>
/// Single place that stamps the shared posture of every site-admin <c>/api/admin</c> route group:
/// authentication, the site-admin gate (reads included), the admin-operations rate limit, the
/// OpenAPI tag, and <see cref="SkipTenantResolutionAttribute"/> (site administration is
/// control-plane, not tenant-scoped).
///
/// Exists because the same MapGroup preamble was previously repeated per admin endpoint file and
/// had already drifted (one copy was missing the rate limit). The gate sits on the group, so a new
/// admin GET that forgets a per-route call is still closed.
/// </summary>
public static class AdminEndpointGroup
{
    public static RouteGroupBuilder MapSiteAdminGroup(this IEndpointRouteBuilder app) =>
        app.MapSiteAdminGroup("", "Admin");

    /// <summary>A site-admin group at <c>/api/admin{subPath}</c> under its own OpenAPI tag.</summary>
    public static RouteGroupBuilder MapSiteAdminGroup(this IEndpointRouteBuilder app, string subPath, string tag) =>
        app.MapGroup("/api/admin" + subPath)
            .RequireAuthorization()
            .RequireSiteAdmin()
            .RequireRateLimiting(FoundationRateLimitPolicies.AdminOperations)
            .WithTags(tag)
            .WithMetadata(new SkipTenantResolutionAttribute());
}
