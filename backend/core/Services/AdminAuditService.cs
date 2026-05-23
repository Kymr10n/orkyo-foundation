using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace Api.Services;

/// <summary>
/// Default <see cref="IAdminAuditService"/> that writes to <c>control_plane.audit_events</c>.
/// Failures are logged but don't break the calling operation — audit logging is
/// best-effort. The table-not-found path lets foundation-only deployments that
/// haven't yet applied the audit migration still operate.
/// </summary>
public sealed class AdminAuditService : IAdminAuditService
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ILogger<AdminAuditService> _logger;

    public AdminAuditService(IDbConnectionFactory connectionFactory, ILogger<AdminAuditService> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task RecordEventAsync(
        Guid? actorUserId,
        string action,
        string? targetType = null,
        string? targetId = null,
        object? metadata = null, CancellationToken ct = default)
    {
        try
        {
            await using var conn = _connectionFactory.CreateControlPlaneConnection();
            await conn.OpenAsync(ct);

            await using var cmd = new NpgsqlCommand(
                @"INSERT INTO audit_events (id, actor_user_id, actor_type, action, target_type, target_id, metadata, created_at)
                  VALUES (@id, @actorUserId, @actorType, @action, @targetType, @targetId, @metadata, NOW())",
                conn);

            cmd.Parameters.AddWithValue("id", Guid.NewGuid());
            cmd.Parameters.AddWithValue("actorUserId", actorUserId.HasValue ? (object)actorUserId.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("actorType", actorUserId.HasValue ? "user" : "system");
            cmd.Parameters.AddWithValue("action", action);
            cmd.Parameters.AddWithValue("targetType", (object?)targetType ?? DBNull.Value);
            cmd.Parameters.AddWithValue("targetId", (object?)targetId ?? DBNull.Value);

            var metadataParam = new NpgsqlParameter("metadata", NpgsqlDbType.Jsonb);
            metadataParam.Value = metadata != null ? JsonSerializer.Serialize(metadata) : DBNull.Value;
            cmd.Parameters.Add(metadataParam);

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
