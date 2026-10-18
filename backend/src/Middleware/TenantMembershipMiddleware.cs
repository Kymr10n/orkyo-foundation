using Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace Api.Middleware;

/// <summary>
/// Membership by default. Runs after the security context is enriched: when a tenant was
/// resolved and the caller is authenticated but not a member, the request ends with the
/// membership denial before any endpoint runs. The only ways past it are declared on the
/// endpoint — <see cref="IAllowAnonymous"/>, <see cref="ISkipTenantResolution"/> or
/// <see cref="IAllowNonMember"/> — so a GET group that forgot its guard is no longer a
/// cross-tenant read (saas#296). Role checks stay with the Require* conventions.
/// </summary>
public sealed class TenantMembershipMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<TenantMembershipMiddleware> _logger;

    public TenantMembershipMiddleware(RequestDelegate next, ILogger<TenantMembershipMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, ICurrentPrincipal principal, ICurrentTenant tenant, IAuthorizationContext authorization)
    {
        if (principal.IsAuthenticated && tenant.HasTenant && !authorization.IsMember
            && RequiresMembership(context.GetEndpoint()))
        {
            _logger.LogWarning("Tenant membership check failed for user {UserId} on tenant {TenantSlug}",
                principal.UserId, tenant.TenantSlug);
            await AuthorizationExtensions.BuildMembershipDenial(context).ExecuteAsync(context);
            return;
        }

        await _next(context);
    }

    private static bool RequiresMembership(Endpoint? endpoint) =>
        endpoint is not null
        && endpoint.Metadata.GetMetadata<IAllowAnonymous>() is null
        && endpoint.Metadata.GetMetadata<ISkipTenantResolution>() is null
        && endpoint.Metadata.GetMetadata<IAllowNonMember>() is null;
}
