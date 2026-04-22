using Api.Security;

namespace Api.Middleware;

public enum MembershipDenialKind
{
    Unauthorized,
    BreakGlassExpired,
    Forbidden
}

public static class AuthorizationPolicy
{
    public static MembershipDenialKind GetMembershipDenialKind(bool isAuthenticated, bool isSiteAdmin)
    {
        if (!isAuthenticated)
            return MembershipDenialKind.Unauthorized;

        return isSiteAdmin
            ? MembershipDenialKind.BreakGlassExpired
            : MembershipDenialKind.Forbidden;
    }

    public static bool IsRoleAllowed(TenantRole tenantRole, params TenantRole[] requiredRoles)
    {
        if (tenantRole == TenantRole.None)
            return false;

        return requiredRoles.Contains(tenantRole);
    }

    public static bool HasAdminAccess(bool isAuthenticated, bool isSiteAdmin, bool isTenantAdminMember)
    {
        if (!isAuthenticated)
            return false;

        return isSiteAdmin || isTenantAdminMember;
    }
}