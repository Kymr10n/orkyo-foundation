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
        if (!tenantFound)
            return TenantUpdateDecision.TenantNotFound;

        if (ownerUserId != actorUserId && !isSiteAdmin)
            return TenantUpdateDecision.NotOwner;

        if (string.Equals(tenantStatus, TenantStatusConstants.Deleting, StringComparison.OrdinalIgnoreCase))
            return TenantUpdateDecision.TenantDeleting;

        if (displayName != null && string.IsNullOrWhiteSpace(displayName))
            return TenantUpdateDecision.EmptyDisplayName;

        if (displayName != null && displayName.Length > 255)
            return TenantUpdateDecision.DisplayNameTooLong;

        return TenantUpdateDecision.Allowed;
    }
}
