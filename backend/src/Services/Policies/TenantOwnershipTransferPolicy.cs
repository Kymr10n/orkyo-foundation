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
        var ownerDecision = TenantOwnerAccessPolicy.Evaluate(tenantFound, ownerUserId, currentOwnerId, isSiteAdmin);
        if (ownerDecision == TenantOwnerAccessDecision.TenantNotFound)
            return TenantOwnershipTransferDecision.TenantNotFound;

        if (ownerDecision == TenantOwnerAccessDecision.NotOwner)
            return TenantOwnershipTransferDecision.NotCurrentOwner;

        if (TenantOwnerAccessPolicy.IsDeleting(tenantStatus))
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
