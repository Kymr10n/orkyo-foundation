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
    public void BuildTouchLastActivitySql_ShouldContainExpectedUpdate()
    {
        TenantMutationQueryContract.BuildTouchLastActivitySql().Should().Contain("SET last_activity_at = NOW()");
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

    [Fact]
    public void BuildReactivateSuspendedTenantSql_ShouldContainExpectedUpdateAndGuard()
    {
        var sql = TenantMutationQueryContract.BuildReactivateSuspendedTenantSql();
        sql.Should().Contain("SET status = 'active'");
        sql.Should().Contain("suspended_at = NULL");
        sql.Should().Contain("suspension_reason = NULL");
        sql.Should().Contain("last_activity_at = NOW()");
        sql.Should().Contain("WHERE id = @tenantId AND status = 'suspended'");
    }
}
