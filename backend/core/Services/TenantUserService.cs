using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace Api.Services;

/// <summary>
/// Service for tenant-local user operations.
/// Handles user stub creation and audit logging in tenant databases.
/// </summary>
public interface ITenantUserService
{
    /// <summary>
    /// Creates a minimal user stub in the org database for FK relationships.
    /// The full user record exists in control_plane, but org databases need a stub for references.
    /// </summary>
    Task CreateUserStubInTenantDatabaseAsync(OrgContext org, Guid userId, string email, CancellationToken ct = default);

    /// <summary>
    /// Records an audit event in the org's audit log.
    /// </summary>
    Task RecordAuditEventAsync(
        OrgContext org,
        string action,
        Guid? actorUserId = null,
        string? targetType = null,
        string? targetId = null,
        object? metadata = null, CancellationToken ct = default);
}

public class TenantUserService : ITenantUserService
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ILogger<TenantUserService> _logger;

    public TenantUserService(
        IDbConnectionFactory connectionFactory,
        ILogger<TenantUserService> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task CreateUserStubInTenantDatabaseAsync(OrgContext org, Guid userId, string email, CancellationToken ct = default)
    {
        try
        {
            await using var conn = _connectionFactory.CreateOrgConnection(org);
            await conn.OpenAsync(ct);

            await using var cmd = new NpgsqlCommand(@"
                INSERT INTO users (id, email, created_at)
                VALUES (@id, @email, NOW())
                ON CONFLICT DO NOTHING", conn);
            cmd.Parameters.AddWithValue("id", userId);
            cmd.Parameters.AddWithValue("email", email.ToLowerInvariant());

            await cmd.ExecuteNonQueryAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create user stub in tenant database for user {UserId}", userId);
        }
    }

    public async Task RecordAuditEventAsync(
        OrgContext org,
        string action,
        Guid? actorUserId = null,
        string? targetType = null,
        string? targetId = null,
        object? metadata = null, CancellationToken ct = default)
    {
        try
        {
            await using var conn = _connectionFactory.CreateOrgConnection(org);
            await conn.OpenAsync(ct);
            await using var transaction = await conn.BeginTransactionAsync(ct);

            var cmd = new NpgsqlCommand(@"
                INSERT INTO audit_events (actor_user_id, actor_type, action, target_type, target_id, metadata, created_at)
                VALUES (@actorUserId, @actorType, @action, @targetType, @targetId, @metadata, NOW())",
                conn, transaction);

            cmd.Parameters.AddWithValue("actorUserId", actorUserId.HasValue ? actorUserId.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("actorType", actorUserId.HasValue ? "user" : "system");
            cmd.Parameters.AddWithValue("action", action);
            cmd.Parameters.AddWithValue("targetType", (object?)targetType ?? DBNull.Value);
            cmd.Parameters.AddWithValue("targetId", (object?)targetId ?? DBNull.Value);
            cmd.Parameters.Add(new NpgsqlParameter("metadata", NpgsqlDbType.Jsonb)
            {
                Value = metadata != null ? JsonSerializer.Serialize(metadata) : DBNull.Value,
            });

            await cmd.ExecuteNonQueryAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (PostgresException ex) when (ex.SqlState == "42P01")
        {
            _logger.LogWarning("Audit events table does not exist - skipping audit logging");
        }
        catch (Exception ex)
        {
            // Best-effort: auditing must never fail the operation it documents (site/settings/invite
            // writes, and the platform mirror all await this directly). Log — don't propagate.
            _logger.LogError(ex, "Failed to record audit event {Action} for org {OrgId}", action, org.OrgId);
        }
    }
}
