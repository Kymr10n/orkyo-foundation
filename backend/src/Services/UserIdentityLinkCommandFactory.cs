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

    public static NpgsqlCommand CreateSelectKeycloakSubjectByUserIdCommand(NpgsqlConnection connection, Guid userId)
    {
        var command = new NpgsqlCommand(
            UserIdentityLinkQueryContract.BuildSelectKeycloakSubjectByUserIdSql(), connection);
        command.Parameters.AddWithValue(UserIdentityLinkQueryContract.UserIdParameterName, userId);
        return command;
    }

    public static NpgsqlCommand CreateSelectIdentitiesByUserIdCommand(NpgsqlConnection connection, Guid userId)
    {
        var command = new NpgsqlCommand(
            UserIdentityLinkQueryContract.BuildSelectIdentitiesByUserIdSql(), connection);
        command.Parameters.AddWithValue(UserIdentityLinkQueryContract.UserIdParameterName, userId);
        return command;
    }
}

public static class UserIdentityLinkReaderFlow
{
    /// <summary>
    /// Drain an <see cref="NpgsqlDataReader"/> whose row shape matches
    /// <see cref="UserIdentityLinkQueryContract.IdentitySelectColumns"/> and project
    /// each row into a <see cref="UserIdentityRow"/>. Allocates and returns a list.
    /// </summary>
    public static async Task<List<UserIdentityRow>> ReadIdentitiesAsync(NpgsqlDataReader reader)
    {
        var rows = new List<UserIdentityRow>();
        while (await reader.ReadAsync())
        {
            rows.Add(new UserIdentityRow(
                Id: reader.GetGuid(0),
                Provider: reader.GetString(1),
                ProviderSubject: reader.GetString(2),
                ProviderEmail: reader.IsDBNull(3) ? null : reader.GetString(3),
                CreatedAt: reader.GetDateTime(4)));
        }
        return rows;
    }
}

public static class UserIdentityLinkScalarFlow
{
    /// <summary>
    /// Read the Keycloak <c>provider_subject</c> scalar result. Returns <c>null</c>
    /// when the user has no Keycloak identity.
    /// </summary>
    public static string? ReadKeycloakSubject(object? scalarResult) => scalarResult as string;
}
