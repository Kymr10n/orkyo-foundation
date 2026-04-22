using Api.Services;

namespace Orkyo.Foundation.Tests.Services;

public class TenantRecordQueryContractTests
{
    [Fact]
    public void BuildSelectByIdSql_ShouldContainProjectionAndIdParameter()
    {
        var sql = TenantRecordQueryContract.BuildSelectByIdSql();

        sql.Should().Contain("SELECT id, slug, display_name, status, db_identifier, owner_user_id, created_at");
        sql.Should().Contain("FROM tenants WHERE id = @id");
    }

    [Fact]
    public void BuildSelectBySlugSql_ShouldContainProjectionAndSlugParameter()
    {
        var sql = TenantRecordQueryContract.BuildSelectBySlugSql();

        sql.Should().Contain("SELECT id, slug, display_name, status, db_identifier, owner_user_id, created_at");
        sql.Should().Contain("FROM tenants WHERE slug = @slug");
    }
}
