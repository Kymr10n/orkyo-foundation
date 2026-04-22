using Api.Services;

namespace Orkyo.Foundation.Tests.Services;

public class TenantMembershipRoleStatusQueryContractTests
{
    [Fact]
    public void BuildSelectByTenantAndUserSql_ShouldContainExpectedProjectionAndParameters()
    {
        var sql = TenantMembershipRoleStatusQueryContract.BuildSelectByTenantAndUserSql();

        sql.Should().Contain("SELECT role, status");
        sql.Should().Contain("FROM tenant_memberships");
        sql.Should().Contain("WHERE tenant_id = @tenantId AND user_id = @userId");
    }
}
