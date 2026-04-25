using Npgsql;

namespace Api.Services;

public static class TenantMembershipListCommandFactory
{
    public static NpgsqlCommand CreateSelectByUserCommand(NpgsqlConnection connection, Guid userId)
    {
        var command = new NpgsqlCommand(TenantMembershipListQueryContract.BuildSelectByUserSql(), connection);
        command.Parameters.AddWithValue(TenantMembershipListQueryContract.UserIdParameterName, userId);
        return command;
    }
}
