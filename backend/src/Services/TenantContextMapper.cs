using System.Data.Common;
using Api.Models;

namespace Api.Services;

public static class TenantContextMapper
{
    public static TenantContext MapFromResolverRow(DbDataReader reader, string controlPlaneConnectionString)
    {
        var tenantId = reader.GetGuid(TenantResolverQueryContract.TenantIdOrdinal);
        var slug = reader.GetString(TenantResolverQueryContract.TenantSlugOrdinal);
        var dbIdentifier = reader.GetString(TenantResolverQueryContract.DbIdentifierOrdinal);
        var status = reader.GetString(TenantResolverQueryContract.StatusOrdinal);
        var tierValue = reader.GetInt32(TenantResolverQueryContract.TierOrdinal);
        var tier = (ServiceTier)tierValue;
        var suspensionReason = reader.IsDBNull(TenantResolverQueryContract.SuspensionReasonOrdinal)
            ? null
            : reader.GetString(TenantResolverQueryContract.SuspensionReasonOrdinal);

        var tenantConnectionString = TenantConnectionStringHelper.BuildTenantDatabaseConnectionString(
            controlPlaneConnectionString,
            dbIdentifier);

        return new TenantContext
        {
            TenantId = tenantId,
            TenantSlug = slug,
            TenantDbConnectionString = tenantConnectionString,
            Tier = tier,
            Status = status,
            SuspensionReason = suspensionReason
        };
    }
}