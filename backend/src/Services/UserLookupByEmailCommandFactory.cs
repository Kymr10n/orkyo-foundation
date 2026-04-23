using Npgsql;

namespace Api.Services;

public static class UserLookupByEmailCommandFactory
{
    public static NpgsqlCommand CreateSelectUserIdByEmailCommand(NpgsqlConnection connection, string email)
    {
        var command = new NpgsqlCommand(UserLookupByEmailQueryContract.BuildSelectUserIdByEmailSql(), connection);
        command.Parameters.AddWithValue(UserLookupByEmailQueryContract.EmailParameterName, email);
        return command;
    }
}
