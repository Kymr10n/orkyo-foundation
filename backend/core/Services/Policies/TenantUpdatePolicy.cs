using Orkyo.Shared;

namespace Api.Services;

public enum TenantUpdateDecision
{
    Allowed,
    TenantNotFound,
    NotOwner,
    TenantDeleting,
    EmptyDisplayName,
    DisplayNameTooLong
}

public static class TenantUpdatePolicy
{
    public static TenantUpdateDecision Evaluate(
        bool tenantFound,
        Guid? ownerUserId,
        Guid actorUserId,
        bool isSiteAdmin,
        string? tenantStatus,
        string? displayName)
    {
        var ownerDecision = TenantOwnerAccessPolicy.Evaluate(tenantFound, ownerUserId, actorUserId, isSiteAdmin);
        if (ownerDecision == TenantOwnerAccessDecision.TenantNotFound)
            return TenantUpdateDecision.TenantNotFound;

        if (ownerDecision == TenantOwnerAccessDecision.NotOwner)
            return TenantUpdateDecision.NotOwner;

        if (TenantOwnerAccessPolicy.IsDeleting(tenantStatus))
            return TenantUpdateDecision.TenantDeleting;

        if (displayName != null && string.IsNullOrWhiteSpace(displayName))
            return TenantUpdateDecision.EmptyDisplayName;

        if (displayName != null && displayName.Length > 255)
            return TenantUpdateDecision.DisplayNameTooLong;

        return TenantUpdateDecision.Allowed;
    }
}
