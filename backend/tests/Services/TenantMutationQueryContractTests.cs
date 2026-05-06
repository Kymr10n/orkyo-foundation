using Api.Services;

namespace Orkyo.Foundation.Tests.Services;

public class TenantMutationQueryContractTests
{
    [Fact]
    public void BuildMarkDeletingSql_ShouldContainExpectedUpdate()
    {
        TenantMutationQueryContract.BuildMarkDeletingSql().Should().Contain("SET status = 'deleting'");
    }

    [Fact]
    public void BuildMarkActiveSql_ShouldContainExpectedUpdate()
    {
        TenantMutationQueryContract.BuildMarkActiveSql().Should().Contain("SET status = 'active'");
    }

    [Fact]
    public void BuildUpdateDisplayNameSql_ShouldContainExpectedUpdate()
    {
        var sql = TenantMutationQueryContract.BuildUpdateDisplayNameSql();
        sql.Should().Contain("SET display_name = @displayName");
        sql.Should().Contain("WHERE id = @tenantId");
    }

    [Fact]
    public void BuildTransferOwnershipSql_ShouldContainExpectedUpdate()
    {
        var sql = TenantMutationQueryContract.BuildTransferOwnershipSql();
        sql.Should().Contain("SET owner_user_id = @newOwnerId");
        sql.Should().Contain("WHERE id = @tenantId");
    }

    [Fact]
    public void BuildDeleteMembershipSql_ShouldContainExpectedDelete()
    {
        var sql = TenantMutationQueryContract.BuildDeleteMembershipSql();
        sql.Should().Contain("DELETE FROM tenant_memberships");
        sql.Should().Contain("WHERE tenant_id = @tenantId AND user_id = @userId");
    }

}
