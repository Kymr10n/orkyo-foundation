using Api.Services;
using Npgsql;

namespace Orkyo.Foundation.Tests.Services;

public class TenantResolverCommandFactoryTests
{
    [Fact]
    public void CreateSelectBySlugCommand_ShouldUseSharedQueryContractSql()
    {
        using var connection = new NpgsqlConnection("Host=localhost;Database=control_plane;Username=postgres;Password=postgres");

        using var command = TenantResolverCommandFactory.CreateSelectBySlugCommand(connection, "acme");

        command.CommandText.Should().Be(TenantResolverQueryContract.BuildSelectBySlugSql());
    }

    [Fact]
    public void CreateSelectBySlugCommand_ShouldBindSlugParameterFromContract()
    {
        using var connection = new NpgsqlConnection("Host=localhost;Database=control_plane;Username=postgres;Password=postgres");

        using var command = TenantResolverCommandFactory.CreateSelectBySlugCommand(connection, "blue");

        command.Parameters.Count.Should().Be(1);
        command.Parameters[0].ParameterName.Should().Be(TenantResolverQueryContract.SlugParameterName);
        command.Parameters[0].Value.Should().Be("blue");
    }
}