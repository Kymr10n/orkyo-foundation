using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace Api.Services;

public static class TenantAuditEventCommandFactory
{
    /// <summary>
    /// Build the audit-event insert command. Optionally bound to a transaction.
    /// Metadata is JSON-serialized via <see cref="JsonSerializer"/> with default options.
    /// </summary>
    public static NpgsqlCommand CreateInsertAuditEventCommand(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string action,
        Guid? actorUserId,
        string? targetType,
        string? targetId,
        object? metadata)
    {
        var command = transaction is null
            ? new NpgsqlCommand(TenantAuditEventQueryContract.BuildInsertAuditEventSql(), connection)
            : new NpgsqlCommand(TenantAuditEventQueryContract.BuildInsertAuditEventSql(), connection, transaction);

        command.Parameters.AddWithValue(
            TenantAuditEventQueryContract.ActorUserIdParameterName,
            actorUserId.HasValue ? actorUserId.Value : DBNull.Value);
        command.Parameters.AddWithValue(
            TenantAuditEventQueryContract.ActorTypeParameterName,
            TenantAuditEventQueryContract.ResolveActorType(actorUserId));
        command.Parameters.AddWithValue(
            TenantAuditEventQueryContract.ActionParameterName,
            action);
        command.Parameters.AddWithValue(
            TenantAuditEventQueryContract.TargetTypeParameterName,
            (object?)targetType ?? DBNull.Value);
        command.Parameters.AddWithValue(
            TenantAuditEventQueryContract.TargetIdParameterName,
            (object?)targetId ?? DBNull.Value);

        var metadataParam = new NpgsqlParameter(TenantAuditEventQueryContract.MetadataParameterName, NpgsqlDbType.Jsonb)
        {
            Value = metadata != null ? JsonSerializer.Serialize(metadata) : DBNull.Value,
        };
        command.Parameters.Add(metadataParam);

        return command;
    }
}
