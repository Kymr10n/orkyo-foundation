using Api.Constants;

namespace Api.Services;

public enum TenantLeaveMembershipDecision
{
    Allowed,
    OwnerCannotLeave,
    NotMember,
    LastAdminCannotLeave
}

public static class TenantLeaveMembershipPolicy
{
    public static TenantLeaveMembershipDecision Evaluate(Guid? ownerUserId, Guid actorUserId, string? actorRole, long activeAdminCount)
    {
        if (ownerUserId == actorUserId)
            return TenantLeaveMembershipDecision.OwnerCannotLeave;

        if (actorRole == null)
            return TenantLeaveMembershipDecision.NotMember;

        if (string.Equals(actorRole, RoleConstants.Admin, StringComparison.OrdinalIgnoreCase) && activeAdminCount <= 1)
            return TenantLeaveMembershipDecision.LastAdminCannotLeave;

        return TenantLeaveMembershipDecision.Allowed;
    }
}
