using Api.Services;

namespace Orkyo.Foundation.Tests.Services;

public class TenantSlugAvailabilityQueryContractTests
{
    [Fact]
    public void BuildSelectExistingTenantIdBySlugSql_ShouldContainExpectedProjectionAndParameter()
    {
        var sql = TenantSlugAvailabilityQueryContract.BuildSelectExistingTenantIdBySlugSql();

        sql.Should().Contain("SELECT id FROM tenants WHERE slug = @slug");
    }
}
