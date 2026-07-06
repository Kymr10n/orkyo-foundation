using Api.Configuration;
using Api.Middleware;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Api.Endpoints.Admin;

/// <summary>
/// Single place that stamps the shared posture of every <c>/api/admin</c> route group:
/// authentication, the admin-operations rate limit, the Admin OpenAPI tag, and
/// <see cref="SkipTenantResolutionAttribute"/> (site administration is control-plane,
/// not tenant-scoped). Individual routes still declare <c>RequireSiteAdmin()</c>.
///
/// Exists because the same four-line MapGroup preamble was previously repeated per
/// admin endpoint file and had already drifted (one copy was missing the rate limit).
/// </summary>
public static class AdminEndpointGroup
{
    public static RouteGroupBuilder MapSiteAdminGroup(this IEndpointRouteBuilder app) =>
        app.MapGroup("/api/admin")
            .RequireAuthorization()
            .RequireRateLimiting(FoundationRateLimitPolicies.AdminOperations)
            .WithTags("Admin")
            .WithMetadata(new SkipTenantResolutionAttribute());
}
