using Api.Constants;
using Api.Helpers;
using Api.Models;
using Api.Services.Caching;
using Npgsql;
using Orkyo.Shared;

namespace Api.Services;

public interface IUserManagementService
{
    Task<List<User>> GetAllUsersAsync(OrgContext org, CancellationToken ct = default);
    Task<Result> UpdateUserRoleAsync(OrgContext org, Guid userId, UserRole role, Guid updatedBy, CancellationToken ct = default);
    Task<Result> DeleteUserAsync(OrgContext org, Guid userId, Guid deletedBy, CancellationToken ct = default);

    /// <summary>Updates users.status globally. Accepts only <see cref="UserStatusConstants"/> values.</summary>
    Task SetGlobalStatusAsync(Guid userId, string status, CancellationToken ct = default);

    /// <summary>Hard-deletes the user row; cascade removes memberships and identities.</summary>
    Task PermanentlyDeleteAsync(Guid userId, CancellationToken ct = default);
}

/// <summary>
/// User management service that uses centralized tenant_memberships table.
///
/// Architecture:
/// - control_plane.users: Global user identity (auth handled by Keycloak)
/// - control_plane.tenant_memberships: User-tenant associations with per-tenant role
/// - tenant_X.users: Minimal stubs for FK references (created on-demand by TenantUserService)
/// </summary>
public class UserManagementService : IUserManagementService
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ITenantUserService _tenantUserService;
    private readonly ILogger<UserManagementService> _logger;
    private readonly SingleFlightCache? _identityCache;

    /// <param name="identityCache">
    /// The shared cache the request pipeline keeps each user's tenant role in. Optional so a
    /// caller that composes this service by hand keeps compiling; DI always supplies it.
    /// </param>
    public UserManagementService(
        IDbConnectionFactory connectionFactory,
        ITenantUserService tenantUserService,
        ILogger<UserManagementService> logger,
        SingleFlightCache? identityCache = null)
    {
        _connectionFactory = connectionFactory;
        _tenantUserService = tenantUserService;
        _logger = logger;
        _identityCache = identityCache;
    }

    /// <summary>
    /// Get all users who are members of the specified tenant.
    /// Joins users with tenant_memberships to get per-tenant role and status.
    /// </summary>
    public async Task<List<User>> GetAllUsersAsync(OrgContext org, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateControlPlaneConnection();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($@"
            SELECT {UserHelper.UserSelectColumns}
            FROM users u
            INNER JOIN tenant_memberships tm ON u.id = tm.user_id AND tm.tenant_id = @tenantId
            ORDER BY u.created_at DESC", conn);
        cmd.Parameters.AddWithValue("tenantId", org.OrgId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var users = new List<User>();
        while (await reader.ReadAsync(ct))
            users.Add(UserHelper.MapUser(reader));
        return users;
    }

    /// <summary>
    /// Update a user's role within a specific tenant.
    /// Prevents demoting the last active admin.
    /// </summary>
    public async Task<Result> UpdateUserRoleAsync(OrgContext org, Guid userId, UserRole role, Guid updatedBy, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateControlPlaneConnection();
        await conn.OpenAsync(ct);

        // One statement: the guard and the write cannot be split by a concurrent demotion.
        await using var cmd = new NpgsqlCommand($@"
            {LockActiveAdminsCte}
            UPDATE tenant_memberships
            SET role = @role, updated_at = NOW()
            WHERE user_id = @userId AND tenant_id = @tenantId
              AND ({KeepsAnotherActiveAdminSql})", conn);
        cmd.Parameters.AddWithValue("role", role.ToString().ToLowerInvariant());
        AddGuardParameters(cmd, org.OrgId, userId);

        var rowsAffected = await cmd.ExecuteNonQueryAsync(ct);

        // A demoted member must not keep the old role for the rest of the cache TTL.
        _identityCache?.Remove(IdentityCacheKeys.Role(userId, org.OrgId));

        if (rowsAffected == 0 && await MembershipExistsAsync(conn, org.OrgId, userId, ct))
        {
            _logger.LogWarning("Cannot demote user {UserId}: they are the last active admin in tenant {TenantId}", userId, org.OrgId);
            return Result.Fail("Cannot demote the last admin. Promote another user to admin first.");
        }

        if (rowsAffected > 0)
            await _tenantUserService.RecordAuditEventAsync(org, TenantAuditActions.UserRoleUpdated, updatedBy, "user", userId.ToString(), new { newRole = role.ToString() }, ct);

        return new Result(rowsAffected > 0, null);
    }

    /// <summary>
    /// Remove a user from a tenant (deletes membership, not the user).
    /// Prevents removing the last active admin.
    /// </summary>
    public async Task<Result> DeleteUserAsync(OrgContext org, Guid userId, Guid deletedBy, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateControlPlaneConnection();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($@"
            {LockActiveAdminsCte}
            DELETE FROM tenant_memberships
            WHERE user_id = @userId AND tenant_id = @tenantId
              AND ({DeleteKeepsAnotherActiveAdminSql})", conn);
        AddGuardParameters(cmd, org.OrgId, userId);

        var rowsAffected = await cmd.ExecuteNonQueryAsync(ct);

        // A removed member must lose access now, not when the cached role expires.
        _identityCache?.Remove(IdentityCacheKeys.Role(userId, org.OrgId));

        if (rowsAffected == 0 && await MembershipExistsAsync(conn, org.OrgId, userId, ct))
        {
            _logger.LogWarning("Cannot remove user {UserId}: they are the last active admin in tenant {TenantId}", userId, org.OrgId);
            return Result.Fail("Cannot remove the last admin. Promote another user to admin first.");
        }

        if (rowsAffected > 0)
            await _tenantUserService.RecordAuditEventAsync(org, TenantAuditActions.UserRemovedFromTenant, deletedBy, "user", userId.ToString(), ct: ct);

        return new Result(rowsAffected > 0, null);
    }

    public async Task SetGlobalStatusAsync(Guid userId, string status, CancellationToken ct = default)
    {
        if (!UserStatusConstants.All.Contains(status))
            throw new ArgumentException($"Unknown user status: {status}", nameof(status));

        await using var conn = _connectionFactory.CreateControlPlaneConnection();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(@"
            UPDATE users SET status = @status, updated_at = NOW() WHERE id = @userId", conn);
        cmd.Parameters.AddWithValue("status", status);
        cmd.Parameters.AddWithValue("userId", userId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task PermanentlyDeleteAsync(Guid userId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateControlPlaneConnection();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand("DELETE FROM users WHERE id = @userId", conn);
        cmd.Parameters.AddWithValue("userId", userId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Locks the tenant's active-admin rows in a fixed order. Two admins demoting each other
    /// at once serialize here: the second waits, re-reads the first's committed row, no longer
    /// counts it as an admin and so refuses. Without the lock each statement sees the other
    /// admin in its snapshot and both succeed.
    /// </summary>
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

    private static void AddGuardParameters(NpgsqlCommand cmd, Guid tenantId, Guid userId)
    {
        cmd.Parameters.AddWithValue("userId", userId);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("adminRole", RoleConstants.Admin);
        cmd.Parameters.AddWithValue("activeStatus", MembershipStatusConstants.Active);
    }

    private static async Task<bool> MembershipExistsAsync(NpgsqlConnection conn, Guid tenantId, Guid userId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT EXISTS (SELECT 1 FROM tenant_memberships WHERE tenant_id = @tenantId AND user_id = @userId)", conn);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("userId", userId);
        return (bool)(await cmd.ExecuteScalarAsync(ct))!;
    }
}
