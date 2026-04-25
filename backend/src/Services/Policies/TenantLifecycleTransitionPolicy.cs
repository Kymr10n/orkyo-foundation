using Orkyo.Shared;

namespace Api.Services;

/// <summary>
/// Pure transition rules for tenant lifecycle states.
/// Keep this aligned with worker SQL predicates so tests can verify policy semantics.
/// </summary>
public static class TenantLifecycleTransitionPolicy
{
    public static bool ShouldSuspend(
        string status,
        DateTime? lastActivityAtUtc,
        DateTime nowUtc)
    {
        if (!string.Equals(status, TenantStatusConstants.Active, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!lastActivityAtUtc.HasValue)
            return false;

        var cutoff = nowUtc.AddDays(-LifecyclePolicyConstants.TenantSuspendAfterDormantDays);
        return lastActivityAtUtc.Value < cutoff;
    }

    public static bool ShouldMarkDeleting(
        string status,
        DateTime? suspendedAtUtc,
        DateTime nowUtc)
    {
        if (!string.Equals(status, TenantStatusConstants.Suspended, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!suspendedAtUtc.HasValue)
            return false;

        var cutoff = nowUtc.AddDays(-LifecyclePolicyConstants.UserPurgeAfterDormantDays);
        return suspendedAtUtc.Value < cutoff;
    }

    public static bool ShouldPermanentlyDelete(
        string status,
        DateTime updatedAtUtc,
        DateTime nowUtc)
    {
        if (!string.Equals(status, TenantStatusConstants.Deleting, StringComparison.OrdinalIgnoreCase))
            return false;

        var cutoff = nowUtc.AddDays(-LifecyclePolicyConstants.TenantDeleteGraceDays);
        return updatedAtUtc < cutoff;
    }
}
