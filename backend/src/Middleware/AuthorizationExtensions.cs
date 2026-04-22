using Api.Constants;
using Api.Helpers;
using Api.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Api.Middleware;

public static class AuthorizationExtensions
{
    /// <summary>
    /// Path the frontend lands on after a break-glass session ends. Served from the apex domain.
    /// </summary>
    private const string AdminReturnPath = "/admin";

    /// <summary>
    /// Requires the user to be a site administrator (global admin role from Keycloak).
    /// Uses ICurrentPrincipal populated during request processing.
    /// </summary>
    public static RouteHandlerBuilder RequireSiteAdmin(this RouteHandlerBuilder builder)
    {
        return builder.AddEndpointFilter(async (context, next) =>
        {
            var httpContext = context.HttpContext;
            var logger = httpContext.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger(typeof(AuthorizationExtensions));
            var currentPrincipal = httpContext.RequestServices.GetService<ICurrentPrincipal>();

            if (currentPrincipal == null || !currentPrincipal.IsAuthenticated)
            {
                logger.LogWarning("Site admin check failed: user not authenticated");
                return ErrorResponses.Unauthorized();
            }

            if (!currentPrincipal.IsSiteAdmin)
            {
                logger.LogWarning("Site admin check failed: user {UserId} is not a site admin",
                    currentPrincipal.UserId);
                return ErrorResponses.Forbidden();
            }

            logger.LogDebug("Site admin authorization passed for user {UserId}", currentPrincipal.UserId);
            return await next(context);
        });
    }

    /// <summary>
    /// Requires the user to have one of the specified roles in the current tenant.
    /// Uses IAuthorizationContext populated during request processing.
    /// </summary>
    public static RouteHandlerBuilder RequireRole(this RouteHandlerBuilder builder, params TenantRole[] roles)
    {
        return builder.AddEndpointFilter(async (context, next) =>
        {
            var httpContext = context.HttpContext;
            var logger = httpContext.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger(typeof(AuthorizationExtensions));
            var authContext = httpContext.RequestServices.GetService<IAuthorizationContext>();

            if (authContext == null || !authContext.IsMember)
            {
                logger.LogWarning("User not a member of tenant or auth context missing");
                return BuildMembershipDenial(httpContext);
            }

            if (authContext.Role == TenantRole.None)
            {
                logger.LogWarning("User has no valid role in tenant");
                return BuildMembershipDenial(httpContext);
            }

            logger.LogDebug("Authorization check: TenantRole={TenantRole}, RequiredRoles={RequiredRoles}",
                authContext.Role, string.Join(",", roles));

            if (!AuthorizationPolicy.IsRoleAllowed(authContext.Role, roles))
            {
                logger.LogWarning("User role {TenantRole} not in required roles: {RequiredRoles}",
                    authContext.Role, string.Join(",", roles));
                return ErrorResponses.Forbidden();
            }

            return await next(context);
        });
    }

    /// <summary>
    /// Requires the user to be a member of the current tenant (any role).
    /// Apply at the MapGroup level on all tenant-scoped route groups to block
    /// authenticated users who are not members of the resolved tenant.
    /// </summary>
    public static TBuilder RequireTenantMembership<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        return builder.AddEndpointFilter(async (context, next) =>
        {
            var httpContext = context.HttpContext;
            var authContext = httpContext.RequestServices.GetService<IAuthorizationContext>();

            if (authContext == null || !authContext.IsMember)
            {
                var logger = httpContext.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger(typeof(AuthorizationExtensions));
                var principal = httpContext.RequestServices.GetService<ICurrentPrincipal>();
                logger.LogWarning(
                    "Tenant membership check failed for user {UserId} — not a member of tenant",
                    principal?.UserId);
                return BuildMembershipDenial(httpContext);
            }

            return await next(context);
        });
    }

    /// <summary>
    /// Requires either site-admin access (no tenant needed) or Admin role in a tenant.
    /// Use for endpoints that serve both site-scoped and tenant-scoped data.
    /// </summary>
    public static RouteHandlerBuilder RequireAdminAccess(this RouteHandlerBuilder builder)
    {
        return builder.AddEndpointFilter(async (context, next) =>
        {
            var httpContext = context.HttpContext;
            var logger = httpContext.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger(typeof(AuthorizationExtensions));
            var principal = httpContext.RequestServices.GetService<ICurrentPrincipal>();

            if (principal == null || !principal.IsAuthenticated)
            {
                logger.LogWarning("Admin access check failed: user not authenticated");
                return ErrorResponses.Unauthorized();
            }

            var authContext = httpContext.RequestServices.GetService<IAuthorizationContext>();
            var isTenantAdminMember = authContext != null && authContext.IsMember && authContext.Role == TenantRole.Admin;

            if (AuthorizationPolicy.HasAdminAccess(principal.IsAuthenticated, principal.IsSiteAdmin, isTenantAdminMember))
            {
                if (principal.IsSiteAdmin)
                    logger.LogDebug("Admin access granted via site-admin for user {UserId}", principal.UserId);
                else
                    logger.LogDebug("Admin access granted via tenant admin role for user {UserId}", principal.UserId);
                return await next(context);
            }

            logger.LogWarning("Admin access denied for user {UserId}: neither site-admin nor tenant admin", principal.UserId);
            return ErrorResponses.Forbidden();
        });
    }

    /// <summary>
    /// Tenant-membership denials are split into two cases so the frontend can respond differently:
    /// a site-admin whose break-glass session ended should be routed back to /admin, while a genuine
    /// non-member should see a plain 403 (toast). Unauthenticated callers get 401.
    /// </summary>
    private static IResult BuildMembershipDenial(HttpContext httpContext)
    {
        var principal = httpContext.RequestServices.GetService<ICurrentPrincipal>();
        var denialKind = AuthorizationPolicy.GetMembershipDenialKind(
            principal?.IsAuthenticated ?? false,
            principal?.IsSiteAdmin ?? false);

        return denialKind switch
        {
            MembershipDenialKind.Unauthorized => ErrorResponses.Unauthorized(),
            MembershipDenialKind.BreakGlassExpired => ErrorResponses.Forbidden(
                code: ApiErrorCodes.BreakGlassExpired,
                message: "Your break-glass session for this tenant has ended.",
                returnTo: AdminReturnPath),
            _ => ErrorResponses.Forbidden()
        };
    }
}