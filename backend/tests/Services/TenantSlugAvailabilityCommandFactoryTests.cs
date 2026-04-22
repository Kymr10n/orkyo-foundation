using Api.Services;
using Npgsql;

namespace Orkyo.Foundation.Tests.Services;

public class TenantSlugAvailabilityCommandFactoryTests
{
    [Fact]
    public void CreateSelectExistingTenantIdBySlugCommand_ShouldBindSlugParameter()
    {
        using var connection = new NpgsqlConnection();

        using var command = TenantSlugAvailabilityCommandFactory.CreateSelectExistingTenantIdBySlugCommand(connection, "acme");

        command.CommandText.Should().Be(TenantSlugAvailabilityQueryContract.BuildSelectExistingTenantIdBySlugSql());
        command.Parameters[TenantSlugAvailabilityQueryContract.SlugParameterName].Value.Should().Be("acme");
    }
}
