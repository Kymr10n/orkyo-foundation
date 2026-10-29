using Api.Middleware;
using Api.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Api.Endpoints;

/// <summary>
/// <c>GET /api/tenants/memberships</c>: the caller's workspaces, for the apex workspace picker and
/// the account page. Foundation owns the route and the response shape; the product supplies the
/// data through <see cref="ITenantMembershipQuery"/>.
/// </summary>
public static class TenantMembershipEndpoints
{
    public static void MapTenantMembershipEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/tenants/memberships", async (ITenantMembershipQuery memberships, CancellationToken ct) =>
                Results.Ok(await memberships.GetCurrentUserMembershipsAsync(ct)))
            .RequireAuthorization()
            .WithMetadata(new SkipTenantResolutionAttribute())
            .WithName("GetUserMemberships")
            .WithSummary("The current user's workspace memberships")
            .WithTags("Tenants");
    }
}
