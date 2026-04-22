using Api.Services;

namespace Orkyo.Foundation.Tests.Services;

public class TenantReactivationLookupQueryContractTests
{
    [Fact]
    public void BuildSelectByTenantAndUserSql_ShouldContainExpectedJoinAndFilters()
    {
        var sql = TenantReactivationLookupQueryContract.BuildSelectByTenantAndUserSql();

        sql.Should().Contain("SELECT t.status, t.suspension_reason, tm.role, t.owner_user_id");
        sql.Should().Contain("FROM tenants t");
        sql.Should().Contain("JOIN tenant_memberships tm ON tm.tenant_id = t.id");
        sql.Should().Contain("WHERE t.id = @tenantId");
        sql.Should().Contain("AND tm.user_id = @userId");
        sql.Should().Contain("AND tm.status = 'active'");
    }
}
