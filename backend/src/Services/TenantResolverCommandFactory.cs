using Npgsql;

namespace Api.Services;

public static class TenantResolverCommandFactory
{
    public static NpgsqlCommand CreateSelectBySlugCommand(NpgsqlConnection connection, string tenantSlug)
    {
        var command = new NpgsqlCommand(TenantResolverQueryContract.BuildSelectBySlugSql(), connection);
        command.Parameters.AddWithValue(TenantResolverQueryContract.SlugParameterName, tenantSlug);
        return command;
    }
}