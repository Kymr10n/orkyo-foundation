using Npgsql;

namespace Api.Services;

public static class TenantCreationCommandFactory
{
    public static NpgsqlCommand CreateInsertTenantCommand(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string slug,
        string displayName,
        string dbIdentifier,
        Guid ownerId)
    {
        var command = transaction == null
            ? new NpgsqlCommand(TenantCreationQueryContract.BuildInsertTenantSql(), connection)
            : new NpgsqlCommand(TenantCreationQueryContract.BuildInsertTenantSql(), connection, transaction);

        command.Parameters.AddWithValue(TenantCreationQueryContract.SlugParameterName, slug);
        command.Parameters.AddWithValue(TenantCreationQueryContract.DisplayNameParameterName, displayName);
        command.Parameters.AddWithValue(TenantCreationQueryContract.DbIdentifierParameterName, dbIdentifier);
        command.Parameters.AddWithValue(TenantCreationQueryContract.OwnerIdParameterName, ownerId);
        return command;
    }

    public static NpgsqlCommand CreateInsertOwnerMembershipCommand(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid userId,
        Guid tenantId)
    {
        var command = transaction == null
            ? new NpgsqlCommand(TenantCreationQueryContract.BuildInsertOwnerMembershipSql(), connection)
            : new NpgsqlCommand(TenantCreationQueryContract.BuildInsertOwnerMembershipSql(), connection, transaction);

        command.Parameters.AddWithValue(TenantCreationQueryContract.UserIdParameterName, userId);
        command.Parameters.AddWithValue(TenantCreationQueryContract.TenantIdParameterName, tenantId);
        return command;
    }

    public static NpgsqlCommand CreateSelectUserEmailByIdCommand(NpgsqlConnection connection, Guid userId)
    {
        var command = new NpgsqlCommand(TenantCreationQueryContract.BuildSelectUserEmailByIdSql(), connection);
        command.Parameters.AddWithValue(TenantCreationQueryContract.UserIdParameterName, userId);
        return command;
    }
}
