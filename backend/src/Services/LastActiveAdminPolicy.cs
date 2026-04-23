using Api.Constants;

namespace Api.Services;

/// <summary>
/// Pure decision policy: is the specified user the last active admin in a tenant?
///
/// "Last active admin" means: the user is currently an active admin AND there are
/// no other active admins in the tenant. Used to guard role-demotion and
/// membership-removal operations that would otherwise leave a tenant without
/// any active administrator.
/// </summary>
public static class LastActiveAdminPolicy
{
    /// <summary>
    /// Evaluate the last-active-admin guard.
    /// </summary>
    /// <param name="currentRole">
    /// Current active membership role of the user (or <c>null</c> if not an active member).
    /// </param>
    /// <param name="totalActiveAdminCount">
    /// Total count of active admins in the tenant, INCLUDING the user themselves.
    /// </param>
    public static bool IsLastActiveAdmin(string? currentRole, long totalActiveAdminCount)
    {
        if (!string.Equals(currentRole, RoleConstants.Admin, StringComparison.OrdinalIgnoreCase))
            return false;

        return totalActiveAdminCount <= 1;
    }
}
