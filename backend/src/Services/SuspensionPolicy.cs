using Api.Constants;
using Orkyo.Shared;

namespace Api.Services;

/// <summary>
/// Single source of truth for tenant reactivation rules. Used by the suspension
/// middleware gate (to advertise the option), the session bootstrap payload
/// (to populate <c>canReactivate</c>), and the reactivation endpoint (to enforce).
/// </summary>
public static class SuspensionPolicy
{
    /// <summary>
    /// Whether the given suspension reason is eligible for self-service reactivation
    /// (independent of the caller's role). Keep this in sync with the frontend
    /// <c>SELF_SERVICE_SUSPENSION_REASONS</c>.
    /// </summary>
    public static bool IsSelfServiceReason(string? reason) =>
        string.Equals(reason, SuspensionReasonConstants.Inactivity, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether a caller with the given role/ownership can reactivate a tenant in
    /// the given status/reason. Only owners and admins may reactivate, and only
    /// when the reason is self-service eligible.
    /// </summary>
    public static bool CanReactivate(string status, string? reason, string? role, bool isOwner)
    {
        if (!string.Equals(status, TenantStatusConstants.Suspended, StringComparison.OrdinalIgnoreCase))
            return false;
        if (!IsSelfServiceReason(reason))
            return false;
        return isOwner || string.Equals(role, RoleConstants.Admin, StringComparison.OrdinalIgnoreCase);
    }
}
