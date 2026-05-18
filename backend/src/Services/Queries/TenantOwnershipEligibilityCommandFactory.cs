using Npgsql;

namespace Api.Services;

public static class TenantOwnershipEligibilityCommandFactory
{
    public static NpgsqlCommand CreateActiveOwnedTenantCountCommand(NpgsqlConnection connection, Guid userId)
    {
        var command = new NpgsqlCommand(TenantOwnershipEligibilityQueryContract.BuildActiveOwnedTenantCountSql(), connection);
        command.Parameters.AddWithValue(TenantOwnershipEligibilityQueryContract.UserIdParameterName, userId);
        return command;
    }
}
