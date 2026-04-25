namespace Api.Services;

public static class TenantReactivationLookupQueryContract
{
    public const string TenantIdParameterName = "tenantId";
    public const string UserIdParameterName = "userId";

    public static string BuildSelectByTenantAndUserSql()
    {
        return @"
            SELECT t.status, t.suspension_reason, tm.role, t.owner_user_id
            FROM tenants t
            JOIN tenant_memberships tm ON tm.tenant_id = t.id
            WHERE t.id = @tenantId
              AND tm.user_id = @userId
              AND tm.status = 'active'
        ";
    }
}
