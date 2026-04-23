namespace Api.Services;

/// <summary>
/// SQL contracts for tenant_memberships mutations driven by user-management
/// operations: updating a member's role, removing a membership, and ensuring
/// an admin membership exists for an initial admin.
///
/// Parameter names are exposed so command factories and callers stay aligned.
/// </summary>
public static class TenantMembershipMutationQueryContract
{
    public const string TenantIdParameterName = "tenantId";
    public const string UserIdParameterName = "userId";
    public const string RoleParameterName = "role";

    /// <summary>
    /// Update the membership role of <c>@userId</c> in <c>@tenantId</c> to <c>@role</c>.
    /// Touches <c>updated_at</c>.
    /// </summary>
    public static string BuildUpdateMembershipRoleSql()
    {
        return @"
            UPDATE tenant_memberships
            SET role = @role, updated_at = NOW()
            WHERE user_id = @userId AND tenant_id = @tenantId
        ";
    }

    /// <summary>
    /// Delete the membership of <c>@userId</c> in <c>@tenantId</c> (does not delete the user).
    /// </summary>
    public static string BuildDeleteMembershipSql()
    {
        return @"
            DELETE FROM tenant_memberships
            WHERE user_id = @userId AND tenant_id = @tenantId
        ";
    }

    /// <summary>
    /// Ensure an active admin membership exists for <c>@userId</c> in <c>@tenantId</c>.
    /// On conflict, force role back to admin and touch <c>updated_at</c>.
    /// </summary>
    public static string BuildUpsertAdminMembershipSql()
    {
        return @"
            INSERT INTO tenant_memberships (user_id, tenant_id, role, status, created_at, updated_at)
            VALUES (@userId, @tenantId, 'admin', 'active', NOW(), NOW())
            ON CONFLICT (user_id, tenant_id)
            DO UPDATE SET role = 'admin', updated_at = NOW()
        ";
    }
}
