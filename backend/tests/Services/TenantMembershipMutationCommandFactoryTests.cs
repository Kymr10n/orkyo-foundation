using Api.Services;
using Npgsql;

namespace Orkyo.Foundation.Tests.Services;

public class TenantMembershipMutationCommandFactoryTests
{
    [Fact]
    public void CreateUpdateMembershipRoleCommand_BindsAllParameters()
    {
        using var connection = new NpgsqlConnection();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        using var command = TenantMembershipMutationCommandFactory.CreateUpdateMembershipRoleCommand(
            connection, tenantId, userId, "admin");

        command.CommandText.Should().Be(TenantMembershipMutationQueryContract.BuildUpdateMembershipRoleSql());
        command.Parameters[TenantMembershipMutationQueryContract.TenantIdParameterName].Value.Should().Be(tenantId);
        command.Parameters[TenantMembershipMutationQueryContract.UserIdParameterName].Value.Should().Be(userId);
        command.Parameters[TenantMembershipMutationQueryContract.RoleParameterName].Value.Should().Be("admin");
    }

    [Fact]
    public void CreateDeleteMembershipCommand_BindsTenantAndUser()
    {
        using var connection = new NpgsqlConnection();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        using var command = TenantMembershipMutationCommandFactory.CreateDeleteMembershipCommand(
            connection, tenantId, userId);

        command.CommandText.Should().Be(TenantMembershipMutationQueryContract.BuildDeleteMembershipSql());
        command.Parameters[TenantMembershipMutationQueryContract.TenantIdParameterName].Value.Should().Be(tenantId);
        command.Parameters[TenantMembershipMutationQueryContract.UserIdParameterName].Value.Should().Be(userId);
    }

    [Fact]
    public void CreateUpsertAdminMembershipCommand_BindsTenantAndUser()
    {
        using var connection = new NpgsqlConnection();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        using var command = TenantMembershipMutationCommandFactory.CreateUpsertAdminMembershipCommand(
            connection, tenantId, userId);

        command.CommandText.Should().Be(TenantMembershipMutationQueryContract.BuildUpsertAdminMembershipSql());
        command.Parameters[TenantMembershipMutationQueryContract.TenantIdParameterName].Value.Should().Be(tenantId);
        command.Parameters[TenantMembershipMutationQueryContract.UserIdParameterName].Value.Should().Be(userId);
    }
}
