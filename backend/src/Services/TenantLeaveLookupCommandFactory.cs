using Npgsql;

namespace Api.Services;

public static class TenantLeaveLookupCommandFactory
{
    public static NpgsqlCommand CreateSelectOwnerUserIdByTenantIdCommand(NpgsqlConnection connection, Guid tenantId)
    {
        var command = new NpgsqlCommand(TenantLeaveLookupQueryContract.BuildSelectOwnerUserIdByTenantIdSql(), connection);
        command.Parameters.AddWithValue(TenantLeaveLookupQueryContract.TenantIdParameterName, tenantId);
        return command;
    }

    public static NpgsqlCommand CreateSelectActiveAdminCountByTenantIdCommand(NpgsqlConnection connection, Guid tenantId)
    {
        var command = new NpgsqlCommand(TenantLeaveLookupQueryContract.BuildSelectActiveAdminCountByTenantIdSql(), connection);
        command.Parameters.AddWithValue(TenantLeaveLookupQueryContract.TenantIdParameterName, tenantId);
        return command;
    }

    public static NpgsqlCommand CreateSelectActiveRoleByTenantAndUserCommand(NpgsqlConnection connection, Guid tenantId, Guid userId)
    {
        var command = new NpgsqlCommand(TenantLeaveLookupQueryContract.BuildSelectActiveRoleByTenantAndUserSql(), connection);
        command.Parameters.AddWithValue(TenantLeaveLookupQueryContract.TenantIdParameterName, tenantId);
        command.Parameters.AddWithValue(TenantLeaveLookupQueryContract.UserIdParameterName, userId);
        return command;
    }
}
