using Npgsql;

namespace Api.Services;

public static class TenantReactivationLookupCommandFactory
{
    public static NpgsqlCommand CreateSelectByTenantAndUserCommand(NpgsqlConnection connection, Guid tenantId, Guid userId)
    {
        var command = new NpgsqlCommand(TenantReactivationLookupQueryContract.BuildSelectByTenantAndUserSql(), connection);
        command.Parameters.AddWithValue(TenantReactivationLookupQueryContract.TenantIdParameterName, tenantId);
        command.Parameters.AddWithValue(TenantReactivationLookupQueryContract.UserIdParameterName, userId);
        return command;
    }
}
