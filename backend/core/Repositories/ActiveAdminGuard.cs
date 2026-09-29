using Api.Constants;
using Npgsql;

namespace Api.Repositories;

/// <summary>What a guarded membership write did.</summary>
internal enum GuardedMembershipWrite
{
    /// <summary>The row was written.</summary>
    Written,
    /// <summary>No membership row for that user in that tenant.</summary>
    NoMembership,
    /// <summary>The row exists but the write would have left the tenant without an active admin.</summary>
    LastActiveAdmin,
}

/// <summary>
/// The last-active-admin rule as the one locking statement every membership write goes
/// through: an admin's own removal (<c>UserManagementService</c>), a member leaving
/// (<c>TenantControlPlaneRepository.DeleteMembershipAsync</c>) and a demotion. Two admins
/// removing each other at once serialize on the locked rows: the second waits, re-reads the
/// first's committed row, no longer counts it as an admin and so refuses. Reading the count
/// first and acting on it cannot do that — each statement sees the other admin in its
/// snapshot and both succeed.
/// </summary>
internal static class ActiveAdminGuard
{
    internal const string DemotionRefused = "Cannot demote the last admin. Promote another user to admin first.";
    internal const string RemovalRefused = "Cannot remove the last admin. Promote another user to admin first.";

    /// <summary>Locks the tenant's active-admin rows in a fixed order, so concurrent guards serialize.</summary>
    private const string LockActiveAdminsCte = @"
            WITH active_admins AS (
                SELECT user_id FROM tenant_memberships
                WHERE tenant_id = @tenantId AND role = @adminRole AND status = @activeStatus
                ORDER BY user_id
                FOR UPDATE)";

    private const string OtherActiveAdminExistsSql =
        "EXISTS (SELECT 1 FROM active_admins WHERE user_id <> @userId)";

    /// <summary>A role change keeps an admin when the target is not one, stays one, or another exists.</summary>
    private const string KeepsAnotherActiveAdminSql =
        "NOT (role = @adminRole AND status = @activeStatus) OR @role = @adminRole OR " + OtherActiveAdminExistsSql;

    private const string DeleteKeepsAnotherActiveAdminSql =
        "NOT (role = @adminRole AND status = @activeStatus) OR " + OtherActiveAdminExistsSql;

    /// <summary>Sets the member's role unless that would demote the last active admin.</summary>
    internal static Task<GuardedMembershipWrite> SetRoleAsync(
        NpgsqlConnection conn, Guid tenantId, Guid userId, string role, CancellationToken ct)
        => WriteAsync(conn, $@"
            {LockActiveAdminsCte}
            UPDATE tenant_memberships
            SET role = @role, updated_at = NOW()
            WHERE user_id = @userId AND tenant_id = @tenantId
              AND ({KeepsAnotherActiveAdminSql})",
            tenantId, userId, p => p.AddWithValue("role", role), ct);

    /// <summary>Deletes the membership row unless that would remove the last active admin.</summary>
    internal static Task<GuardedMembershipWrite> DeleteAsync(
        NpgsqlConnection conn, Guid tenantId, Guid userId, CancellationToken ct)
        => WriteAsync(conn, $@"
            {LockActiveAdminsCte}
            DELETE FROM tenant_memberships
            WHERE user_id = @userId AND tenant_id = @tenantId
              AND ({DeleteKeepsAnotherActiveAdminSql})",
            tenantId, userId, null, ct);

    private static async Task<GuardedMembershipWrite> WriteAsync(
        NpgsqlConnection conn, string sql, Guid tenantId, Guid userId,
        Action<NpgsqlParameterCollection>? bind, CancellationToken ct)
    {
        // One statement: the guard and the write cannot be split by a concurrent write.
        var rowsAffected = await conn.ExecuteAsync(sql, p =>
        {
            p.AddWithValue("userId", userId);
            p.AddWithValue("tenantId", tenantId);
            p.AddWithValue("adminRole", RoleConstants.Admin);
            p.AddWithValue("activeStatus", MembershipStatusConstants.Active);
            bind?.Invoke(p);
        }, ct);
        if (rowsAffected > 0)
            return GuardedMembershipWrite.Written;

        var exists = await conn.ExecuteScalarAsync<bool>(
            "SELECT EXISTS (SELECT 1 FROM tenant_memberships WHERE tenant_id = @tenantId AND user_id = @userId)",
            p =>
            {
                p.AddWithValue("tenantId", tenantId);
                p.AddWithValue("userId", userId);
            }, ct);
        return exists ? GuardedMembershipWrite.LastActiveAdmin : GuardedMembershipWrite.NoMembership;
    }
}
