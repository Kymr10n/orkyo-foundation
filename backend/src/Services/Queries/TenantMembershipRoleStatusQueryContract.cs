namespace Api.Services;

public static class TenantMembershipRoleStatusQueryContract
{
    public const string TenantIdParameterName = "tenantId";
    public const string UserIdParameterName = "userId";

    public static string BuildSelectByTenantAndUserSql()
    {
        return @"
            SELECT role, status FROM tenant_memberships
            WHERE tenant_id = @tenantId AND user_id = @userId
        ";
    }
}
