using Api.Services;

namespace Orkyo.Foundation.Tests.Services;

public class TenantMembershipMutationQueryContractTests
{
    [Fact]
    public void BuildUpdateMembershipRoleSql_TargetsTenantMembershipsAndScopesByUserAndTenant()
    {
        var sql = TenantMembershipMutationQueryContract.BuildUpdateMembershipRoleSql();

        sql.Should().Contain("UPDATE tenant_memberships");
        sql.Should().Contain("SET role = @role");
        sql.Should().Contain("updated_at = NOW()");
        sql.Should().Contain("WHERE user_id = @userId AND tenant_id = @tenantId");
    }

    [Fact]
    public void BuildDeleteMembershipSql_DeletesByUserAndTenant()
    {
        var sql = TenantMembershipMutationQueryContract.BuildDeleteMembershipSql();

        sql.Should().Contain("DELETE FROM tenant_memberships");
        sql.Should().Contain("WHERE user_id = @userId AND tenant_id = @tenantId");
    }

    [Fact]
    public void BuildUpsertAdminMembershipSql_InsertsActiveAdminAndOnConflictResetsRole()
    {
        var sql = TenantMembershipMutationQueryContract.BuildUpsertAdminMembershipSql();

        sql.Should().Contain("INSERT INTO tenant_memberships");
        sql.Should().Contain("VALUES (@userId, @tenantId, 'admin', 'active'");
        sql.Should().Contain("ON CONFLICT (user_id, tenant_id)");
        sql.Should().Contain("DO UPDATE SET role = 'admin'");
        sql.Should().Contain("updated_at = NOW()");
    }

    [Fact]
    public void ParameterNames_AreStable()
    {
        TenantMembershipMutationQueryContract.TenantIdParameterName.Should().Be("tenantId");
        TenantMembershipMutationQueryContract.UserIdParameterName.Should().Be("userId");
        TenantMembershipMutationQueryContract.RoleParameterName.Should().Be("role");
    }
}
