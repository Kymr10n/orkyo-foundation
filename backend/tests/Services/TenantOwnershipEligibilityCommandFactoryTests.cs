using Api.Services;
using Npgsql;

namespace Orkyo.Foundation.Tests.Services;

public class TenantOwnershipEligibilityCommandFactoryTests
{
    [Fact]
    public void CreateActiveOwnedTenantCountCommand_ShouldBindUserIdParameter()
    {
        using var connection = new NpgsqlConnection();
        var userId = Guid.NewGuid();

        using var command = TenantOwnershipEligibilityCommandFactory.CreateActiveOwnedTenantCountCommand(connection, userId);

        command.CommandText.Should().Be(TenantOwnershipEligibilityQueryContract.BuildActiveOwnedTenantCountSql());
        command.Parameters[TenantOwnershipEligibilityQueryContract.UserIdParameterName].Value.Should().Be(userId);
    }
}
