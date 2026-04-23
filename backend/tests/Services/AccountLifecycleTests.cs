using Api.Services;
using FluentAssertions;
using Xunit;

namespace Orkyo.Foundation.Tests.Services;

public class AccountLifecycleQueryContractTests
{
    [Fact]
    public void BuildSelectUserByConfirmTokenSql_SelectsExpectedColumns()
    {
        var sql = AccountLifecycleQueryContract.BuildSelectUserByConfirmTokenSql();

        sql.Should().Contain("SELECT id, keycloak_id, display_name, lifecycle_status");
        sql.Should().Contain("FROM users");
    }

    [Fact]
    public void BuildSelectUserByConfirmTokenSql_FiltersByConfirmTokenAndActiveLifecycle()
    {
        var sql = AccountLifecycleQueryContract.BuildSelectUserByConfirmTokenSql();

        sql.Should().Contain("WHERE lifecycle_confirm_token = @token");
        sql.Should().Contain("AND lifecycle_status IS NOT NULL");
    }

    [Fact]
    public void BuildClearLifecycleStateSql_ClearsAllLifecycleColumnsAndBumpsUpdatedAt()
    {
        var sql = AccountLifecycleQueryContract.BuildClearLifecycleStateSql();

        sql.Should().Contain("UPDATE users");
        sql.Should().Contain("lifecycle_status = NULL");
        sql.Should().Contain("lifecycle_warning_count = 0");
        sql.Should().Contain("lifecycle_last_warned_at = NULL");
        sql.Should().Contain("lifecycle_dormant_since = NULL");
        sql.Should().Contain("lifecycle_confirm_token = NULL");
        sql.Should().Contain("updated_at = NOW()");
        sql.Should().Contain("WHERE id = @id");
    }

    [Fact]
    public void ParameterNames_AreStable()
    {
        // Drift guard: command factory + endpoint code rely on these names.
        AccountLifecycleQueryContract.ConfirmTokenParameterName.Should().Be("token");
        AccountLifecycleQueryContract.UserIdParameterName.Should().Be("id");
    }

    [Fact]
    public void DormantLifecycleStatus_IsLowercaseDormant()
    {
        // Drift guard: matches the legacy magic string previously used in the endpoint.
        AccountLifecycleQueryContract.DormantLifecycleStatus.Should().Be("dormant");
    }
}

public class AccountLifecycleCommandFactoryTests
{
    [Fact]
    public void CreateSelectUserByConfirmTokenCommand_BindsTokenParameter()
    {
        const string token = "abc-123";

        using var cmd = AccountLifecycleCommandFactory.CreateSelectUserByConfirmTokenCommand(connection: null!, token);

        cmd.CommandText.Should().Contain("lifecycle_confirm_token = @token");
        cmd.Parameters.Should().ContainSingle();
        cmd.Parameters[0].ParameterName.Should().Be("token");
        cmd.Parameters[0].Value.Should().Be(token);
    }

    [Fact]
    public void CreateClearLifecycleStateCommand_BindsUserIdParameter()
    {
        var userId = Guid.NewGuid();

        using var cmd = AccountLifecycleCommandFactory.CreateClearLifecycleStateCommand(connection: null!, userId);

        cmd.CommandText.Should().Contain("WHERE id = @id");
        cmd.Parameters.Should().ContainSingle();
        cmd.Parameters[0].ParameterName.Should().Be("id");
        cmd.Parameters[0].Value.Should().Be(userId);
    }
}
