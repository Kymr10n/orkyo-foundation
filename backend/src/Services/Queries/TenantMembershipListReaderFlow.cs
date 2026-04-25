using System.Data.Common;

namespace Api.Services;

public readonly record struct TenantMembershipRowSnapshot(
    Guid TenantId,
    string TenantSlug,
    string TenantDisplayName,
    string TenantStatus,
    Guid? OwnerUserId,
    string Role,
    string MembershipStatus,
    DateTime JoinedAt);

public static class TenantMembershipListReaderFlow
{
    public static TenantMembershipRowSnapshot ReadCurrent(DbDataReader reader)
    {
        var ownerUserId = reader.IsDBNull(4) ? (Guid?)null : reader.GetGuid(4);

        return new TenantMembershipRowSnapshot(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            ownerUserId,
            reader.GetString(5),
            reader.GetString(6),
            reader.GetDateTime(7));
    }
}
