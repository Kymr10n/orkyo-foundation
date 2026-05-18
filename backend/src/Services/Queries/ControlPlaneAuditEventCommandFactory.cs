using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace Api.Services;

public static class ControlPlaneAuditEventCommandFactory
{
    /// <summary>
    /// Build the control-plane audit-event insert command. The row id is
    /// generated here via <see cref="Guid.NewGuid"/> to keep the SaaS caller
    /// free of identifier-allocation concerns. Metadata is JSON-serialized
    /// via <see cref="JsonSerializer"/> with default options.
    /// </summary>
    public static NpgsqlCommand CreateInsertAuditEventCommand(
        NpgsqlConnection connection,
        string action,
        Guid? actorUserId,
        string? targetType,
        string? targetId,
        object? metadata)
    {
        var command = new NpgsqlCommand(ControlPlaneAuditEventQueryContract.BuildInsertAuditEventSql(), connection);

        command.Parameters.AddWithValue(
            ControlPlaneAuditEventQueryContract.IdParameterName,
            Guid.NewGuid());
        command.Parameters.AddWithValue(
            ControlPlaneAuditEventQueryContract.ActorUserIdParameterName,
            actorUserId.HasValue ? actorUserId.Value : DBNull.Value);
        command.Parameters.AddWithValue(
            ControlPlaneAuditEventQueryContract.ActorTypeParameterName,
            ControlPlaneAuditEventQueryContract.ResolveActorType(actorUserId));
        command.Parameters.AddWithValue(
            ControlPlaneAuditEventQueryContract.ActionParameterName,
            action);
        command.Parameters.AddWithValue(
            ControlPlaneAuditEventQueryContract.TargetTypeParameterName,
            (object?)targetType ?? DBNull.Value);
        command.Parameters.AddWithValue(
            ControlPlaneAuditEventQueryContract.TargetIdParameterName,
            (object?)targetId ?? DBNull.Value);

        var metadataParam = new NpgsqlParameter(ControlPlaneAuditEventQueryContract.MetadataParameterName, NpgsqlDbType.Jsonb)
        {
            Value = metadata != null ? JsonSerializer.Serialize(metadata) : DBNull.Value,
        };
        command.Parameters.Add(metadataParam);

        return command;
    }
}
