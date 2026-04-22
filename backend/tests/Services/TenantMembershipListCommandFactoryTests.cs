using Api.Services;
using Npgsql;

namespace Orkyo.Foundation.Tests.Services;

public class TenantMembershipListCommandFactoryTests
{
    [Fact]
    public void CreateSelectByUserCommand_ShouldBindUserIdParameter()
    {
        using var connection = new NpgsqlConnection();
        var userId = Guid.NewGuid();

        using var command = TenantMembershipListCommandFactory.CreateSelectByUserCommand(connection, userId);

        command.CommandText.Should().Be(TenantMembershipListQueryContract.BuildSelectByUserSql());
        command.Parameters[TenantMembershipListQueryContract.UserIdParameterName].Value.Should().Be(userId);
    }
}
