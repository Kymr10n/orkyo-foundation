using Api.Constants;
using Api.Helpers;
using Api.Models;
using Npgsql;

namespace Api.Services;

public interface IUserManagementService
{
    Task<List<User>> GetAllUsersAsync(OrgContext org);
    Task<(bool success, string? error)> UpdateUserRoleAsync(OrgContext org, Guid userId, UserRole role, Guid updatedBy);
    Task<(bool success, string? error)> DeleteUserAsync(OrgContext org, Guid userId, Guid deletedBy);
    Task EnsureInitialAdminAsync(OrgContext org, string adminEmail);

    /// <summary>Updates users.status globally (e.g. 'active', 'disabled').</summary>
    Task SetGlobalStatusAsync(Guid userId, string status);

    /// <summary>Hard-deletes the user row; cascade removes memberships and identities.</summary>
    Task PermanentlyDeleteAsync(Guid userId);
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
    public async Task<List<User>> GetAllUsersAsync(OrgContext org)
    {
        await using var conn = _connectionFactory.CreateControlPlaneConnection();
        await conn.OpenAsync();

        await using var cmd = TenantUserListCommandFactory.CreateListUsersByTenantCommand(conn, org.OrgId);
        await using var reader = await cmd.ExecuteReaderAsync();
        return await TenantUserListReaderFlow.ReadUsersAsync(reader);
    }

    /// <summary>
    /// Update a user's role within a specific tenant.
    /// Prevents demoting the last active admin.
    /// </summary>
    public async Task<(bool success, string? error)> UpdateUserRoleAsync(OrgContext org, Guid userId, UserRole role, Guid updatedBy)
    {
        await using var conn = _connectionFactory.CreateControlPlaneConnection();
        await conn.OpenAsync();

        // If demoting from admin, check if this is the last admin
        if (role != UserRole.Admin)
        {
            var isLastAdmin = await IsLastActiveAdminAsync(conn, org.OrgId, userId);
            if (isLastAdmin)
            {
                _logger.LogWarning("Cannot demote user {UserId}: they are the last active admin in tenant {TenantId}",
                    userId, org.OrgId);
                return (false, "Cannot demote the last admin. Promote another user to admin first.");
            }
        }

        await using var cmd = TenantMembershipMutationCommandFactory.CreateUpdateMembershipRoleCommand(
            conn, org.OrgId, userId, role.ToString().ToLowerInvariant());

        var rowsAffected = await cmd.ExecuteNonQueryAsync();

        if (rowsAffected > 0)
        {
            await _tenantUserService.RecordAuditEventAsync(org, "user.role_updated", updatedBy,
                "user", userId.ToString(), new { newRole = role.ToString() });
        }

        return (rowsAffected > 0, null);
    }

    /// <summary>
    /// Remove a user from a tenant (deletes membership, not the user).
    /// Prevents removing the last active admin.
    /// </summary>
    public async Task<(bool success, string? error)> DeleteUserAsync(OrgContext org, Guid userId, Guid deletedBy)
    {
        await using var conn = _connectionFactory.CreateControlPlaneConnection();
        await conn.OpenAsync();

        // Check if this user is the last admin
        var isLastAdmin = await IsLastActiveAdminAsync(conn, org.OrgId, userId);
        if (isLastAdmin)
        {
            _logger.LogWarning("Cannot remove user {UserId}: they are the last active admin in tenant {TenantId}",
                userId, org.OrgId);
            return (false, "Cannot remove the last admin. Promote another user to admin first.");
        }

        // Delete membership (not the user - they may belong to other tenants)
        await using var cmd = TenantMembershipMutationCommandFactory.CreateDeleteMembershipCommand(
            conn, org.OrgId, userId);

        var rowsAffected = await cmd.ExecuteNonQueryAsync();

        if (rowsAffected > 0)
        {
            await _tenantUserService.RecordAuditEventAsync(org, "user.removed_from_tenant", deletedBy,
                "user", userId.ToString());
        }

        return (rowsAffected > 0, null);
    }

    /// <summary>
    /// Ensure an initial admin exists for a tenant. Creates user and membership if needed.
    /// User creation is handled by Keycloak; this only ensures the membership record exists
    /// for a user who must already have authenticated via Keycloak.
    /// </summary>
    public async Task EnsureInitialAdminAsync(OrgContext org, string adminEmail)
    {
        await using var conn = _connectionFactory.CreateControlPlaneConnection();
        await conn.OpenAsync();

        // Check if user exists (they must have logged in via Keycloak first)
        await using var checkCmd = UserLookupByEmailCommandFactory.CreateSelectUserIdByEmailCommand(conn, adminEmail);
        var existingUserId = UserLookupByEmailScalarFlow.ReadUserId(await checkCmd.ExecuteScalarAsync());

        if (!existingUserId.HasValue)
        {
            _logger.LogWarning("Initial admin {Email} not found — they must log in via Keycloak first", adminEmail);
            return;
        }

        var userId = existingUserId.Value;

        // Ensure tenant membership exists with admin role
        await using var membershipCmd = TenantMembershipMutationCommandFactory.CreateUpsertAdminMembershipCommand(
            conn, org.OrgId, userId);
        await membershipCmd.ExecuteNonQueryAsync();

        _logger.LogInformation("Ensured admin membership for user {Email} in tenant {TenantId}",
            adminEmail, org.OrgId);

        // Sync user to tenant database
        await _tenantUserService.CreateUserStubInTenantDatabaseAsync(org, userId, adminEmail);
    }

    public async Task SetGlobalStatusAsync(Guid userId, string status)
    {
        await using var conn = _connectionFactory.CreateControlPlaneConnection();
        await conn.OpenAsync();

        await using var cmd = ControlPlaneUserMutationCommandFactory.CreateSetUserStatusCommand(conn, userId, status);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task PermanentlyDeleteAsync(Guid userId)
    {
        await using var conn = _connectionFactory.CreateControlPlaneConnection();
        await conn.OpenAsync();

        await using var cmd = ControlPlaneUserMutationCommandFactory.CreateDeleteUserCommand(conn, userId);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Check if the specified user is the last active admin in the tenant.
    /// Used to prevent operations that would leave a tenant without an admin.
    ///
    /// Composes shared <see cref="TenantLeaveLookupCommandFactory"/> for the role +
    /// total-active-admin-count lookups and shared <see cref="LastActiveAdminPolicy"/>
    /// for the pure decision.
    /// </summary>
    private static async Task<bool> IsLastActiveAdminAsync(NpgsqlConnection conn, Guid tenantId, Guid userId)
    {
        await using var roleCmd = TenantLeaveLookupCommandFactory.CreateSelectActiveRoleByTenantAndUserCommand(
            conn, tenantId, userId);
        var currentRole = TenantLeaveLookupScalarFlow.ReadActiveRole(await roleCmd.ExecuteScalarAsync());

        if (!string.Equals(currentRole, RoleConstants.Admin, StringComparison.OrdinalIgnoreCase))
            return false;

        await using var countCmd = TenantLeaveLookupCommandFactory.CreateSelectActiveAdminCountByTenantIdCommand(
            conn, tenantId);
        var totalActiveAdminCount = TenantLeaveLookupScalarFlow.ReadActiveAdminCount(
            await countCmd.ExecuteScalarAsync());

        return LastActiveAdminPolicy.IsLastActiveAdmin(currentRole, totalActiveAdminCount);
    }
}
