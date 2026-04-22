using System.Data.Common;

namespace Api.Services;

public readonly record struct TenantReactivationLookupSnapshot(
    bool Found,
    string? Status,
    string? SuspensionReason,
    string? Role,
    Guid? OwnerUserId);

public static class TenantReactivationLookupReaderFlow
{
    public static async Task<TenantReactivationLookupSnapshot> ReadSingleOrNotFoundAsync(DbDataReader reader)
    {
        if (!await reader.ReadAsync())
        {
            return new TenantReactivationLookupSnapshot(false, null, null, null, null);
        }

        var status = reader.GetString(0);
        var suspensionReason = reader.IsDBNull(1) ? null : reader.GetString(1);
        var role = reader.GetString(2);
        var ownerUserId = reader.IsDBNull(3) ? (Guid?)null : reader.GetGuid(3);
        return new TenantReactivationLookupSnapshot(true, status, suspensionReason, role, ownerUserId);
    }
}
