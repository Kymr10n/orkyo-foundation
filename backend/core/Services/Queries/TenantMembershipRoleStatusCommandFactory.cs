using Npgsql;

namespace Api.Services;

public static class TenantMembershipRoleStatusCommandFactory
{
    public static NpgsqlCommand CreateSelectByTenantAndUserCommand(NpgsqlConnection connection, Guid tenantId, Guid userId)
    {
        var command = new NpgsqlCommand(TenantMembershipRoleStatusQueryContract.BuildSelectByTenantAndUserSql(), connection);
        command.Parameters.AddWithValue(TenantMembershipRoleStatusQueryContract.TenantIdParameterName, tenantId);
        command.Parameters.AddWithValue(TenantMembershipRoleStatusQueryContract.UserIdParameterName, userId);
        return command;
    }
}
