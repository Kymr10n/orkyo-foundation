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
        if (!tenantFound)
            return TenantDeleteDecision.TenantNotFound;

        if (ownerUserId != actorUserId && !isSiteAdmin)
            return TenantDeleteDecision.NotOwner;

        if (string.Equals(status, TenantStatusConstants.Deleting, StringComparison.OrdinalIgnoreCase))
            return TenantDeleteDecision.AlreadyDeleting;

        return TenantDeleteDecision.Allowed;
    }

    public static TenantCancelDeletionDecision EvaluateCancelDeletion(bool tenantFound, Guid? ownerUserId, Guid actorUserId, bool isSiteAdmin, string? status)
    {
        if (!tenantFound)
            return TenantCancelDeletionDecision.TenantNotFound;

        if (ownerUserId != actorUserId && !isSiteAdmin)
            return TenantCancelDeletionDecision.NotOwner;

        if (!string.Equals(status, TenantStatusConstants.Deleting, StringComparison.OrdinalIgnoreCase))
            return TenantCancelDeletionDecision.NotDeleting;

        return TenantCancelDeletionDecision.Allowed;
    }
}
