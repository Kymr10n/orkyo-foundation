namespace Api.Services;

public static class TenantMembershipListQueryContract
{
    public const string UserIdParameterName = "userId";

    public static string BuildSelectByUserSql()
    {
        return @"
            SELECT
                t.id, t.slug, t.display_name, t.status, t.owner_user_id,
                tm.role, tm.status, tm.created_at
            FROM tenant_memberships tm
            JOIN tenants t ON t.id = tm.tenant_id
            WHERE tm.user_id = @userId
            ORDER BY tm.created_at DESC
        ";
    }
}
