using Npgsql;

namespace Api.Services;

/// <summary>
/// Minimal projection of a user row matched by lifecycle confirm token.
/// Carries everything the confirm-activity flow needs to drive Keycloak
/// re-enable + lifecycle-state clear.
/// </summary>
public sealed record AccountLifecycleConfirmRecord(
    Guid UserId,
    string? KeycloakId,
    string DisplayName,
    bool WasDormant);

public static class AccountLifecycleCommandFactory
{
    public static NpgsqlCommand CreateSelectUserByConfirmTokenCommand(NpgsqlConnection connection, string token)
    {
        var command = new NpgsqlCommand(AccountLifecycleQueryContract.BuildSelectUserByConfirmTokenSql(), connection);
        command.Parameters.AddWithValue(AccountLifecycleQueryContract.ConfirmTokenParameterName, token);
        return command;
    }

    public static NpgsqlCommand CreateClearLifecycleStateCommand(NpgsqlConnection connection, Guid userId)
    {
        var command = new NpgsqlCommand(AccountLifecycleQueryContract.BuildClearLifecycleStateSql(), connection);
        command.Parameters.AddWithValue(AccountLifecycleQueryContract.UserIdParameterName, userId);
        return command;
    }
}

public static class AccountLifecycleReaderFlow
{
    /// <summary>
    /// Drain the first row of an open reader produced by
    /// <see cref="AccountLifecycleCommandFactory.CreateSelectUserByConfirmTokenCommand"/>
    /// into an <see cref="AccountLifecycleConfirmRecord"/>, or <c>null</c> when the
    /// token did not match any active lifecycle record.
    /// </summary>
    public static async Task<AccountLifecycleConfirmRecord?> ReadConfirmRecordAsync(NpgsqlDataReader reader)
    {
        if (!await reader.ReadAsync())
            return null;

        var userId = reader.GetGuid(0);
        var keycloakId = reader.IsDBNull(1) ? null : reader.GetString(1);
        var displayName = reader.GetString(2);
        var wasDormant = !reader.IsDBNull(3)
            && string.Equals(
                reader.GetString(3),
                AccountLifecycleQueryContract.DormantLifecycleStatus,
                StringComparison.Ordinal);

        return new AccountLifecycleConfirmRecord(userId, keycloakId, displayName, wasDormant);
    }
}
