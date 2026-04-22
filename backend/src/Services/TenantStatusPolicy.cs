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
}