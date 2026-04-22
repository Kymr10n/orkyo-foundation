using Api.Services;

namespace Orkyo.Foundation.Tests.Services;

public class TenantLeaveLookupQueryContractTests
{
    [Fact]
    public void BuildSelectOwnerUserIdByTenantIdSql_ShouldContainExpectedProjectionAndFilter()
    {
        var sql = TenantLeaveLookupQueryContract.BuildSelectOwnerUserIdByTenantIdSql();

        sql.Should().Contain("SELECT owner_user_id FROM tenants WHERE id = @tenantId");
    }

    [Fact]
    public void BuildSelectActiveAdminCountByTenantIdSql_ShouldContainExpectedProjectionAndFilter()
    {
        var sql = TenantLeaveLookupQueryContract.BuildSelectActiveAdminCountByTenantIdSql();

        sql.Should().Contain("SELECT COUNT(*) FROM tenant_memberships");
        sql.Should().Contain("WHERE tenant_id = @tenantId AND role = 'admin' AND status = 'active'");
    }

    [Fact]
    public void BuildSelectActiveRoleByTenantAndUserSql_ShouldContainExpectedProjectionAndFilter()
    {
        var sql = TenantLeaveLookupQueryContract.BuildSelectActiveRoleByTenantAndUserSql();

        sql.Should().Contain("SELECT role FROM tenant_memberships");
        sql.Should().Contain("WHERE tenant_id = @tenantId AND user_id = @userId AND status = 'active'");
    }
}
