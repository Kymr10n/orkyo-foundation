using Api.Services;

namespace Orkyo.Foundation.Tests.Services;

public class TenantOwnershipEligibilityQueryContractTests
{
    [Fact]
    public void BuildActiveOwnedTenantCountSql_ShouldContainExpectedCountAndPredicate()
    {
        var sql = TenantOwnershipEligibilityQueryContract.BuildActiveOwnedTenantCountSql();

        sql.Should().Contain("SELECT COUNT(*) FROM tenants");
        sql.Should().Contain("WHERE owner_user_id = @userId AND status != 'deleting'");
    }
}
