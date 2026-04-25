using Npgsql;

namespace Api.Services;

public static class TenantMutationCommandFactory
{
    public static NpgsqlCommand CreateMarkDeletingCommand(NpgsqlConnection connection, Guid tenantId)
    {
        var command = new NpgsqlCommand(TenantMutationQueryContract.BuildMarkDeletingSql(), connection);
        command.Parameters.AddWithValue(TenantMutationQueryContract.TenantIdParameterName, tenantId);
        return command;
    }

    public static NpgsqlCommand CreateMarkActiveCommand(NpgsqlConnection connection, Guid tenantId)
    {
        var command = new NpgsqlCommand(TenantMutationQueryContract.BuildMarkActiveSql(), connection);
        command.Parameters.AddWithValue(TenantMutationQueryContract.TenantIdParameterName, tenantId);
        return command;
    }

    public static NpgsqlCommand CreateTouchLastActivityCommand(NpgsqlConnection connection, Guid tenantId)
    {
        var command = new NpgsqlCommand(TenantMutationQueryContract.BuildTouchLastActivitySql(), connection);
        command.Parameters.AddWithValue(TenantMutationQueryContract.TenantIdParameterName, tenantId);
        return command;
    }

    public static NpgsqlCommand CreateUpdateDisplayNameCommand(NpgsqlConnection connection, Guid tenantId, string displayName)
    {
        var command = new NpgsqlCommand(TenantMutationQueryContract.BuildUpdateDisplayNameSql(), connection);
        command.Parameters.AddWithValue(TenantMutationQueryContract.DisplayNameParameterName, displayName);
        command.Parameters.AddWithValue(TenantMutationQueryContract.TenantIdParameterName, tenantId);
        return command;
    }

    public static NpgsqlCommand CreateTransferOwnershipCommand(NpgsqlConnection connection, Guid tenantId, Guid newOwnerId)
    {
        var command = new NpgsqlCommand(TenantMutationQueryContract.BuildTransferOwnershipSql(), connection);
        command.Parameters.AddWithValue(TenantMutationQueryContract.TenantIdParameterName, tenantId);
        command.Parameters.AddWithValue(TenantMutationQueryContract.NewOwnerIdParameterName, newOwnerId);
        return command;
    }

    public static NpgsqlCommand CreateDeleteMembershipCommand(NpgsqlConnection connection, Guid tenantId, Guid userId)
    {
        var command = new NpgsqlCommand(TenantMutationQueryContract.BuildDeleteMembershipSql(), connection);
        command.Parameters.AddWithValue(TenantMutationQueryContract.TenantIdParameterName, tenantId);
        command.Parameters.AddWithValue(TenantMutationQueryContract.UserIdParameterName, userId);
        return command;
    }

    public static NpgsqlCommand CreateReactivateSuspendedTenantCommand(NpgsqlConnection connection, Guid tenantId)
    {
        var command = new NpgsqlCommand(TenantMutationQueryContract.BuildReactivateSuspendedTenantSql(), connection);
        command.Parameters.AddWithValue(TenantMutationQueryContract.TenantIdParameterName, tenantId);
        return command;
    }
}
