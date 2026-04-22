using Api.Services;
using Npgsql;

namespace Orkyo.Foundation.Tests.Services;

public class TenantMembershipRoleStatusCommandFactoryTests
{
    [Fact]
    public void CreateSelectByTenantAndUserCommand_ShouldBindTenantAndUserParameters()
    {
        using var connection = new NpgsqlConnection();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        using var command = TenantMembershipRoleStatusCommandFactory.CreateSelectByTenantAndUserCommand(connection, tenantId, userId);

        command.CommandText.Should().Be(TenantMembershipRoleStatusQueryContract.BuildSelectByTenantAndUserSql());
        command.Parameters.Should().ContainSingle(p => p.ParameterName == TenantMembershipRoleStatusQueryContract.TenantIdParameterName);
        command.Parameters.Should().ContainSingle(p => p.ParameterName == TenantMembershipRoleStatusQueryContract.UserIdParameterName);
        command.Parameters[TenantMembershipRoleStatusQueryContract.TenantIdParameterName].Value.Should().Be(tenantId);
        command.Parameters[TenantMembershipRoleStatusQueryContract.UserIdParameterName].Value.Should().Be(userId);
    }
}
