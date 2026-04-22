using Api.Services;
using Npgsql;

namespace Orkyo.Foundation.Tests.Services;

public class TenantOwnerStatusCommandFactoryTests
{
    [Fact]
    public void CreateSelectByTenantIdCommand_ShouldBindTenantIdParameter()
    {
        using var connection = new NpgsqlConnection();
        var tenantId = Guid.NewGuid();

        using var command = TenantOwnerStatusCommandFactory.CreateSelectByTenantIdCommand(connection, tenantId);

        command.CommandText.Should().Be(TenantOwnerStatusQueryContract.BuildSelectByTenantIdSql());
        command.Parameters.Should().ContainSingle(p => p.ParameterName == TenantOwnerStatusQueryContract.TenantIdParameterName);
        command.Parameters[TenantOwnerStatusQueryContract.TenantIdParameterName].Value.Should().Be(tenantId);
    }
}
