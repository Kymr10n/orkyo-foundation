using Npgsql;

namespace Api.Services;

public readonly record struct TenantOwnershipTransferLookupSnapshot(
    bool TenantFound,
    Guid? OwnerUserId,
    string? TenantStatus,
    bool NewOwnerMembershipFound,
    string? NewOwnerRole,
    string? NewOwnerMembershipStatus);

public static class TenantOwnershipTransferLookupFlow
{
    public static TenantOwnershipTransferLookupSnapshot FromSnapshots(
        TenantOwnerStatusSnapshot ownerStatus,
        TenantMembershipRoleStatusSnapshot membership)
    {
        return new TenantOwnershipTransferLookupSnapshot(
            ownerStatus.Found,
            ownerStatus.OwnerId,
            ownerStatus.Status,
            membership.Found,
            membership.Role,
            membership.MembershipStatus);
    }

    public static async Task<TenantOwnershipTransferLookupSnapshot> LoadAsync(
        NpgsqlConnection connection,
        Guid tenantId,
        Guid newOwnerId)
    {
        await using var ownerStatusCmd = TenantOwnerStatusCommandFactory.CreateSelectByTenantIdCommand(connection, tenantId);
        await using var ownerStatusReader = await ownerStatusCmd.ExecuteReaderAsync();
        var ownerStatus = await TenantOwnerStatusReaderFlow.ReadSingleOrNotFoundAsync(ownerStatusReader);

        await using var membershipCmd = TenantMembershipRoleStatusCommandFactory.CreateSelectByTenantAndUserCommand(connection, tenantId, newOwnerId);
        await using var membershipReader = await membershipCmd.ExecuteReaderAsync();
        var membership = await TenantMembershipRoleStatusReaderFlow.ReadSingleOrNotFoundAsync(membershipReader);

        return FromSnapshots(ownerStatus, membership);
    }
}
