using Api.Middleware;
using Npgsql;

namespace Api.Helpers;

public static class DbConnectionHelper
{
    public static async Task<NpgsqlConnection> OpenTenantConnectionAsync(this HttpContext context)
    {
        var tenant = context.GetTenantContext();
        var conn = new NpgsqlConnection(tenant.TenantDbConnectionString);
        await conn.OpenAsync();
        return conn;
    }
}
