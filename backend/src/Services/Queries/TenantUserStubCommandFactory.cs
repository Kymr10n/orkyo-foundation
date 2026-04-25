using Npgsql;

namespace Api.Services;

public static class TenantUserStubCommandFactory
{
    /// <summary>
    /// Build the user-stub insert command. The caller is responsible for
    /// normalizing the email (e.g. lowercasing) before passing it in.
    /// </summary>
    public static NpgsqlCommand CreateInsertUserStubCommand(NpgsqlConnection connection, Guid userId, string normalizedEmail)
    {
        var command = new NpgsqlCommand(TenantUserStubQueryContract.BuildInsertUserStubSql(), connection);
        command.Parameters.AddWithValue(TenantUserStubQueryContract.IdParameterName, userId);
        command.Parameters.AddWithValue(TenantUserStubQueryContract.EmailParameterName, normalizedEmail);
        return command;
    }
}
