using Api.Services;
using Npgsql;

namespace Orkyo.Foundation.Tests.Services;

public class ControlPlaneUserMutationQueryContractTests
{
    [Fact]
    public void BuildSetUserStatusSql_TargetsUsersWithStatusAndUpdatedAt()
    {
        var sql = ControlPlaneUserMutationQueryContract.BuildSetUserStatusSql();

        sql.Should().Contain("UPDATE users");
        sql.Should().Contain("SET status = @status");
        sql.Should().Contain("updated_at = NOW()");
        sql.Should().Contain("WHERE id = @userId");
    }

    [Fact]
    public void BuildDeleteUserSql_DeletesByUserId()
    {
        var sql = ControlPlaneUserMutationQueryContract.BuildDeleteUserSql();

        sql.Should().Contain("DELETE FROM users");
        sql.Should().Contain("WHERE id = @userId");
    }

    [Fact]
    public void ParameterNames_AreStable()
    {
        ControlPlaneUserMutationQueryContract.UserIdParameterName.Should().Be("userId");
        ControlPlaneUserMutationQueryContract.StatusParameterName.Should().Be("status");
    }
}

public class ControlPlaneUserMutationCommandFactoryTests
{
    [Fact]
    public void CreateSetUserStatusCommand_BindsStatusAndUserId()
    {
        using var connection = new NpgsqlConnection();
        var userId = Guid.NewGuid();

        using var command = ControlPlaneUserMutationCommandFactory.CreateSetUserStatusCommand(
            connection, userId, "disabled");

        command.CommandText.Should().Be(ControlPlaneUserMutationQueryContract.BuildSetUserStatusSql());
        command.Parameters[ControlPlaneUserMutationQueryContract.StatusParameterName].Value.Should().Be("disabled");
        command.Parameters[ControlPlaneUserMutationQueryContract.UserIdParameterName].Value.Should().Be(userId);
    }

    [Fact]
    public void CreateDeleteUserCommand_BindsUserId()
    {
        using var connection = new NpgsqlConnection();
        var userId = Guid.NewGuid();

        using var command = ControlPlaneUserMutationCommandFactory.CreateDeleteUserCommand(connection, userId);

        command.CommandText.Should().Be(ControlPlaneUserMutationQueryContract.BuildDeleteUserSql());
        command.Parameters[ControlPlaneUserMutationQueryContract.UserIdParameterName].Value.Should().Be(userId);
    }
}
