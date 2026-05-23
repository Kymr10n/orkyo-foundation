using System.Data.Common;

namespace Api.Services;

public readonly record struct TenantMembershipRoleStatusSnapshot(bool Found, string? Role, string? MembershipStatus);

public static class TenantMembershipRoleStatusReaderFlow
{
    public static async Task<TenantMembershipRoleStatusSnapshot> ReadSingleOrNotFoundAsync(DbDataReader reader)
    {
        if (!await reader.ReadAsync())
        {
            return new TenantMembershipRoleStatusSnapshot(false, null, null);
        }

        var role = reader.GetString(0);
        var membershipStatus = reader.GetString(1);
        return new TenantMembershipRoleStatusSnapshot(true, role, membershipStatus);
    }
}
