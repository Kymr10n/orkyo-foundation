using Api.Services;

namespace Orkyo.Foundation.Tests.Services;

public class TenantOwnerStatusQueryContractTests
{
    [Fact]
    public void BuildSelectByTenantIdSql_ShouldContainExpectedProjectionAndParameter()
    {
        var sql = TenantOwnerStatusQueryContract.BuildSelectByTenantIdSql();

        sql.Should().Contain("SELECT owner_user_id, status");
        sql.Should().Contain("FROM tenants");
        sql.Should().Contain("WHERE id = @tenantId");
    }
}
