using Orkyo.Shared;

namespace Api.Services;

public static class TenantStatusPolicy
{
    public static bool IsActive(string status)
    {
        return string.Equals(status, TenantStatusConstants.Active, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsSuspended(string status)
    {
        return string.Equals(status, TenantStatusConstants.Suspended, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsPendingDeletion(string status)
    {
        return string.Equals(status, TenantStatusConstants.Deleting, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Whether the tenant is blocked from normal use but restorable by an
    /// owner/admin: suspended, or scheduled for deletion (grace window).
    /// </summary>
    public static bool IsBlocked(string status)
    {
        return IsSuspended(status) || IsPendingDeletion(status);
    }
}
