namespace Api.Services;

/// <summary>
/// SQL contract for direct mutations on the control-plane <c>users</c> table.
///
/// Covers the operations that are purely platform-level user-record state
/// management (global status flag, hard delete) and apply identically in
/// multi-tenant SaaS and single-tenant Community deployments. Per-tenant
/// membership mutations live in
/// <see cref="TenantMembershipMutationQueryContract"/>.
/// </summary>
public static class ControlPlaneUserMutationQueryContract
{
    public const string UserIdParameterName = "userId";
    public const string StatusParameterName = "status";

    /// <summary>
    /// SQL: <c>UPDATE users SET status = @status, updated_at = NOW() WHERE id = @userId</c>.
    /// </summary>
    public static string BuildSetUserStatusSql()
    {
        return @"
            UPDATE users
            SET status = @status,
                updated_at = NOW()
            WHERE id = @userId
        ";
    }

    /// <summary>
    /// SQL: <c>DELETE FROM users WHERE id = @userId</c>.
    /// Cascade rules in the schema remove memberships and identity links.
    /// </summary>
    public static string BuildDeleteUserSql()
    {
        return @"
            DELETE FROM users
            WHERE id = @userId
        ";
    }
}
