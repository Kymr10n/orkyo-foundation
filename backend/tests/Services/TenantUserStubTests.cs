using Api.Services;
using Npgsql;

namespace Orkyo.Foundation.Tests.Services;

public class TenantUserStubQueryContractTests
{
    [Fact]
    public void BuildInsertUserStubSql_TargetsUsersAndIsConflictSafe()
    {
        var sql = TenantUserStubQueryContract.BuildInsertUserStubSql();

        sql.Should().Contain("INSERT INTO users (id, email, created_at)");
        sql.Should().Contain("VALUES (@id, @email, NOW())");
        sql.Should().Contain("ON CONFLICT DO NOTHING");
    }

    [Fact]
    public void ParameterNames_AreStable()
    {
        TenantUserStubQueryContract.IdParameterName.Should().Be("id");
        TenantUserStubQueryContract.EmailParameterName.Should().Be("email");
    }
}

public class TenantUserStubCommandFactoryTests
{
    [Fact]
    public void CreateInsertUserStubCommand_BindsIdAndNormalizedEmail()
    {
        using var connection = new NpgsqlConnection();
        var userId = Guid.NewGuid();

        using var command = TenantUserStubCommandFactory.CreateInsertUserStubCommand(
            connection, userId, "alice@example.com");

        command.CommandText.Should().Be(TenantUserStubQueryContract.BuildInsertUserStubSql());
        command.Parameters[TenantUserStubQueryContract.IdParameterName].Value.Should().Be(userId);
        command.Parameters[TenantUserStubQueryContract.EmailParameterName].Value.Should().Be("alice@example.com");
    }
}
