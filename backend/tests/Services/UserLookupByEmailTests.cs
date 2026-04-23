using Api.Services;
using Npgsql;

namespace Orkyo.Foundation.Tests.Services;

public class UserLookupByEmailQueryContractTests
{
    [Fact]
    public void BuildSelectUserIdByEmailSql_TargetsUsersTableAndFiltersByEmail()
    {
        var sql = UserLookupByEmailQueryContract.BuildSelectUserIdByEmailSql();

        sql.Should().Contain("SELECT id FROM users");
        sql.Should().Contain("WHERE email = @email");
    }

    [Fact]
    public void EmailParameterName_IsStable()
    {
        UserLookupByEmailQueryContract.EmailParameterName.Should().Be("email");
    }
}

public class UserLookupByEmailCommandFactoryTests
{
    [Fact]
    public void CreateSelectUserIdByEmailCommand_BindsEmail()
    {
        using var connection = new NpgsqlConnection();

        using var command = UserLookupByEmailCommandFactory.CreateSelectUserIdByEmailCommand(connection, "alice@example.com");

        command.CommandText.Should().Be(UserLookupByEmailQueryContract.BuildSelectUserIdByEmailSql());
        command.Parameters[UserLookupByEmailQueryContract.EmailParameterName].Value.Should().Be("alice@example.com");
    }
}

public class UserLookupByEmailScalarFlowTests
{
    [Fact]
    public void ReadUserId_GuidScalar_ReturnsGuid()
    {
        var id = Guid.NewGuid();
        UserLookupByEmailScalarFlow.ReadUserId(id).Should().Be(id);
    }

    [Fact]
    public void ReadUserId_NullScalar_ReturnsNull()
    {
        UserLookupByEmailScalarFlow.ReadUserId(null).Should().BeNull();
    }

    [Fact]
    public void ReadUserId_NonGuidScalar_ReturnsNull()
    {
        UserLookupByEmailScalarFlow.ReadUserId("not-a-guid").Should().BeNull();
    }
}
