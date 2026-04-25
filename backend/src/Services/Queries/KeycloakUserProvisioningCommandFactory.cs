using Npgsql;

namespace Api.Services;

public static class KeycloakUserProvisioningCommandFactory
{
    public static NpgsqlCommand CreateSelectUserByLowerEmailCommand(NpgsqlConnection connection, string email)
    {
        var command = new NpgsqlCommand(KeycloakUserProvisioningQueryContract.BuildSelectUserByLowerEmailSql(), connection);
        command.Parameters.AddWithValue(KeycloakUserProvisioningQueryContract.EmailParameterName, email);
        return command;
    }

    public static NpgsqlCommand CreateInsertNewActiveUserCommand(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid userId,
        string email,
        string displayName)
    {
        var command = new NpgsqlCommand(
            KeycloakUserProvisioningQueryContract.BuildInsertNewActiveUserSql(), connection, transaction);
        command.Parameters.AddWithValue(KeycloakUserProvisioningQueryContract.IdParameterName, userId);
        command.Parameters.AddWithValue(KeycloakUserProvisioningQueryContract.EmailParameterName, email);
        command.Parameters.AddWithValue(KeycloakUserProvisioningQueryContract.DisplayNameParameterName, displayName);
        return command;
    }

    public static NpgsqlCommand CreateInsertIdentityLinkWithIdCommand(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid identityLinkId,
        Guid userId,
        string subject,
        string email)
    {
        var command = new NpgsqlCommand(
            KeycloakUserProvisioningQueryContract.BuildInsertIdentityLinkWithIdSql(), connection, transaction);
        command.Parameters.AddWithValue(KeycloakUserProvisioningQueryContract.IdParameterName, identityLinkId);
        command.Parameters.AddWithValue(KeycloakUserProvisioningQueryContract.UserIdParameterName, userId);
        command.Parameters.AddWithValue(KeycloakUserProvisioningQueryContract.SubjectParameterName, subject);
        command.Parameters.AddWithValue(KeycloakUserProvisioningQueryContract.EmailParameterName, email);
        return command;
    }
}
