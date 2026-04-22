using Api.Services;

namespace Orkyo.Foundation.Tests.Services;

public class TenantMembershipListQueryContractTests
{
    [Fact]
    public void BuildSelectByUserSql_ShouldContainExpectedJoinFilterAndOrdering()
    {
        var sql = TenantMembershipListQueryContract.BuildSelectByUserSql();

        sql.Should().Contain("SELECT");
        sql.Should().Contain("FROM tenant_memberships tm");
        sql.Should().Contain("JOIN tenants t ON t.id = tm.tenant_id");
        sql.Should().Contain("WHERE tm.user_id = @userId");
        sql.Should().Contain("ORDER BY tm.created_at DESC");
    }
}
