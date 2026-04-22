using Api.Services;
using Npgsql;

namespace Orkyo.Foundation.Tests.Services;

public class TenantRecordCommandFactoryTests
{
    [Fact]
    public void CreateSelectByIdCommand_ShouldBindTenantIdParameter()
    {
        using var connection = new NpgsqlConnection();
        var tenantId = Guid.NewGuid();

        using var command = TenantRecordCommandFactory.CreateSelectByIdCommand(connection, tenantId);

        command.CommandText.Should().Be(TenantRecordQueryContract.BuildSelectByIdSql());
        command.Parameters[TenantRecordQueryContract.TenantIdParameterName].Value.Should().Be(tenantId);
    }

    [Fact]
    public void CreateSelectBySlugCommand_ShouldBindSlugParameter()
    {
        using var connection = new NpgsqlConnection();

        using var command = TenantRecordCommandFactory.CreateSelectBySlugCommand(connection, "acme");

        command.CommandText.Should().Be(TenantRecordQueryContract.BuildSelectBySlugSql());
        command.Parameters[TenantRecordQueryContract.TenantSlugParameterName].Value.Should().Be("acme");
    }
}
