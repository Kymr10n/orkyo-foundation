using Api.Helpers;
using Api.Services;
using Npgsql;

namespace Orkyo.Foundation.Tests.Services;

public class TenantUserListQueryContractTests
{
    [Fact]
    public void BuildListUsersByTenantSql_SelectsUserHelperColumns()
    {
        var sql = TenantUserListQueryContract.BuildListUsersByTenantSql();

        // SELECT clause delegates to UserHelper.UserSelectColumns; verify a few key tokens.
        sql.Should().Contain("u.id");
        sql.Should().Contain("u.email");
        sql.Should().Contain("u.display_name");
        sql.Should().Contain("u.status");
        sql.Should().Contain("tm.role");
        sql.Should().Contain("u.created_at");
        sql.Should().Contain("u.updated_at");
        sql.Should().Contain("u.last_login_at");
    }

    [Fact]
    public void BuildListUsersByTenantSql_JoinsMembershipsAndOrders()
    {
        var sql = TenantUserListQueryContract.BuildListUsersByTenantSql();

        sql.Should().Contain("FROM users u");
        sql.Should().Contain("INNER JOIN tenant_memberships tm ON u.id = tm.user_id AND tm.tenant_id = @tenantId");
        sql.Should().Contain("ORDER BY u.created_at DESC");
    }

    [Fact]
    public void TenantIdParameterName_IsStable()
    {
        TenantUserListQueryContract.TenantIdParameterName.Should().Be("tenantId");
    }
}

public class TenantUserListCommandFactoryTests
{
    [Fact]
    public void CreateListUsersByTenantCommand_BindsTenantIdParameter()
    {
        using var connection = new NpgsqlConnection();
        var tenantId = Guid.NewGuid();

        using var command = TenantUserListCommandFactory.CreateListUsersByTenantCommand(connection, tenantId);

        command.CommandText.Should().Be(TenantUserListQueryContract.BuildListUsersByTenantSql());
        command.Parameters[TenantUserListQueryContract.TenantIdParameterName].Value.Should().Be(tenantId);
    }
}
