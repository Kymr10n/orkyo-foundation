using Npgsql;

namespace Api.Services;

public static class UserLookupByIdCommandFactory
{
    public static NpgsqlCommand CreateExistsByIdCommand(NpgsqlConnection connection, Guid userId)
    {
        var command = new NpgsqlCommand(UserLookupByIdQueryContract.BuildExistsByIdSql(), connection);
        command.Parameters.AddWithValue(UserLookupByIdQueryContract.UserIdParameterName, userId);
        return command;
    }
}

public static class UserLookupByIdScalarFlow
{
    /// <summary>
    /// Read the EXISTS scalar result. Treats <c>null</c> / non-bool as <c>false</c>.
    /// </summary>
    public static bool ReadExists(object? scalarResult) => scalarResult is bool exists && exists;
}
