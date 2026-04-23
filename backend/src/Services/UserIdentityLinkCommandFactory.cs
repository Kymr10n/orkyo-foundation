using Npgsql;

namespace Api.Services;

public static class UserIdentityLinkCommandFactory
{
    public static NpgsqlCommand CreateSelectActiveUserByExternalIdentityCommand(
        NpgsqlConnection connection, string externalSubject)
    {
        var command = new NpgsqlCommand(
            UserIdentityLinkQueryContract.BuildSelectActiveUserByExternalIdentitySql(), connection);
        command.Parameters.AddWithValue(UserIdentityLinkQueryContract.SubjectParameterName, externalSubject);
        return command;
    }

    public static NpgsqlCommand CreateInsertIdentityLinkCommand(
        NpgsqlConnection connection, Guid userId, string subject, string? email)
    {
        var command = new NpgsqlCommand(UserIdentityLinkQueryContract.BuildInsertIdentityLinkSql(), connection);
        command.Parameters.AddWithValue(UserIdentityLinkQueryContract.UserIdParameterName, userId);
        command.Parameters.AddWithValue(UserIdentityLinkQueryContract.SubjectParameterName, subject);
        command.Parameters.AddWithValue(
            UserIdentityLinkQueryContract.EmailParameterName, (object?)email ?? DBNull.Value);
        return command;
    }

    public static NpgsqlCommand CreateUpdateLastLoginCommand(NpgsqlConnection connection, Guid userId)
    {
        var command = new NpgsqlCommand(UserIdentityLinkQueryContract.BuildUpdateLastLoginSql(), connection);
        command.Parameters.AddWithValue(UserIdentityLinkQueryContract.LastLoginUserIdParameterName, userId);
        return command;
    }
}
