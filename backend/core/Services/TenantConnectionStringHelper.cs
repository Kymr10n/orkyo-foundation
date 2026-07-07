using Npgsql;

namespace Api.Services;

public static class TenantConnectionStringHelper
{
    public static string BuildTenantDatabaseConnectionString(string controlPlaneConnectionString, string tenantDatabaseIdentifier)
    {
        // Use NpgsqlConnectionStringBuilder for safety; string replacement can corrupt other segments.
        // MinPoolSize = 0: tenant pools must be able to shrink to zero when idle; MinPoolSize
        // connections are exempt from Npgsql idle pruning, so any floor > 0 scales idle
        // connections with tenant count.
        var builder = new NpgsqlConnectionStringBuilder(controlPlaneConnectionString)
        {
            Database = tenantDatabaseIdentifier,
            MinPoolSize = 0
        };

        return builder.ConnectionString;
    }
}
