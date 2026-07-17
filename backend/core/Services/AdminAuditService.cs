using System.Text.Json;
using Api.Security;
using Npgsql;
using NpgsqlTypes;

namespace Api.Services;

/// <summary>
/// Default <see cref="IAdminAuditService"/> that writes to <c>control_plane.audit_events</c>.
/// Failures are logged but don't break the calling operation — audit logging is best-effort.
/// The table-not-found path lets foundation-only deployments that haven't yet applied the
/// audit migration still operate.
///
/// The control-plane table requires an explicit <c>id</c> column (tenant-DB audit rows rely
/// on a database default); actor-type semantics match the tenant-side writer in
/// <see cref="TenantUserService"/>: a present actor user-id → <c>"user"</c>, else <c>"system"</c>.
/// </summary>
public sealed class AdminAuditService : IAdminAuditService
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICurrentTenant _currentTenant;
    private readonly ILogger<AdminAuditService> _logger;

    public AdminAuditService(IDbConnectionFactory connectionFactory, ICurrentTenant currentTenant, ILogger<AdminAuditService> logger)
    {
        _connectionFactory = connectionFactory;
        _currentTenant = currentTenant;
        _logger = logger;
    }

    public async Task RecordEventAsync(
        Guid? actorUserId,
        string action,
        string? targetType = null,
        string? targetId = null,
        object? metadata = null, CancellationToken ct = default)
    {
        // Capture the tenant synchronously (before the first await) so fire-and-forget callers
        // still stamp the right tenant even after the request scope unwinds. Events with no
        // resolved tenant (platform/site-admin, SkipTenantResolution) stay NULL.
        Guid? tenantId = _currentTenant.HasTenant ? _currentTenant.TenantId : null;
        try
        {
            await using var conn = _connectionFactory.CreateControlPlaneConnection();
            await conn.OpenAsync(ct);

            await using var cmd = new NpgsqlCommand(@"
                INSERT INTO audit_events (id, tenant_id, actor_user_id, actor_type, action, target_type, target_id, metadata, created_at)
                VALUES (@id, @tenantId, @actorUserId, @actorType, @action, @targetType, @targetId, @metadata, NOW())", conn);

            cmd.Parameters.AddWithValue("id", Guid.NewGuid());
            cmd.Parameters.AddWithValue("tenantId", tenantId.HasValue ? tenantId.Value : DBNull.Value);
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
        }
        catch (PostgresException ex) when (ex.SqlState == "42P01")
        {
            _logger.LogWarning("audit_events table does not exist — skipping audit");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record audit event: {Action}", action);
        }
    }
}
