using Api.Helpers;
using Api.Models;
using Api.Services;
using Npgsql;

namespace Api.Repositories;

public class AvailabilityEventRepository(OrgContext orgContext, IOrgDbConnectionFactory connectionFactory)
    : IAvailabilityEventRepository
{
    private const string EventCols =
        "id, site_id, title, description, event_type, default_effect, " +
        "start_ts, end_ts, is_recurring, recurrence_rule, enabled, created_at, updated_at";

    private const string ScopeCols =
        "id, availability_event_id, target_type, target_id, effect";

    // ── Queries ──────────────────────────────────────────────────────────────

    public async Task<List<AvailabilityEventInfo>> GetBySiteAsync(Guid siteId, CancellationToken ct = default)
    {
        await using var conn = connectionFactory.CreateOrgConnection(orgContext);
        await conn.OpenAsync(ct);

        var events = await FetchEventsBySiteAsync(conn, siteId, ct);
        await HydrateScopesAsync(conn, events, ct);
        return events;
    }

    public async Task<AvailabilityEventInfo?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = connectionFactory.CreateOrgConnection(orgContext);
        await conn.OpenAsync(ct);

        var ev = await FetchEventByIdCoreAsync(conn, id, ct);
        if (ev == null) return null;

        var scopes = await FetchScopesByEventAsync(conn, [id], ct);
        return ev with { Scopes = scopes.GetValueOrDefault(id, []) };
    }

    public async Task<List<AvailabilityEventInfo>> GetEnabledBySiteWithScopesAsync(Guid siteId, CancellationToken ct = default)
    {
        await using var conn = connectionFactory.CreateOrgConnection(orgContext);
        var events = await conn.QueryListAsync(
            $"SELECT {EventCols} FROM availability_events WHERE site_id = @siteId AND enabled = true ORDER BY start_ts",
            p => p.AddWithValue("siteId", siteId),
            SchedulingMapper.MapAvailabilityEventFromReader, ct);
        await HydrateScopesAsync(conn, events, ct);
        return events;
    }

    // ── Mutations ────────────────────────────────────────────────────────────

    public async Task<AvailabilityEventInfo> CreateAsync(Guid siteId, CreateAvailabilityEventRequest request, CancellationToken ct = default)
    {
        await using var conn = connectionFactory.CreateOrgConnection(orgContext);
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var id = Guid.NewGuid();
        await using var cmd = new NpgsqlCommand($@"
            INSERT INTO availability_events
                (id, site_id, title, description, event_type, default_effect,
                 start_ts, end_ts, is_recurring, recurrence_rule, enabled)
            VALUES
                (@id, @siteId, @title, @description, @eventType, @defaultEffect,
                 @startTs, @endTs, @isRecurring, @recurrenceRule, @enabled)
            RETURNING {EventCols}", conn, tx);

        BindEventParams(cmd, id, siteId, request.Title, request.Description,
            request.EventType, request.DefaultEffect,
            request.StartTs, request.EndTs, request.IsRecurring, request.RecurrenceRule, request.Enabled);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        var ev = SchedulingMapper.MapAvailabilityEventFromReader(reader);
        reader.Close();

        var scopes = new List<AvailabilityEventScopeInfo>();
        foreach (var s in request.Scopes)
            scopes.Add(await InsertScopeAsync(conn, tx, id, s, ct));

        await tx.CommitAsync(ct);
        return ev with { Scopes = scopes };
    }

    public async Task<AvailabilityEventInfo?> UpdateAsync(Guid id, UpdateAvailabilityEventRequest request, CancellationToken ct = default)
    {
        await using var conn = connectionFactory.CreateOrgConnection(orgContext);
        await conn.OpenAsync(ct);

        var existing = await FetchEventByIdCoreAsync(conn, id, ct);
        if (existing == null) return null;

        var isRecurring = request.IsRecurring ?? existing.IsRecurring;
        var recurrenceRule = isRecurring ? (request.RecurrenceRule ?? existing.RecurrenceRule) : null;

        await using var cmd = new NpgsqlCommand($@"
            UPDATE availability_events SET
                title           = @title,
                description     = @description,
                event_type      = @eventType,
                default_effect  = @defaultEffect,
                start_ts        = @startTs,
                end_ts          = @endTs,
                is_recurring    = @isRecurring,
                recurrence_rule = @recurrenceRule,
                enabled         = @enabled
            WHERE id = @id
            RETURNING {EventCols}", conn);

        BindEventParams(cmd, id, existing.SiteId,
            request.Title ?? existing.Title,
            request.Description ?? existing.Description,
            request.EventType ?? existing.EventType,
            request.DefaultEffect ?? existing.DefaultEffect,
            request.StartTs ?? existing.StartTs,
            request.EndTs ?? existing.EndTs,
            isRecurring, recurrenceRule,
            request.Enabled ?? existing.Enabled);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        var updated = SchedulingMapper.MapAvailabilityEventFromReader(reader);
        reader.Close();

        var scopeMap = await FetchScopesByEventAsync(conn, [id], ct);
        return updated with { Scopes = scopeMap.GetValueOrDefault(id, []) };
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = connectionFactory.CreateOrgConnection(orgContext);
        return await conn.ExecuteAsync("DELETE FROM availability_events WHERE id = @id",
            p => p.AddWithValue("id", id), ct) > 0;
    }

    // ── Scope mutations ──────────────────────────────────────────────────────

    public async Task<AvailabilityEventScopeInfo> AddScopeAsync(Guid eventId, AddScopeRequest request, CancellationToken ct = default)
    {
        await using var conn = connectionFactory.CreateOrgConnection(orgContext);
        await conn.OpenAsync(ct);
        return await InsertScopeAsync(conn, null, eventId, request, ct);
    }

    public async Task<bool> DeleteScopeAsync(Guid eventId, Guid scopeId, CancellationToken ct = default)
    {
        await using var conn = connectionFactory.CreateOrgConnection(orgContext);
        return await conn.ExecuteAsync(
            "DELETE FROM availability_event_scopes WHERE id = @id AND availability_event_id = @eventId",
            p => { p.AddWithValue("id", scopeId); p.AddWithValue("eventId", eventId); }, ct) > 0;
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private Task<List<AvailabilityEventInfo>> FetchEventsBySiteAsync(
        NpgsqlConnection conn, Guid siteId, CancellationToken ct)
        => conn.QueryListAsync(
            $"SELECT {EventCols} FROM availability_events WHERE site_id = @siteId ORDER BY start_ts",
            p => p.AddWithValue("siteId", siteId),
            SchedulingMapper.MapAvailabilityEventFromReader, ct);

    private Task<AvailabilityEventInfo?> FetchEventByIdCoreAsync(
        NpgsqlConnection conn, Guid id, CancellationToken ct)
        => conn.QuerySingleOrDefaultAsync(
            $"SELECT {EventCols} FROM availability_events WHERE id = @id",
            p => p.AddWithValue("id", id),
            SchedulingMapper.MapAvailabilityEventFromReader, ct);

    private async Task HydrateScopesAsync(
        NpgsqlConnection conn, List<AvailabilityEventInfo> events, CancellationToken ct)
    {
        if (events.Count == 0) return;
        var ids = events.Select(e => e.Id).ToList();
        var scopeMap = await FetchScopesByEventAsync(conn, ids, ct);
        for (var i = 0; i < events.Count; i++)
        {
            if (scopeMap.TryGetValue(events[i].Id, out var scopes))
                events[i] = events[i] with { Scopes = scopes };
        }
    }

    private async Task<Dictionary<Guid, List<AvailabilityEventScopeInfo>>> FetchScopesByEventAsync(
        NpgsqlConnection conn, List<Guid> eventIds, CancellationToken ct)
    {
        var scopes = await conn.QueryListAsync(
            $"SELECT {ScopeCols} FROM availability_event_scopes WHERE availability_event_id = ANY(@ids)",
            p => p.AddWithValue("ids", eventIds.ToArray()),
            SchedulingMapper.MapScopeFromReader, ct);

        var map = new Dictionary<Guid, List<AvailabilityEventScopeInfo>>();
        foreach (var scope in scopes)
        {
            if (!map.TryGetValue(scope.AvailabilityEventId, out var list))
            {
                list = [];
                map[scope.AvailabilityEventId] = list;
            }
            list.Add(scope);
        }
        return map;
    }

    private static async Task<AvailabilityEventScopeInfo> InsertScopeAsync(
        NpgsqlConnection conn, NpgsqlTransaction? tx, Guid eventId, AddScopeRequest request, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand($@"
            INSERT INTO availability_event_scopes (availability_event_id, target_type, target_id, effect)
            VALUES (@eventId, @targetType, @targetId, @effect)
            RETURNING {ScopeCols}", conn, tx!);

        cmd.Parameters.AddWithValue("eventId", eventId);
        cmd.Parameters.AddWithValue("targetType", EnumMapper.ToDbValue(request.TargetType));
        cmd.Parameters.AddWithValue("targetId", request.TargetId);
        cmd.Parameters.AddWithValue("effect", EnumMapper.ToDbValue(request.Effect));

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return SchedulingMapper.MapScopeFromReader(reader);
    }

    private static void BindEventParams(
        NpgsqlCommand cmd, Guid id, Guid siteId,
        string title, string? description,
        AvailabilityEventType eventType, DefaultEffect defaultEffect,
        DateTime startTs, DateTime endTs,
        bool isRecurring, string? recurrenceRule, bool enabled)
    {
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("siteId", siteId);
        cmd.Parameters.AddWithValue("title", title);
        cmd.Parameters.AddWithValue("description", (object?)description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("eventType", EnumMapper.ToDbValue(eventType));
        cmd.Parameters.AddWithValue("defaultEffect", EnumMapper.ToDbValue(defaultEffect));
        cmd.Parameters.AddWithValue("startTs", startTs);
        cmd.Parameters.AddWithValue("endTs", endTs);
        cmd.Parameters.AddWithValue("isRecurring", isRecurring);
        cmd.Parameters.AddWithValue("recurrenceRule", (object?)recurrenceRule ?? DBNull.Value);
        cmd.Parameters.AddWithValue("enabled", enabled);
    }
}
