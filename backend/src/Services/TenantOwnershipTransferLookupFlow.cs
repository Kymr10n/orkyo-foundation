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
        // Each reader must be fully disposed before starting the next command on the same
        // connection — Npgsql does not support concurrent active result sets. Scoping each
        // read inside its own block guarantees `await using` runs the async disposal before
        // the second ExecuteReaderAsync is invoked.
        TenantOwnerStatusSnapshot ownerStatus;
        {
            await using var ownerStatusCmd = TenantOwnerStatusCommandFactory.CreateSelectByTenantIdCommand(connection, tenantId);
            await using var ownerStatusReader = await ownerStatusCmd.ExecuteReaderAsync();
            ownerStatus = await TenantOwnerStatusReaderFlow.ReadSingleOrNotFoundAsync(ownerStatusReader);
        }

        TenantMembershipRoleStatusSnapshot membership;
        {
            await using var membershipCmd = TenantMembershipRoleStatusCommandFactory.CreateSelectByTenantAndUserCommand(connection, tenantId, newOwnerId);
            await using var membershipReader = await membershipCmd.ExecuteReaderAsync();
            membership = await TenantMembershipRoleStatusReaderFlow.ReadSingleOrNotFoundAsync(membershipReader);
        }

        return FromSnapshots(ownerStatus, membership);
    }
}
