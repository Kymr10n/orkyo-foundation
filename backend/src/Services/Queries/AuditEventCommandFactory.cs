using Npgsql;

namespace Api.Services;

/// <summary>
/// Builds the WHERE clause and parameter list for an
/// <see cref="AuditEventListFilter"/> applied to <c>audit_events</c>.
/// </summary>
public static class AuditEventFilterBinder
{
    /// <summary>
    /// Returns an empty string when no filter members are populated
    /// (i.e. the caller should not prepend a <c>WHERE</c> keyword).
    /// </summary>
    public static (string WhereClause, List<NpgsqlParameter> Parameters) Build(AuditEventListFilter filter)
    {
        var clauses = new List<string>();
        var parameters = new List<NpgsqlParameter>();

        if (!string.IsNullOrWhiteSpace(filter.Action))
        {
            clauses.Add("action = @action");
            parameters.Add(new NpgsqlParameter(AuditEventQueryContract.ActionParameterName, filter.Action));
        }

        if (filter.ActorUserId.HasValue)
        {
            clauses.Add("actor_user_id = @actorId");
            parameters.Add(new NpgsqlParameter(AuditEventQueryContract.ActorIdParameterName, filter.ActorUserId.Value));
        }

        if (!string.IsNullOrWhiteSpace(filter.TargetType))
        {
            clauses.Add("target_type = @targetType");
            parameters.Add(new NpgsqlParameter(AuditEventQueryContract.TargetTypeParameterName, filter.TargetType));
        }

        if (!string.IsNullOrWhiteSpace(filter.TargetId))
        {
            clauses.Add("target_id = @targetId");
            parameters.Add(new NpgsqlParameter(AuditEventQueryContract.TargetIdParameterName, filter.TargetId));
        }

        if (filter.FromUtc.HasValue)
        {
            clauses.Add("created_at >= @from");
            parameters.Add(new NpgsqlParameter(AuditEventQueryContract.FromParameterName, filter.FromUtc.Value));
        }

        if (filter.ToUtc.HasValue)
        {
            clauses.Add("created_at <= @to");
            parameters.Add(new NpgsqlParameter(AuditEventQueryContract.ToParameterName, filter.ToUtc.Value));
        }

        var whereClause = clauses.Count == 0 ? string.Empty : "WHERE " + string.Join(" AND ", clauses);
        return (whereClause, parameters);
    }
}

public static class AuditEventCommandFactory
{
    public static NpgsqlCommand CreateCountCommand(NpgsqlConnection connection, AuditEventListFilter filter)
    {
        var (whereClause, parameters) = AuditEventFilterBinder.Build(filter);
        var command = new NpgsqlCommand(AuditEventQueryContract.BuildCountSql(whereClause), connection);
        foreach (var p in parameters)
            command.Parameters.Add(p);
        return command;
    }

    public static NpgsqlCommand CreateSelectPageCommand(
        NpgsqlConnection connection, AuditEventListFilter filter, int pageSize, int offset)
    {
        var (whereClause, parameters) = AuditEventFilterBinder.Build(filter);
        var command = new NpgsqlCommand(AuditEventQueryContract.BuildSelectPageSql(whereClause), connection);
        foreach (var p in parameters)
            command.Parameters.Add(p);
        command.Parameters.AddWithValue(AuditEventQueryContract.PageSizeParameterName, pageSize);
        command.Parameters.AddWithValue(AuditEventQueryContract.OffsetParameterName, offset);
        return command;
    }
}

public static class AuditEventReaderFlow
{
    /// <summary>
    /// Drain an open reader produced by
    /// <see cref="AuditEventCommandFactory.CreateSelectPageCommand"/> into a
    /// list of <see cref="AuditEventReadProjection"/>. Column order is locked
    /// to <see cref="AuditEventQueryContract.SelectColumns"/>.
    /// </summary>
    public static async Task<List<AuditEventReadProjection>> ReadEventsAsync(NpgsqlDataReader reader)
    {
        var events = new List<AuditEventReadProjection>();
        while (await reader.ReadAsync())
        {
            events.Add(new AuditEventReadProjection(
                Id: reader.GetGuid(0),
                ActorUserId: reader.IsDBNull(1) ? null : reader.GetGuid(1),
                ActorType: reader.GetString(2),
                Action: reader.GetString(3),
                TargetType: reader.IsDBNull(4) ? null : reader.GetString(4),
                TargetId: reader.IsDBNull(5) ? null : reader.GetString(5),
                Metadata: reader.IsDBNull(6) ? null : reader.GetString(6),
                RequestId: reader.IsDBNull(7) ? null : reader.GetString(7),
                IpAddress: reader.IsDBNull(8) ? null : reader.GetString(8),
                CreatedAt: reader.GetDateTime(9)));
        }
        return events;
    }
}
