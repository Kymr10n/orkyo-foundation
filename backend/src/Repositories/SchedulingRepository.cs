using Api.Models;
using Api.Services;
using Npgsql;

namespace Api.Repositories;

public class SchedulingRepository : ISchedulingRepository
{
    private const string SettingsSelectColumns =
        "id, site_id, time_zone, working_hours_enabled, working_day_start, working_day_end, " +
        "weekends_enabled, public_holidays_enabled, public_holiday_region, created_at, updated_at";

    // Qualified version for INSERT...ON CONFLICT...RETURNING to avoid ambiguity
    // with EXCLUDED.* which PostgreSQL 16+ exposes in the RETURNING clause.
    private const string SettingsSelectColumnsQualified =
        "scheduling_settings.id, scheduling_settings.site_id, scheduling_settings.time_zone, " +
        "scheduling_settings.working_hours_enabled, scheduling_settings.working_day_start, scheduling_settings.working_day_end, " +
        "scheduling_settings.weekends_enabled, scheduling_settings.public_holidays_enabled, " +
        "scheduling_settings.public_holiday_region, scheduling_settings.created_at, scheduling_settings.updated_at";

    private const string OffTimeSelectColumns =
        "id, site_id, title, type, applies_to_all_resources, start_ts, end_ts, " +
        "is_recurring, recurrence_rule, enabled, created_at, updated_at";

    private readonly OrgContext _orgContext;
    private readonly IOrgDbConnectionFactory _connectionFactory;

    public SchedulingRepository(OrgContext orgContext, IOrgDbConnectionFactory connectionFactory)
    {
        _orgContext = orgContext;
        _connectionFactory = connectionFactory;
    }

    // ── Helpers ──────────────────────────────────────────────────────

    public async Task<Guid?> GetSiteIdForResourceAsync(Guid resourceId)
    {
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "SELECT site_id FROM spaces WHERE id = @resourceId", conn);
        cmd.Parameters.AddWithValue("resourceId", resourceId);

        var result = await cmd.ExecuteScalarAsync();
        return result is Guid siteId ? siteId : null;
    }

    // ── Scheduling Settings ─────────────────────────────────────────

    public async Task<SchedulingSettingsInfo?> GetSettingsAsync(Guid siteId)
    {
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            $"SELECT {SettingsSelectColumns} FROM scheduling_settings WHERE site_id = @siteId", conn);
        cmd.Parameters.AddWithValue("siteId", siteId);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;

        return SchedulingMapper.MapSettingsFromReader(reader);
    }

    public async Task<SchedulingSettingsInfo> UpsertSettingsAsync(Guid siteId, UpsertSchedulingSettingsRequest request)
    {
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand($@"
            INSERT INTO scheduling_settings (site_id, time_zone, working_hours_enabled,
                working_day_start, working_day_end, weekends_enabled,
                public_holidays_enabled, public_holiday_region)
            VALUES (@siteId, @timeZone, @workingHoursEnabled,
                @workingDayStart, @workingDayEnd, @weekendsEnabled,
                @publicHolidaysEnabled, @publicHolidayRegion)
            ON CONFLICT (site_id) DO UPDATE SET
                time_zone = @timeZone,
                working_hours_enabled = @workingHoursEnabled,
                working_day_start = @workingDayStart,
                working_day_end = @workingDayEnd,
                weekends_enabled = @weekendsEnabled,
                public_holidays_enabled = @publicHolidaysEnabled,
                public_holiday_region = @publicHolidayRegion
            RETURNING scheduling_settings.id, scheduling_settings.site_id, scheduling_settings.time_zone,
                scheduling_settings.working_hours_enabled, scheduling_settings.working_day_start,
                scheduling_settings.working_day_end, scheduling_settings.weekends_enabled,
                scheduling_settings.public_holidays_enabled, scheduling_settings.public_holiday_region,
                scheduling_settings.created_at, scheduling_settings.updated_at", conn);

        cmd.Parameters.AddWithValue("siteId", siteId);
        cmd.Parameters.AddWithValue("timeZone", request.TimeZone);
        cmd.Parameters.AddWithValue("workingHoursEnabled", request.WorkingHoursEnabled);
        cmd.Parameters.AddWithValue("workingDayStart", TimeSpan.Parse(request.WorkingDayStart));
        cmd.Parameters.AddWithValue("workingDayEnd", TimeSpan.Parse(request.WorkingDayEnd));
        cmd.Parameters.AddWithValue("weekendsEnabled", request.WeekendsEnabled);
        cmd.Parameters.AddWithValue("publicHolidaysEnabled", request.PublicHolidaysEnabled);
        cmd.Parameters.AddWithValue("publicHolidayRegion", (object?)request.PublicHolidayRegion ?? DBNull.Value);

        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        return SchedulingMapper.MapSettingsFromReader(reader);
    }

    public async Task<bool> DeleteSettingsAsync(Guid siteId)
    {
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "DELETE FROM scheduling_settings WHERE site_id = @siteId", conn);
        cmd.Parameters.AddWithValue("siteId", siteId);

        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    // ── Off-Times ───────────────────────────────────────────────────

    public async Task<List<OffTimeInfo>> GetOffTimesAsync(Guid siteId)
    {
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            $"SELECT {OffTimeSelectColumns} FROM off_times WHERE site_id = @siteId ORDER BY start_ts", conn);
        cmd.Parameters.AddWithValue("siteId", siteId);

        var offTimes = new List<OffTimeInfo>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            offTimes.Add(SchedulingMapper.MapOffTimeFromReader(reader));
        }
        reader.Close();

        // Batch-load space associations for non-global off-times
        var nonGlobalIds = offTimes.Where(o => !o.AppliesToAllResources).Select(o => o.Id).ToList();
        if (nonGlobalIds.Count > 0)
        {
            var resourceMap = await LoadOffTimeResourceIdsBatch(conn, nonGlobalIds);
            for (var i = 0; i < offTimes.Count; i++)
            {
                if (resourceMap.TryGetValue(offTimes[i].Id, out var resourceIds))
                    offTimes[i] = offTimes[i] with { ResourceIds = resourceIds };
            }
        }

        return offTimes;
    }

    public async Task<List<OffTimeInfo>> GetOffTimesByResourceAsync(Guid resourceId, Guid siteId)
    {
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            $"SELECT DISTINCT {OffTimeSelectColumns} FROM off_times o " +
            "JOIN off_time_resources otr ON otr.off_time_id = o.id " +
            "WHERE o.site_id = @siteId AND otr.resource_id = @resourceId " +
            "ORDER BY o.start_ts", conn);
        cmd.Parameters.AddWithValue("siteId", siteId);
        cmd.Parameters.AddWithValue("resourceId", resourceId);

        var offTimes = new List<OffTimeInfo>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            offTimes.Add(SchedulingMapper.MapOffTimeFromReader(reader));
        reader.Close();

        for (var i = 0; i < offTimes.Count; i++)
            offTimes[i] = offTimes[i] with { ResourceIds = await LoadOffTimeResourceIds(conn, offTimes[i].Id) };

        return offTimes;
    }

    public async Task<OffTimeInfo?> GetOffTimeByIdAsync(Guid siteId, Guid offTimeId)
    {
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);
        await conn.OpenAsync();

        var offTime = await GetOffTimeCore(conn, siteId, offTimeId);
        if (offTime == null) return null;

        if (!offTime.AppliesToAllResources)
        {
            offTime = offTime with { ResourceIds = await LoadOffTimeResourceIds(conn, offTime.Id) };
        }

        return offTime;
    }

    public async Task<OffTimeInfo?> GetOffTimeByIdAsync(Guid offTimeId)
    {
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            $"SELECT {OffTimeSelectColumns} FROM off_times WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", offTimeId);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        var offTime = SchedulingMapper.MapOffTimeFromReader(reader);
        reader.Close();

        if (!offTime.AppliesToAllResources)
            offTime = offTime with { ResourceIds = await LoadOffTimeResourceIds(conn, offTime.Id) };

        return offTime;
    }

    public async Task<OffTimeInfo> CreateOffTimeAsync(Guid siteId, CreateOffTimeRequest request)
    {
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);
        await conn.OpenAsync();

        await using var tx = await conn.BeginTransactionAsync();

        var offTimeId = Guid.NewGuid();
        await using var cmd = new NpgsqlCommand($@"
            INSERT INTO off_times (id, site_id, title, type, applies_to_all_resources,
                start_ts, end_ts, is_recurring, recurrence_rule, enabled)
            VALUES (@id, @siteId, @title, @type, @appliesToAllResources,
                @startTs, @endTs, @isRecurring, @recurrenceRule, @enabled)
            RETURNING {OffTimeSelectColumns}", conn, tx);

        cmd.Parameters.AddWithValue("id", offTimeId);
        cmd.Parameters.AddWithValue("siteId", siteId);
        cmd.Parameters.AddWithValue("title", request.Title);
        cmd.Parameters.AddWithValue("type", Helpers.EnumMapper.ToDbValue(request.Type));
        cmd.Parameters.AddWithValue("appliesToAllResources", request.AppliesToAllResources);
        cmd.Parameters.AddWithValue("startTs", request.StartTs);
        cmd.Parameters.AddWithValue("endTs", request.EndTs);
        cmd.Parameters.AddWithValue("isRecurring", request.IsRecurring);
        cmd.Parameters.AddWithValue("recurrenceRule", (object?)request.RecurrenceRule ?? DBNull.Value);
        cmd.Parameters.AddWithValue("enabled", request.Enabled);

        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        var offTime = SchedulingMapper.MapOffTimeFromReader(reader);
        reader.Close();

        // Insert space associations if not applies-to-all
        if (!request.AppliesToAllResources && request.ResourceIds is { Count: > 0 })
        {
            await InsertOffTimeResources(conn, tx, offTimeId, request.ResourceIds);
            offTime = offTime with { ResourceIds = request.ResourceIds };
        }

        await tx.CommitAsync();
        return offTime;
    }

    public async Task<OffTimeInfo?> UpdateOffTimeAsync(Guid siteId, Guid offTimeId, UpdateOffTimeRequest request)
    {
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);
        await conn.OpenAsync();

        // Get existing
        var existing = await GetOffTimeCore(conn, siteId, offTimeId);
        if (existing == null) return null;

        await using var tx = await conn.BeginTransactionAsync();

        var title = request.Title ?? existing.Title;
        var type = request.Type ?? existing.Type;
        var appliesToAllResources = request.AppliesToAllResources ?? existing.AppliesToAllResources;
        var startTs = request.StartTs ?? existing.StartTs;
        var endTs = request.EndTs ?? existing.EndTs;
        var isRecurring = request.IsRecurring ?? existing.IsRecurring;
        var recurrenceRule = isRecurring
            ? (request.RecurrenceRule ?? existing.RecurrenceRule)
            : null;
        var enabled = request.Enabled ?? existing.Enabled;

        await using var cmd = new NpgsqlCommand($@"
            UPDATE off_times SET
                title = @title, type = @type, applies_to_all_resources = @appliesToAllResources,
                start_ts = @startTs, end_ts = @endTs, is_recurring = @isRecurring,
                recurrence_rule = @recurrenceRule, enabled = @enabled
            WHERE id = @id AND site_id = @siteId
            RETURNING {OffTimeSelectColumns}", conn, tx);

        cmd.Parameters.AddWithValue("id", offTimeId);
        cmd.Parameters.AddWithValue("siteId", siteId);
        cmd.Parameters.AddWithValue("title", title);
        cmd.Parameters.AddWithValue("type", Helpers.EnumMapper.ToDbValue(type));
        cmd.Parameters.AddWithValue("appliesToAllResources", appliesToAllResources);
        cmd.Parameters.AddWithValue("startTs", startTs);
        cmd.Parameters.AddWithValue("endTs", endTs);
        cmd.Parameters.AddWithValue("isRecurring", isRecurring);
        cmd.Parameters.AddWithValue("recurrenceRule", (object?)recurrenceRule ?? DBNull.Value);
        cmd.Parameters.AddWithValue("enabled", enabled);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            await tx.RollbackAsync();
            return null;
        }
        var offTime = SchedulingMapper.MapOffTimeFromReader(reader);
        reader.Close();

        // Update space associations if changed
        if (request.ResourceIds != null || request.AppliesToAllResources.HasValue)
        {
            await using var delCmd = new NpgsqlCommand(
                "DELETE FROM off_time_resources WHERE off_time_id = @id", conn, tx);
            delCmd.Parameters.AddWithValue("id", offTimeId);
            await delCmd.ExecuteNonQueryAsync();

            if (!appliesToAllResources && request.ResourceIds is { Count: > 0 })
            {
                await InsertOffTimeResources(conn, tx, offTimeId, request.ResourceIds);
                offTime = offTime with { ResourceIds = request.ResourceIds };
            }
        }
        else if (!offTime.AppliesToAllResources)
        {
            offTime = offTime with { ResourceIds = await LoadOffTimeResourceIds(conn, offTimeId) };
        }

        await tx.CommitAsync();
        return offTime;
    }

    public async Task<bool> DeleteOffTimeAsync(Guid siteId, Guid offTimeId)
    {
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "DELETE FROM off_times WHERE id = @id AND site_id = @siteId", conn);
        cmd.Parameters.AddWithValue("id", offTimeId);
        cmd.Parameters.AddWithValue("siteId", siteId);

        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    // ── Helpers ─────────────────────────────────────────────────────

    private static async Task<List<Guid>> LoadOffTimeResourceIds(NpgsqlConnection conn, Guid offTimeId)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT resource_id FROM off_time_resources WHERE off_time_id = @id", conn);
        cmd.Parameters.AddWithValue("id", offTimeId);

        var resourceIds = new List<Guid>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            resourceIds.Add(reader.GetGuid(0));
        }
        return resourceIds;
    }

    private static async Task<Dictionary<Guid, List<Guid>>> LoadOffTimeResourceIdsBatch(
        NpgsqlConnection conn, List<Guid> offTimeIds)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT off_time_id, resource_id FROM off_time_resources WHERE off_time_id = ANY(@ids)", conn);
        cmd.Parameters.AddWithValue("ids", offTimeIds.ToArray());

        var map = new Dictionary<Guid, List<Guid>>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var otId = reader.GetGuid(0);
            var resourceId = reader.GetGuid(1);
            if (!map.TryGetValue(otId, out var list))
            {
                list = [];
                map[otId] = list;
            }
            list.Add(resourceId);
        }
        return map;
    }

    private static async Task InsertOffTimeResources(
        NpgsqlConnection conn, NpgsqlTransaction tx, Guid offTimeId, List<Guid> resourceIds)
    {
        if (resourceIds.Count == 0) return;

        var values = string.Join(", ",
            Enumerable.Range(0, resourceIds.Count).Select(i => $"(@offTimeId, @s{i})"));
        await using var cmd = new NpgsqlCommand(
            $"INSERT INTO off_time_resources (off_time_id, resource_id) VALUES {values}", conn, tx);
        cmd.Parameters.AddWithValue("offTimeId", offTimeId);
        for (var i = 0; i < resourceIds.Count; i++)
            cmd.Parameters.AddWithValue($"s{i}", resourceIds[i]);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<OffTimeInfo?> GetOffTimeCore(NpgsqlConnection conn, Guid siteId, Guid offTimeId)
    {
        await using var cmd = new NpgsqlCommand(
            $"SELECT {OffTimeSelectColumns} FROM off_times WHERE id = @id AND site_id = @siteId", conn);
        cmd.Parameters.AddWithValue("id", offTimeId);
        cmd.Parameters.AddWithValue("siteId", siteId);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;
        return SchedulingMapper.MapOffTimeFromReader(reader);
    }
}
