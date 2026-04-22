using Api.Services;
using Orkyo.Shared;

namespace Orkyo.Foundation.Tests.Services;

public class TenantResolverQueryContractTests
{
    [Fact]
    public void BuildSelectBySlugSql_ShouldContainExpectedProjectionAndFilter()
    {
        var sql = TenantResolverQueryContract.BuildSelectBySlugSql();

        sql.Should().Contain("SELECT id, slug, db_identifier, status, tier, suspension_reason FROM tenants");
        sql.Should().Contain($"slug = @{TenantResolverQueryContract.SlugParameterName}");
        sql.Should().Contain($"status != '{TenantStatusConstants.Deleting}'");
    }

    [Fact]
    public void Ordinals_ShouldMatchSelectedColumnOrder()
    {
        TenantResolverQueryContract.TenantIdOrdinal.Should().Be(0);
        TenantResolverQueryContract.TenantSlugOrdinal.Should().Be(1);
        TenantResolverQueryContract.DbIdentifierOrdinal.Should().Be(2);
        TenantResolverQueryContract.StatusOrdinal.Should().Be(3);
        TenantResolverQueryContract.TierOrdinal.Should().Be(4);
        TenantResolverQueryContract.SuspensionReasonOrdinal.Should().Be(5);
    }
}