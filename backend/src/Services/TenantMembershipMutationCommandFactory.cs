using Npgsql;

namespace Api.Services;

/// <summary>
/// Command-construction helpers for tenant_memberships mutations driven by
/// user-management operations. Keeps SQL/parameter binding contract aligned
/// with <see cref="TenantMembershipMutationQueryContract"/>.
/// </summary>
public static class TenantMembershipMutationCommandFactory
{
    public static NpgsqlCommand CreateUpdateMembershipRoleCommand(
        NpgsqlConnection connection, Guid tenantId, Guid userId, string role)
    {
        var command = new NpgsqlCommand(
            TenantMembershipMutationQueryContract.BuildUpdateMembershipRoleSql(), connection);
        command.Parameters.AddWithValue(TenantMembershipMutationQueryContract.RoleParameterName, role);
        command.Parameters.AddWithValue(TenantMembershipMutationQueryContract.UserIdParameterName, userId);
        command.Parameters.AddWithValue(TenantMembershipMutationQueryContract.TenantIdParameterName, tenantId);
        return command;
    }

    public static NpgsqlCommand CreateDeleteMembershipCommand(
        NpgsqlConnection connection, Guid tenantId, Guid userId)
    {
        var command = new NpgsqlCommand(
            TenantMembershipMutationQueryContract.BuildDeleteMembershipSql(), connection);
        command.Parameters.AddWithValue(TenantMembershipMutationQueryContract.UserIdParameterName, userId);
        command.Parameters.AddWithValue(TenantMembershipMutationQueryContract.TenantIdParameterName, tenantId);
        return command;
    }

    public static NpgsqlCommand CreateUpsertAdminMembershipCommand(
        NpgsqlConnection connection, Guid tenantId, Guid userId)
    {
        var command = new NpgsqlCommand(
            TenantMembershipMutationQueryContract.BuildUpsertAdminMembershipSql(), connection);
        command.Parameters.AddWithValue(TenantMembershipMutationQueryContract.UserIdParameterName, userId);
        command.Parameters.AddWithValue(TenantMembershipMutationQueryContract.TenantIdParameterName, tenantId);
        return command;
    }
}
