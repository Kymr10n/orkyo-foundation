using Npgsql;

namespace Api.Services;

public static class TenantConnectionStringHelper
{
    /// <summary>
    /// Default per-tenant connection-pool ceiling. Npgsql pools per distinct connection string,
    /// i.e. per tenant database, and its own default is 100. With N tenants that is 100·N possible
    /// server connections against a fixed Postgres max_connections (150 in prod) — a handful of
    /// busy tenants can exhaust the server. Cap each tenant's pool so the worst case scales with a
    /// bounded per-tenant number, not the unbounded Npgsql default. Overridable per deployment.
    /// </summary>
    public const int DefaultMaxPoolSize = 10;

    public static string BuildTenantDatabaseConnectionString(
        string controlPlaneConnectionString,
        string tenantDatabaseIdentifier,
        int maxPoolSize = DefaultMaxPoolSize)
    {
        // Use NpgsqlConnectionStringBuilder for safety; string replacement can corrupt other segments.
        // MinPoolSize = 0: tenant pools must be able to shrink to zero when idle; MinPoolSize
        // connections are exempt from Npgsql idle pruning, so any floor > 0 scales idle
        // connections with tenant count.
        var builder = new NpgsqlConnectionStringBuilder(controlPlaneConnectionString)
        {
            Database = tenantDatabaseIdentifier,
            MinPoolSize = 0,
            MaxPoolSize = maxPoolSize
        };

        return builder.ConnectionString;
    }
}
