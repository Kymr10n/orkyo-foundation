using Npgsql;

namespace Api.Services;

public static class ControlPlaneUserMutationCommandFactory
{
    /// <summary>
    /// Build the global user-status update command.
    /// </summary>
    public static NpgsqlCommand CreateSetUserStatusCommand(NpgsqlConnection connection, Guid userId, string status)
    {
        var command = new NpgsqlCommand(ControlPlaneUserMutationQueryContract.BuildSetUserStatusSql(), connection);
        command.Parameters.AddWithValue(ControlPlaneUserMutationQueryContract.StatusParameterName, status);
        command.Parameters.AddWithValue(ControlPlaneUserMutationQueryContract.UserIdParameterName, userId);
        return command;
    }

    /// <summary>
    /// Build the hard-delete user command.
    /// </summary>
    public static NpgsqlCommand CreateDeleteUserCommand(NpgsqlConnection connection, Guid userId)
    {
        var command = new NpgsqlCommand(ControlPlaneUserMutationQueryContract.BuildDeleteUserSql(), connection);
        command.Parameters.AddWithValue(ControlPlaneUserMutationQueryContract.UserIdParameterName, userId);
        return command;
    }
}
