using Api.Models;
using Api.Services;
using Npgsql;

namespace Api.Repositories;

public class SchedulingRepository : ISchedulingRepository
{
    private const string SettingsSelectColumns =
        "id, site_id, time_zone, working_hours_enabled, working_day_start, working_day_end, " +
        "weekends_enabled, public_holidays_enabled, public_holiday_region, created_at, updated_at";

    private const string SettingsSelectColumnsQualified =
        "scheduling_settings.id, scheduling_settings.site_id, scheduling_settings.time_zone, " +
        "scheduling_settings.working_hours_enabled, scheduling_settings.working_day_start, scheduling_settings.working_day_end, " +
        "scheduling_settings.weekends_enabled, scheduling_settings.public_holidays_enabled, " +
        "scheduling_settings.public_holiday_region, scheduling_settings.created_at, scheduling_settings.updated_at";

    private readonly OrgContext _orgContext;
    private readonly IOrgDbConnectionFactory _connectionFactory;

    public SchedulingRepository(OrgContext orgContext, IOrgDbConnectionFactory connectionFactory)
    {
        _orgContext = orgContext;
        _connectionFactory = connectionFactory;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    public async Task<Guid?> GetSiteIdForResourceAsync(Guid resourceId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(
            "SELECT site_id FROM spaces WHERE id = @resourceId", conn);
        cmd.Parameters.AddWithValue("resourceId", resourceId);

        var result = await cmd.ExecuteScalarAsync(ct);
        return result is Guid siteId ? siteId : null;
    }

    public async Task<Dictionary<Guid, Guid>> GetResourceTypeIdsAsync(
        IReadOnlyList<Guid> resourceIds, CancellationToken ct = default)
    {
        if (resourceIds.Count == 0) return [];

        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(
            "SELECT id, resource_type_id FROM resources WHERE id = ANY(@ids)", conn);
        cmd.Parameters.AddWithValue("ids", resourceIds.ToArray());

        var result = new Dictionary<Guid, Guid>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            result[reader.GetGuid(0)] = reader.GetGuid(1);

        return result;
    }

    // ── Scheduling Settings ──────────────────────────────────────────────────

    public async Task<SchedulingSettingsInfo?> GetSettingsAsync(Guid siteId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(
            $"SELECT {SettingsSelectColumns} FROM scheduling_settings WHERE site_id = @siteId", conn);
        cmd.Parameters.AddWithValue("siteId", siteId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;

        return SchedulingMapper.MapSettingsFromReader(reader);
    }

    public async Task<SchedulingSettingsInfo> UpsertSettingsAsync(Guid siteId, UpsertSchedulingSettingsRequest request, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);
        await conn.OpenAsync(ct);

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
            RETURNING {SettingsSelectColumnsQualified}", conn);

        cmd.Parameters.AddWithValue("siteId", siteId);
        cmd.Parameters.AddWithValue("timeZone", request.TimeZone);
        cmd.Parameters.AddWithValue("workingHoursEnabled", request.WorkingHoursEnabled);
        cmd.Parameters.AddWithValue("workingDayStart", TimeSpan.Parse(request.WorkingDayStart));
        cmd.Parameters.AddWithValue("workingDayEnd", TimeSpan.Parse(request.WorkingDayEnd));
        cmd.Parameters.AddWithValue("weekendsEnabled", request.WeekendsEnabled);
        cmd.Parameters.AddWithValue("publicHolidaysEnabled", request.PublicHolidaysEnabled);
        cmd.Parameters.AddWithValue("publicHolidayRegion", (object?)request.PublicHolidayRegion ?? DBNull.Value);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return SchedulingMapper.MapSettingsFromReader(reader);
    }

    public async Task<bool> DeleteSettingsAsync(Guid siteId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(
            "DELETE FROM scheduling_settings WHERE site_id = @siteId", conn);
        cmd.Parameters.AddWithValue("siteId", siteId);

        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }
}
