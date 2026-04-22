using Npgsql;

namespace Api.Services;

public static class TenantConnectionStringHelper
{
    public static string BuildTenantDatabaseConnectionString(string controlPlaneConnectionString, string tenantDatabaseIdentifier)
    {
        // Use NpgsqlConnectionStringBuilder for safety; string replacement can corrupt other segments.
        var builder = new NpgsqlConnectionStringBuilder(controlPlaneConnectionString)
        {
            Database = tenantDatabaseIdentifier
        };

        return builder.ConnectionString;
    }
}