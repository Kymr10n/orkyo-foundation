using Api.Helpers;

namespace Api.Services;

/// <summary>
/// SQL contract for listing users that are members of a specific tenant.
///
/// Joins control-plane <c>users</c> with <c>tenant_memberships</c> filtered by
/// tenant id; column projection delegates to
/// <see cref="UserHelper.UserSelectColumns"/> so reader-row mapping (also via
/// <see cref="UserHelper.MapUser"/>) stays synchronized with the SELECT shape.
/// Ordered by <c>u.created_at DESC</c>.
/// </summary>
public static class TenantUserListQueryContract
{
    public const string TenantIdParameterName = "tenantId";

    public static string BuildListUsersByTenantSql()
    {
        return $@"
            SELECT {UserHelper.UserSelectColumns}
            FROM users u
            INNER JOIN tenant_memberships tm ON u.id = tm.user_id AND tm.tenant_id = @tenantId
            ORDER BY u.created_at DESC
        ";
    }
}
