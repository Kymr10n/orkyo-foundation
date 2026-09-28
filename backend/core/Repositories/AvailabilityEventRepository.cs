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

    public async Task<Dictionary<Guid, List<AvailabilityEventInfo>>> GetBySitesAsync(IReadOnlyList<Guid> siteIds, CancellationToken ct = default)
    {
        if (siteIds.Count == 0) return [];

        await using var conn = connectionFactory.CreateOrgConnection(orgContext);
        await conn.OpenAsync(ct);

        var events = await conn.QueryListAsync(
            $"SELECT {EventCols} FROM availability_events WHERE site_id = ANY(@ids) ORDER BY site_id, start_ts",
            p => p.AddWithValue("ids", siteIds.ToArray()),
            SchedulingMapper.MapAvailabilityEventFromReader, ct);
        await HydrateScopesAsync(conn, events, ct);

        return events.GroupBy(x => x.SiteId).ToDictionary(g => g.Key, g => g.ToList());
    }

    public async Task<AvailabilityEventInfo?> GetByIdAsync(Guid siteId, Guid id, CancellationToken ct = default)
    {
        await using var conn = connectionFactory.CreateOrgConnection(orgContext);
        await conn.OpenAsync(ct);

        var ev = await FetchEventByIdCoreAsync(conn, siteId, id, ct);
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

    public async Task<AvailabilityEventInfo?> UpdateAsync(Guid siteId, Guid id, UpdateAvailabilityEventRequest request, CancellationToken ct = default)
    {
        // Only the fields the request carries are written: a read-merge-write of the whole row
        // would overwrite a concurrent update of another field with the value read before it.
        var update = RecurringWindowUpdate.Build(request, request.Enabled)
            .SetIfNotNull("title", request.Title)
            .SetIfNotNull("description", request.Description)
            .SetIfNotNull("event_type", request.EventType is { } eventType ? EnumMapper.ToDbValue(eventType) : null)
            .SetIfNotNull("default_effect", request.DefaultEffect is { } effect ? EnumMapper.ToDbValue(effect) : null);

        await using var conn = connectionFactory.CreateOrgConnection(orgContext);
        await conn.OpenAsync(ct);

        var updated = update.IsEmpty
            ? await FetchEventByIdCoreAsync(conn, siteId, id, ct)
            : await conn.QuerySingleOrDefaultAsync(
                $"UPDATE availability_events SET {update.SetClause} WHERE id = @id AND site_id = @siteId RETURNING {EventCols}",
                p =>
                {
                    p.AddWithValue("id", id);
                    p.AddWithValue("siteId", siteId);
                    RecurringWindowUpdate.Bind(p, request);
                    update.Apply(p);
                }, SchedulingMapper.MapAvailabilityEventFromReader, ct);
        if (updated is null) return null;

        var scopeMap = await FetchScopesByEventAsync(conn, [id], ct);
        return updated with { Scopes = scopeMap.GetValueOrDefault(id, []) };
    }

    public async Task<bool> DeleteAsync(Guid siteId, Guid id, CancellationToken ct = default)
    {
        await using var conn = connectionFactory.CreateOrgConnection(orgContext);
        return await conn.ExecuteAsync("DELETE FROM availability_events WHERE id = @id AND site_id = @siteId",
            p => { p.AddWithValue("id", id); p.AddWithValue("siteId", siteId); }, ct) > 0;
    }

    // ── Scope mutations ──────────────────────────────────────────────────────

    public async Task<AvailabilityEventScopeInfo?> AddScopeAsync(Guid siteId, Guid eventId, AddScopeRequest request, CancellationToken ct = default)
    {
        await using var conn = connectionFactory.CreateOrgConnection(orgContext);
        await conn.OpenAsync(ct);
        if (await FetchEventByIdCoreAsync(conn, siteId, eventId, ct) is null)
            return null;
        return await InsertScopeAsync(conn, null, eventId, request, ct);
    }

    public async Task<bool> DeleteScopeAsync(Guid siteId, Guid eventId, Guid scopeId, CancellationToken ct = default)
    {
        await using var conn = connectionFactory.CreateOrgConnection(orgContext);
        return await conn.ExecuteAsync(@"
            DELETE FROM availability_event_scopes s
             USING availability_events e
             WHERE s.id = @id AND s.availability_event_id = @eventId
               AND e.id = s.availability_event_id AND e.site_id = @siteId",
            p => { p.AddWithValue("id", scopeId); p.AddWithValue("eventId", eventId); p.AddWithValue("siteId", siteId); }, ct) > 0;
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private Task<List<AvailabilityEventInfo>> FetchEventsBySiteAsync(
        NpgsqlConnection conn, Guid siteId, CancellationToken ct)
        => conn.QueryListAsync(
            $"SELECT {EventCols} FROM availability_events WHERE site_id = @siteId ORDER BY start_ts",
            p => p.AddWithValue("siteId", siteId),
            SchedulingMapper.MapAvailabilityEventFromReader, ct);

    private Task<AvailabilityEventInfo?> FetchEventByIdCoreAsync(
        NpgsqlConnection conn, Guid siteId, Guid id, CancellationToken ct)
        => conn.QuerySingleOrDefaultAsync(
            $"SELECT {EventCols} FROM availability_events WHERE id = @id AND site_id = @siteId",
            p => { p.AddWithValue("id", id); p.AddWithValue("siteId", siteId); },
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

        return scopes.GroupBy(x => x.AvailabilityEventId).ToDictionary(g => g.Key, g => g.ToList());
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
        cmd.Parameters.AddNullable("description", description);
        cmd.Parameters.AddWithValue("eventType", EnumMapper.ToDbValue(eventType));
        cmd.Parameters.AddWithValue("defaultEffect", EnumMapper.ToDbValue(defaultEffect));
        cmd.Parameters.AddWithValue("startTs", startTs);
        cmd.Parameters.AddWithValue("endTs", endTs);
        cmd.Parameters.AddWithValue("isRecurring", isRecurring);
        cmd.Parameters.AddNullable("recurrenceRule", recurrenceRule);
        cmd.Parameters.AddWithValue("enabled", enabled);
    }
}
