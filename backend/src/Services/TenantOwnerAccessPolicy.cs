using Orkyo.Shared;

namespace Api.Services;

public enum TenantOwnerAccessDecision
{
    Allowed,
    TenantNotFound,
    NotOwner
}

public static class TenantOwnerAccessPolicy
{
    public static TenantOwnerAccessDecision Evaluate(bool tenantFound, Guid? ownerUserId, Guid actorUserId, bool isSiteAdmin)
    {
        if (!tenantFound)
            return TenantOwnerAccessDecision.TenantNotFound;

        if (ownerUserId != actorUserId && !isSiteAdmin)
            return TenantOwnerAccessDecision.NotOwner;

        return TenantOwnerAccessDecision.Allowed;
    }

    public static bool IsDeleting(string? tenantStatus)
    {
        return string.Equals(tenantStatus, TenantStatusConstants.Deleting, StringComparison.OrdinalIgnoreCase);
    }
}
