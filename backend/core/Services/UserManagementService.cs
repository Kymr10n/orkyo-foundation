using Api.Constants;
using Api.Helpers;
using Api.Models;
using Api.Repositories;
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

    /// <summary>
    /// Erases the user everywhere: their rows in every tenant database they belong to, then the
    /// control-plane row (cascade removes memberships and identities). See <see cref="UserDataPurger"/>.
    /// </summary>
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
    private readonly IdentityCacheEvictor? _identityCacheEvictor;

    /// <param name="identityCacheEvictor">
    /// Evicts the role the request pipeline caches per user and tenant, in every process. Optional only
    /// because orkyo-saas's <c>UserManagementServiceIntegrationTests</c> composes this service by
    /// hand; DI always supplies it.
    /// </param>
    public UserManagementService(
        IDbConnectionFactory connectionFactory,
        ITenantUserService tenantUserService,
        ILogger<UserManagementService> logger,
        IdentityCacheEvictor? identityCacheEvictor = null)
    {
        _connectionFactory = connectionFactory;
        _tenantUserService = tenantUserService;
        _logger = logger;
        _identityCacheEvictor = identityCacheEvictor;
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

        var outcome = await ActiveAdminGuard.SetRoleAsync(conn, org.OrgId, userId, role.ToString().ToLowerInvariant(), ct);

        // A demoted member must not keep the old role for the rest of the cache TTL, in any process.
        _identityCacheEvictor?.EvictRole(userId, org.OrgId);

        if (outcome == GuardedMembershipWrite.LastActiveAdmin)
        {
            _logger.LogWarning("Cannot demote user {UserId}: they are the last active admin in tenant {TenantId}", userId, org.OrgId);
            return Result.Fail(ActiveAdminGuard.DemotionRefused);
        }

        if (outcome == GuardedMembershipWrite.Written)
            await _tenantUserService.RecordAuditEventAsync(org, TenantAuditActions.UserRoleUpdated, updatedBy, "user", userId.ToString(), new { newRole = role.ToString() }, ct);

        return new Result(outcome == GuardedMembershipWrite.Written, null);
    }

    /// <summary>
    /// Remove a user from a tenant (deletes membership, not the user).
    /// Prevents removing the last active admin.
    /// </summary>
    public async Task<Result> DeleteUserAsync(OrgContext org, Guid userId, Guid deletedBy, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateControlPlaneConnection();
        await conn.OpenAsync(ct);

        var outcome = await ActiveAdminGuard.DeleteAsync(conn, org.OrgId, userId, ct);

        // A removed member must lose access now, not when the cached role expires.
        _identityCacheEvictor?.EvictRole(userId, org.OrgId);

        if (outcome == GuardedMembershipWrite.LastActiveAdmin)
        {
            _logger.LogWarning("Cannot remove user {UserId}: they are the last active admin in tenant {TenantId}", userId, org.OrgId);
            return Result.Fail(ActiveAdminGuard.RemovalRefused);
        }

        if (outcome == GuardedMembershipWrite.Written)
            await _tenantUserService.RecordAuditEventAsync(org, TenantAuditActions.UserRemovedFromTenant, deletedBy, "user", userId.ToString(), ct: ct);

        return new Result(outcome == GuardedMembershipWrite.Written, null);
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

        // The cached role counts only non-disabled users, and the cached principal is read
        // before any status check, so a status change invalidates both: the role in every
        // tenant the user belongs to and the principal of every linked subject.
        if (_identityCacheEvictor is not null)
        {
            foreach (var tenantId in await IdentityEvictionSql.TenantIdsOfUserAsync(conn, userId, ct))
                _identityCacheEvictor.EvictRole(userId, tenantId);
            foreach (var subject in await IdentityEvictionSql.ExternalSubjectsOfUserAsync(conn, userId, ct))
                _identityCacheEvictor.EvictPrincipal(subject);
        }
    }

    public Task PermanentlyDeleteAsync(Guid userId, CancellationToken ct = default) =>
        new UserDataPurger(_connectionFactory, _logger).PurgeAsync(userId, ct);

}
