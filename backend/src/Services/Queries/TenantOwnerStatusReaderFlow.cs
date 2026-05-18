using System.Data.Common;

namespace Api.Services;

public readonly record struct TenantOwnerStatusSnapshot(Guid? OwnerId, string? Status, bool Found);

public static class TenantOwnerStatusReaderFlow
{
    public static async Task<TenantOwnerStatusSnapshot> ReadSingleOrNotFoundAsync(DbDataReader reader)
    {
        if (!await reader.ReadAsync())
        {
            return new TenantOwnerStatusSnapshot(null, null, false);
        }

        var ownerId = reader.IsDBNull(0) ? (Guid?)null : reader.GetGuid(0);
        var status = reader.GetString(1);
        return new TenantOwnerStatusSnapshot(ownerId, status, true);
    }
}
