using System.Data.Common;
using Api.Models;

namespace Api.Services;

public static class TenantContextMapper
{
    // Column ordinals matching the SELECT: id, slug, db_identifier, status, tier
    public static TenantContext MapFromResolverRow(DbDataReader reader, string controlPlaneConnectionString)
    {
        var tenantId = reader.GetGuid(0);
        var slug = reader.GetString(1);
        var dbIdentifier = reader.GetString(2);
        var status = reader.GetString(3);
        var tier = (ServiceTier)reader.GetInt32(4);

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
        };
    }
}
