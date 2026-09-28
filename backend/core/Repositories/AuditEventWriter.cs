using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace Api.Repositories;

/// <summary>
/// The one INSERT into an <c>audit_events</c> table, shared by the control-plane writer
/// (<see cref="Api.Services.AdminAuditService"/>) and the tenant-database writer
/// (<see cref="Api.Services.TenantUserService.RecordAuditEventAsync"/>): a present actor user-id is
/// <c>"user"</c>, else <c>"system"</c>, and metadata is stored as jsonb. The control-plane table
/// needs an explicit <c>id</c> and carries the tenant; the tenant table uses its defaults.
/// </summary>
internal static class AuditEventWriter
{
    /// <summary>Writes one row to the control plane's <c>audit_events</c>; a NULL tenant marks a platform event.</summary>
    public static Task InsertControlPlaneAsync(
        NpgsqlConnection conn, Guid? tenantId, Guid? actorUserId, string action,
        string? targetType, string? targetId, object? metadata, CancellationToken ct)
    {
        var cmd = new NpgsqlCommand(@"
            INSERT INTO audit_events (id, tenant_id, actor_user_id, actor_type, action, target_type, target_id, metadata, created_at)
            VALUES (@id, @tenantId, @actorUserId, @actorType, @action, @targetType, @targetId, @metadata, NOW())", conn);
        cmd.Parameters.AddWithValue("id", Guid.NewGuid());
        cmd.Parameters.AddWithValue("tenantId", tenantId.HasValue ? tenantId.Value : DBNull.Value);
        return ExecuteAsync(cmd, actorUserId, action, targetType, targetId, metadata, ct);
    }

    /// <summary>Writes one row to a tenant database's <c>audit_events</c>.</summary>
    public static Task InsertTenantAsync(
        NpgsqlConnection conn, Guid? actorUserId, string action,
        string? targetType, string? targetId, object? metadata, CancellationToken ct) =>
        ExecuteAsync(new NpgsqlCommand(@"
            INSERT INTO audit_events (actor_user_id, actor_type, action, target_type, target_id, metadata, created_at)
            VALUES (@actorUserId, @actorType, @action, @targetType, @targetId, @metadata, NOW())", conn),
            actorUserId, action, targetType, targetId, metadata, ct);

    private static async Task ExecuteAsync(
        NpgsqlCommand cmd, Guid? actorUserId, string action,
        string? targetType, string? targetId, object? metadata, CancellationToken ct)
    {
        await using (cmd)
        {
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
    }
}
