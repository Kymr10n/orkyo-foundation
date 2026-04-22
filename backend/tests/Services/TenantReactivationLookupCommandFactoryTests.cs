using Api.Services;
using Npgsql;

namespace Orkyo.Foundation.Tests.Services;

public class TenantReactivationLookupCommandFactoryTests
{
    [Fact]
    public void CreateSelectByTenantAndUserCommand_ShouldBindTenantAndUserParameters()
    {
        using var connection = new NpgsqlConnection();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        using var command = TenantReactivationLookupCommandFactory.CreateSelectByTenantAndUserCommand(connection, tenantId, userId);

        command.CommandText.Should().Be(TenantReactivationLookupQueryContract.BuildSelectByTenantAndUserSql());
        command.Parameters[TenantReactivationLookupQueryContract.TenantIdParameterName].Value.Should().Be(tenantId);
        command.Parameters[TenantReactivationLookupQueryContract.UserIdParameterName].Value.Should().Be(userId);
    }
}
