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

        if (LastActiveAdminPolicy.IsLastActiveAdmin(actorRole, activeAdminCount))
            return TenantLeaveMembershipDecision.LastAdminCannotLeave;

        return TenantLeaveMembershipDecision.Allowed;
    }
}
