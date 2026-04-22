namespace Api.Services;

public static class TenantLeaveLookupQueryContract
{
    public const string TenantIdParameterName = "tenantId";
    public const string UserIdParameterName = "userId";

    public static string BuildSelectOwnerUserIdByTenantIdSql()
    {
        return @"
            SELECT owner_user_id FROM tenants WHERE id = @tenantId
        ";
    }

    public static string BuildSelectActiveAdminCountByTenantIdSql()
    {
        return @"
            SELECT COUNT(*) FROM tenant_memberships
            WHERE tenant_id = @tenantId AND role = 'admin' AND status = 'active'
        ";
    }

    public static string BuildSelectActiveRoleByTenantAndUserSql()
    {
        return @"
            SELECT role FROM tenant_memberships
            WHERE tenant_id = @tenantId AND user_id = @userId AND status = 'active'
        ";
    }
}
