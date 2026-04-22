using Api.Constants;
using Orkyo.Shared;

namespace Api.Services;

public enum TenantOwnershipTransferDecision
{
    Allowed,
    TenantNotFound,
    NotCurrentOwner,
    TenantDeleting,
    NewOwnerNotMember,
    NewOwnerNotAdmin,
    NewOwnerMembershipNotActive
}

public static class TenantOwnershipTransferPolicy
{
    public static TenantOwnershipTransferDecision Evaluate(
        bool tenantFound,
        Guid? ownerUserId,
        Guid currentOwnerId,
        bool isSiteAdmin,
        string? tenantStatus,
        bool newOwnerMembershipFound,
        string? newOwnerRole,
        string? newOwnerMembershipStatus)
    {
        if (!tenantFound)
            return TenantOwnershipTransferDecision.TenantNotFound;

        if (ownerUserId != currentOwnerId && !isSiteAdmin)
            return TenantOwnershipTransferDecision.NotCurrentOwner;

        if (string.Equals(tenantStatus, TenantStatusConstants.Deleting, StringComparison.OrdinalIgnoreCase))
            return TenantOwnershipTransferDecision.TenantDeleting;

        if (!newOwnerMembershipFound)
            return TenantOwnershipTransferDecision.NewOwnerNotMember;

        if (!string.Equals(newOwnerRole, RoleConstants.Admin, StringComparison.OrdinalIgnoreCase))
            return TenantOwnershipTransferDecision.NewOwnerNotAdmin;

        if (!string.Equals(newOwnerMembershipStatus, "active", StringComparison.OrdinalIgnoreCase))
            return TenantOwnershipTransferDecision.NewOwnerMembershipNotActive;

        return TenantOwnershipTransferDecision.Allowed;
    }
}
