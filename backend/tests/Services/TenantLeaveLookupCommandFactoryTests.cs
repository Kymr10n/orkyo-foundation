using Api.Services;
using Npgsql;

namespace Orkyo.Foundation.Tests.Services;

public class TenantLeaveLookupCommandFactoryTests
{
    [Fact]
    public void CreateSelectOwnerUserIdByTenantIdCommand_ShouldBindTenantId()
    {
        using var connection = new NpgsqlConnection();
        var tenantId = Guid.NewGuid();

        using var command = TenantLeaveLookupCommandFactory.CreateSelectOwnerUserIdByTenantIdCommand(connection, tenantId);

        command.CommandText.Should().Be(TenantLeaveLookupQueryContract.BuildSelectOwnerUserIdByTenantIdSql());
        command.Parameters[TenantLeaveLookupQueryContract.TenantIdParameterName].Value.Should().Be(tenantId);
    }

    [Fact]
    public void CreateSelectActiveAdminCountByTenantIdCommand_ShouldBindTenantId()
    {
        using var connection = new NpgsqlConnection();
        var tenantId = Guid.NewGuid();

        using var command = TenantLeaveLookupCommandFactory.CreateSelectActiveAdminCountByTenantIdCommand(connection, tenantId);

        command.CommandText.Should().Be(TenantLeaveLookupQueryContract.BuildSelectActiveAdminCountByTenantIdSql());
        command.Parameters[TenantLeaveLookupQueryContract.TenantIdParameterName].Value.Should().Be(tenantId);
    }

    [Fact]
    public void CreateSelectActiveRoleByTenantAndUserCommand_ShouldBindTenantIdAndUserId()
    {
        using var connection = new NpgsqlConnection();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        using var command = TenantLeaveLookupCommandFactory.CreateSelectActiveRoleByTenantAndUserCommand(connection, tenantId, userId);

        command.CommandText.Should().Be(TenantLeaveLookupQueryContract.BuildSelectActiveRoleByTenantAndUserSql());
        command.Parameters[TenantLeaveLookupQueryContract.TenantIdParameterName].Value.Should().Be(tenantId);
        command.Parameters[TenantLeaveLookupQueryContract.UserIdParameterName].Value.Should().Be(userId);
    }
}
