using System.Data.Common;

namespace Api.Services;

public static class TenantResolverReaderFlow
{
    public static async Task<TenantContext?> ReadSingleOrNullAsync(DbDataReader reader, string controlPlaneConnectionString)
    {
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return TenantContextMapper.MapFromResolverRow(reader, controlPlaneConnectionString);
    }
}