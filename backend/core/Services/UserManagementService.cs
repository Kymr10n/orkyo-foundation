using Api.Constants;
using Api.Helpers;
using Api.Models;
using Npgsql;

namespace Api.Services;

public interface IUserManagementService
{
    Task<List<User>> GetAllUsersAsync(OrgContext org, CancellationToken ct = default);
    Task<(bool success, string? error)> UpdateUserRoleAsync(OrgContext org, Guid userId, UserRole role, Guid updatedBy, CancellationToken ct = default);
    Task<(bool success, string? error)> DeleteUserAsync(OrgContext org, Guid userId, Guid deletedBy, CancellationToken ct = default);
    Task EnsureInitialAdminAsync(OrgContext org, string adminEmail, CancellationToken ct = default);

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

    public UserManagementService(
        IDbConnectionFactory connectionFactory,
        ITenantUserService tenantUserService,
        ILogger<UserManagementService> logger)
    {
        _connectionFactory = connectionFactory;
        _tenantUserService = tenantUserService;
        _logger = logger;
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
    public async Task<(bool success, string? error)> UpdateUserRoleAsync(OrgContext org, Guid userId, UserRole role, Guid updatedBy, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateControlPlaneConnection();
        await conn.OpenAsync(ct);

        if (role != UserRole.Admin)
        {
            var isLastAdmin = await IsLastActiveAdminAsync(conn, org.OrgId, userId, ct);
            if (isLastAdmin)
            {
                _logger.LogWarning("Cannot demote user {UserId}: they are the last active admin in tenant {TenantId}", userId, org.OrgId);
                return (false, "Cannot demote the last admin. Promote another user to admin first.");
            }
        }

        await using var cmd = new NpgsqlCommand(@"
            UPDATE tenant_memberships
            SET role = @role, updated_at = NOW()
            WHERE user_id = @userId AND tenant_id = @tenantId", conn);
        cmd.Parameters.AddWithValue("role", role.ToString().ToLowerInvariant());
        cmd.Parameters.AddWithValue("userId", userId);
        cmd.Parameters.AddWithValue("tenantId", org.OrgId);

        var rowsAffected = await cmd.ExecuteNonQueryAsync(ct);

        if (rowsAffected > 0)
            await _tenantUserService.RecordAuditEventAsync(org, TenantAuditActions.UserRoleUpdated, updatedBy, "user", userId.ToString(), new { newRole = role.ToString() });

        return (rowsAffected > 0, null);
    }

    /// <summary>
    /// Remove a user from a tenant (deletes membership, not the user).
    /// Prevents removing the last active admin.
    /// </summary>
    public async Task<(bool success, string? error)> DeleteUserAsync(OrgContext org, Guid userId, Guid deletedBy, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateControlPlaneConnection();
        await conn.OpenAsync(ct);

        var isLastAdmin = await IsLastActiveAdminAsync(conn, org.OrgId, userId, ct);
        if (isLastAdmin)
        {
            _logger.LogWarning("Cannot remove user {UserId}: they are the last active admin in tenant {TenantId}", userId, org.OrgId);
            return (false, "Cannot remove the last admin. Promote another user to admin first.");
        }

        await using var cmd = new NpgsqlCommand(@"
            DELETE FROM tenant_memberships
            WHERE user_id = @userId AND tenant_id = @tenantId", conn);
        cmd.Parameters.AddWithValue("userId", userId);
        cmd.Parameters.AddWithValue("tenantId", org.OrgId);

        var rowsAffected = await cmd.ExecuteNonQueryAsync(ct);

        if (rowsAffected > 0)
            await _tenantUserService.RecordAuditEventAsync(org, TenantAuditActions.UserRemovedFromTenant, deletedBy, "user", userId.ToString());

        return (rowsAffected > 0, null);
    }

    /// <summary>
    /// Ensure an initial admin exists for a tenant. Creates user and membership if needed.
    /// </summary>
    public async Task EnsureInitialAdminAsync(OrgContext org, string adminEmail, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateControlPlaneConnection();
        await conn.OpenAsync(ct);

        await using var checkCmd = new NpgsqlCommand("SELECT id FROM users WHERE email = @email", conn);
        checkCmd.Parameters.AddWithValue("email", adminEmail);
        var existingUserId = (await checkCmd.ExecuteScalarAsync(ct)) is Guid id ? id : (Guid?)null;

        if (!existingUserId.HasValue)
        {
            _logger.LogWarning("Initial admin {Email} not found — they must log in via Keycloak first", adminEmail);
            return;
        }

        var userId = existingUserId.Value;

        await using var membershipCmd = new NpgsqlCommand(@"
            INSERT INTO tenant_memberships (user_id, tenant_id, role, status, created_at, updated_at)
            VALUES (@userId, @tenantId, 'admin', 'active', NOW(), NOW())
            ON CONFLICT (user_id, tenant_id)
            DO UPDATE SET role = 'admin', updated_at = NOW()", conn);
        membershipCmd.Parameters.AddWithValue("userId", userId);
        membershipCmd.Parameters.AddWithValue("tenantId", org.OrgId);
        await membershipCmd.ExecuteNonQueryAsync(ct);

        _logger.LogInformation("Ensured admin membership for user {Email} in tenant {TenantId}", adminEmail, org.OrgId);

        await _tenantUserService.CreateUserStubInTenantDatabaseAsync(org, userId, adminEmail);
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

    private static async Task<bool> IsLastActiveAdminAsync(NpgsqlConnection conn, Guid tenantId, Guid userId, CancellationToken ct = default)
    {
        await using var roleCmd = new NpgsqlCommand(@"
            SELECT role FROM tenant_memberships
            WHERE tenant_id = @tenantId AND user_id = @userId AND status = 'active'", conn);
        roleCmd.Parameters.AddWithValue("tenantId", tenantId);
        roleCmd.Parameters.AddWithValue("userId", userId);
        var currentRole = (await roleCmd.ExecuteScalarAsync(ct)) as string;

        if (!string.Equals(currentRole, RoleConstants.Admin, StringComparison.OrdinalIgnoreCase))
            return false;

        await using var countCmd = new NpgsqlCommand(@"
            SELECT COUNT(*) FROM tenant_memberships
            WHERE tenant_id = @tenantId AND role = 'admin' AND status = 'active'", conn);
        countCmd.Parameters.AddWithValue("tenantId", tenantId);
        var count = Convert.ToInt64(await countCmd.ExecuteScalarAsync(ct) ?? 0L);

        return LastActiveAdminPolicy.IsLastActiveAdmin(currentRole, count);
    }
}
