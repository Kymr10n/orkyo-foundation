using Orkyo.Shared;

namespace Api.Services;

public enum TenantDeleteDecision
{
    Allowed,
    TenantNotFound,
    NotOwner,
    AlreadyDeleting
}

public enum TenantCancelDeletionDecision
{
    Allowed,
    TenantNotFound,
    NotOwner,
    NotDeleting
}

public static class TenantDeletionPolicy
{
    public static TenantDeleteDecision EvaluateDelete(bool tenantFound, Guid? ownerUserId, Guid actorUserId, bool isSiteAdmin, string? status)
    {
        var ownerDecision = TenantOwnerAccessPolicy.Evaluate(tenantFound, ownerUserId, actorUserId, isSiteAdmin);
        if (ownerDecision == TenantOwnerAccessDecision.TenantNotFound)
            return TenantDeleteDecision.TenantNotFound;

        if (ownerDecision == TenantOwnerAccessDecision.NotOwner)
            return TenantDeleteDecision.NotOwner;

        if (TenantOwnerAccessPolicy.IsDeleting(status))
            return TenantDeleteDecision.AlreadyDeleting;

        return TenantDeleteDecision.Allowed;
    }

    public static TenantCancelDeletionDecision EvaluateCancelDeletion(bool tenantFound, Guid? ownerUserId, Guid actorUserId, bool isSiteAdmin, string? status)
    {
        var ownerDecision = TenantOwnerAccessPolicy.Evaluate(tenantFound, ownerUserId, actorUserId, isSiteAdmin);
        if (ownerDecision == TenantOwnerAccessDecision.TenantNotFound)
            return TenantCancelDeletionDecision.TenantNotFound;

        if (ownerDecision == TenantOwnerAccessDecision.NotOwner)
            return TenantCancelDeletionDecision.NotOwner;

        if (!TenantOwnerAccessPolicy.IsDeleting(status))
            return TenantCancelDeletionDecision.NotDeleting;

        return TenantCancelDeletionDecision.Allowed;
    }
}
