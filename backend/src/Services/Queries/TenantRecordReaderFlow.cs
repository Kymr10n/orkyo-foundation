using System.Data.Common;

namespace Api.Services;

public readonly record struct TenantRecordSnapshot(
    Guid Id,
    string Slug,
    string DisplayName,
    string Status,
    string DbIdentifier,
    Guid? OwnerUserId,
    DateTime CreatedAt);

public static class TenantRecordReaderFlow
{
    public static async Task<TenantRecordSnapshot?> ReadSingleOrNullAsync(DbDataReader reader)
    {
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return ReadCurrent(reader);
    }

    public static TenantRecordSnapshot ReadCurrent(DbDataReader reader)
    {
        var ownerUserId = reader.IsDBNull(TenantRecordQueryContract.OwnerUserIdOrdinal)
            ? (Guid?)null
            : reader.GetGuid(TenantRecordQueryContract.OwnerUserIdOrdinal);

        return new TenantRecordSnapshot(
            reader.GetGuid(TenantRecordQueryContract.IdOrdinal),
            reader.GetString(TenantRecordQueryContract.SlugOrdinal),
            reader.GetString(TenantRecordQueryContract.DisplayNameOrdinal),
            reader.GetString(TenantRecordQueryContract.StatusOrdinal),
            reader.GetString(TenantRecordQueryContract.DbIdentifierOrdinal),
            ownerUserId,
            reader.GetDateTime(TenantRecordQueryContract.CreatedAtOrdinal));
    }
}
