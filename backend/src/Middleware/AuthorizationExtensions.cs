using Api.Constants;
using Api.Helpers;
using Api.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Api.Middleware;

/// <summary>
/// Build-time marker stamped on every endpoint governed by one of the role conventions
/// (<see cref="AuthorizationExtensions.RequireMemberReadEditorWrite"/> and friends). The
/// authorization conformance test enumerates the endpoint graph and asserts every mutating route
/// either carries this marker or is on an explicit self-service/auth allowlist — so a new ungated
/// write fails CI.
/// </summary>
public sealed class AuthorizationGoverned;

/// <summary>
/// Build-time marker opting a single non-mutating POST (e.g. /validate, /preview) out of the
/// editor/admin write gate its group otherwise applies, keeping it readable by any member.
/// </summary>
public sealed class AllowMemberWriteMarker;

public static class AuthorizationExtensions
{
    /// <summary>
    /// Path the frontend lands on after a break-glass session ends. Served from the apex domain.
    /// This is the platform site-admin panel route (renamed from /admin to mirror the
    /// isSiteAdmin role; distinct from the tenant /tenant-admin Administration page).
    /// </summary>
    private const string AdminReturnPath = "/site-admin";

    /// <summary>
    /// Requires the user to be a site administrator (global admin role from Keycloak).
    /// Uses ICurrentPrincipal populated during request processing.
    /// </summary>
    public static RouteHandlerBuilder RequireSiteAdmin(this RouteHandlerBuilder builder)
    {
        builder.WithMetadata(new AuthorizationGoverned());
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
        builder.WithMetadata(new AuthorizationGoverned());
        return builder.AddEndpointFilter(async (context, next) =>
            EnforceRole(context.HttpContext, roles) ?? await next(context));
    }

    /// <summary>
    /// Core tenant-role check shared by <see cref="RequireRole"/> and the group conventions.
    /// Returns a denial <see cref="IResult"/> when the caller is not allowed, or <c>null</c> to
    /// continue. Mirrors the membership-vs-role split so the frontend can react to each.
    /// </summary>
    private static IResult? EnforceRole(HttpContext httpContext, params TenantRole[] roles)
    {
        var logger = httpContext.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(AuthorizationExtensions));
        var authContext = httpContext.RequestServices.GetService<IAuthorizationContext>();

        if (authContext == null || !authContext.IsMember || authContext.Role == TenantRole.None)
        {
            logger.LogWarning("User not a member of tenant or auth context missing");
            return BuildMembershipDenial(httpContext);
        }

        if (!AuthorizationPolicy.IsRoleAllowed(authContext.Role, roles))
        {
            logger.LogWarning("User role {TenantRole} not in required roles: {RequiredRoles}",
                authContext.Role, string.Join(",", roles));
            return ErrorResponses.Forbidden();
        }

        return null;
    }

    /// <summary>
    /// Requires Editor-or-Admin role in the current tenant — the standard gate for
    /// write operations on tenant-editable content (settings criteria, templates,
    /// presets, scheduling, …). Reads stay open to any member via
    /// <see cref="RequireTenantMembership{TBuilder}"/>; this guards the writes.
    /// Mirrors <see cref="IAuthorizationContext.CanEdit"/> (Role &gt;= Editor) and is
    /// the single source of truth for that threshold across endpoints.
    /// </summary>
    public static RouteHandlerBuilder RequireEditAccess(this RouteHandlerBuilder builder)
        => builder.RequireRole(TenantRole.Editor, TenantRole.Admin);

    // ── Verb-aware group conventions ──────────────────────────────────────────
    // Declare a group's authorization once; the filter gates by HTTP method so any new write
    // endpoint is protected by default. Pick exactly one per tenant-scoped group. The single
    // source of truth for the role contract — see docs/authorization.md.

    /// <summary>
    /// Reads (GET/HEAD) open to any tenant member; writes (POST/PUT/PATCH/DELETE) require Editor+.
    /// The default for general tenant content (requests, people, spaces, criteria, …). Mark a
    /// non-mutating POST (validate/preview) with <see cref="AllowMemberWrite"/> to keep it member-open.
    /// </summary>
    public static RouteGroupBuilder RequireMemberReadEditorWrite(this RouteGroupBuilder group)
        => group.RequireTenantMembership().WithWriteGate(adminOnly: false);

    /// <summary>
    /// Reads open to any tenant member; writes require Admin. For content whose list/get is needed
    /// app-wide but whose mutation is governance (Sites).
    /// </summary>
    public static RouteGroupBuilder RequireMemberReadAdminWrite(this RouteGroupBuilder group)
        => group.RequireTenantMembership().WithWriteGate(adminOnly: true);

    /// <summary>
    /// Every method (reads and writes) requires Admin. For the Administration area — Users, tenant
    /// Settings, quotas, export, reporting tokens — which Viewers/Editors cannot see at all.
    /// </summary>
    public static RouteGroupBuilder RequireAdminArea(this RouteGroupBuilder group)
    {
        group.WithMetadata(new AuthorizationGoverned());
        return group.AddEndpointFilter(async (context, next) =>
            EnforceAdmin(context.HttpContext) ?? await next(context));
    }

    /// <summary>
    /// Opt a single non-mutating POST out of its group's write gate (e.g. validation/preview that
    /// computes but does not persist), keeping it readable by any member.
    /// </summary>
    public static RouteHandlerBuilder AllowMemberWrite(this RouteHandlerBuilder builder)
        => builder.WithMetadata(new AllowMemberWriteMarker());

    /// <summary>
    /// Adds the verb-aware write gate to a group: mutating methods require Editor (or Admin when
    /// <paramref name="adminOnly"/>), unless the route carries <see cref="AllowMemberWriteMarker"/>.
    /// Stamps <see cref="AuthorizationGoverned"/> so the conformance test can prove coverage.
    /// </summary>
    private static RouteGroupBuilder WithWriteGate(this RouteGroupBuilder group, bool adminOnly)
    {
        group.WithMetadata(new AuthorizationGoverned());
        return group.AddEndpointFilter(async (context, next) =>
        {
            var httpContext = context.HttpContext;
            if (IsMutating(httpContext.Request.Method)
                && httpContext.GetEndpoint()?.Metadata.GetMetadata<AllowMemberWriteMarker>() is null)
            {
                var denial = adminOnly
                    ? EnforceAdmin(httpContext)
                    : EnforceRole(httpContext, TenantRole.Editor, TenantRole.Admin);
                if (denial is not null) return denial;
            }

            return await next(context);
        });
    }

    private static bool IsMutating(string method) =>
        HttpMethods.IsPost(method) || HttpMethods.IsPut(method)
        || HttpMethods.IsPatch(method) || HttpMethods.IsDelete(method);

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
        builder.WithMetadata(new AuthorizationGoverned());
        return builder.AddEndpointFilter(async (context, next) =>
            EnforceAdmin(context.HttpContext) ?? await next(context));
    }

    /// <summary>
    /// Core admin check (site-admin OR tenant Admin) shared by <see cref="RequireAdminAccess"/> and
    /// the <see cref="RequireAdminArea"/> / <see cref="RequireMemberReadAdminWrite"/> conventions.
    /// Returns a denial <see cref="IResult"/> when the caller is not an admin, or <c>null</c>.
    /// </summary>
    private static IResult? EnforceAdmin(HttpContext httpContext)
    {
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
            return null;

        logger.LogWarning("Admin access denied for user {UserId}: neither site-admin nor tenant admin", principal.UserId);
        return ErrorResponses.Forbidden();
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
