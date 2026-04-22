using Api.Services;

namespace Orkyo.Foundation.Tests.Services;

public class TenantCreationQueryContractTests
{
    [Fact]
    public void BuildInsertTenantSql_ShouldContainExpectedInsertAndReturningProjection()
    {
        var sql = TenantCreationQueryContract.BuildInsertTenantSql();

        sql.Should().Contain("INSERT INTO tenants");
        sql.Should().Contain("VALUES (@slug, @displayName, 'active', @dbIdentifier, @ownerId");
        sql.Should().Contain("RETURNING id, slug, display_name, status, db_identifier, owner_user_id, created_at");
    }

    [Fact]
    public void BuildInsertOwnerMembershipSql_ShouldContainExpectedInsert()
    {
        var sql = TenantCreationQueryContract.BuildInsertOwnerMembershipSql();

        sql.Should().Contain("INSERT INTO tenant_memberships");
        sql.Should().Contain("VALUES (@userId, @tenantId, 'admin', 'active'");
    }

    [Fact]
    public void BuildSelectUserEmailByIdSql_ShouldContainExpectedSelect()
    {
        var sql = TenantCreationQueryContract.BuildSelectUserEmailByIdSql();

        sql.Should().Contain("SELECT email FROM users WHERE id = @userId");
    }
}
