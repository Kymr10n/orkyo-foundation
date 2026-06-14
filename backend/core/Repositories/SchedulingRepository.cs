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

    public async Task<Guid?> GetSiteIdForResourceAsync(Guid resourceId, CancellationToken ct = default)
    {
        // A resource's anchoring site: spaces use spaces.site_id (immovable); people/tools use
        // resources.home_site_id (administrative anchor / idle-time location). Where a person
        // actually is during an assignment is derived elsewhere from the assignment itself.
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);
        var result = await conn.ExecuteScalarAsync<object>(
            @"SELECT COALESCE(s.site_id, r.home_site_id)
              FROM resources r
              LEFT JOIN spaces s ON s.id = r.id
              WHERE r.id = @resourceId",
            p => p.AddWithValue("resourceId", resourceId), ct);
        return result is Guid siteId ? siteId : null;
    }

    public async Task<Dictionary<Guid, Guid>> GetSiteIdsForResourcesAsync(
        IReadOnlyList<Guid> resourceIds, CancellationToken ct = default)
    {
        if (resourceIds.Count == 0) return [];

        // A resource's anchoring site: spaces use spaces.site_id (immovable); people/tools use
        // resources.home_site_id (administrative anchor). Single resolver so site logic isn't
        // scattered. Unsited resources (null on both) are simply omitted.
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);
        var rows = await conn.QueryListAsync(
            @"SELECT r.id, COALESCE(s.site_id, r.home_site_id) AS site_id
              FROM resources r
              LEFT JOIN spaces s ON s.id = r.id
              WHERE r.id = ANY(@ids)
                AND COALESCE(s.site_id, r.home_site_id) IS NOT NULL",
            p => p.AddWithValue("ids", resourceIds.ToArray()),
            r => (r.GetGuid(0), r.GetGuid(1)), ct);

        return rows.ToDictionary(t => t.Item1, t => t.Item2);
    }

    public async Task<Dictionary<Guid, Guid>> GetResourceTypeIdsAsync(
        IReadOnlyList<Guid> resourceIds, CancellationToken ct = default)
    {
        if (resourceIds.Count == 0) return [];

        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);
        var rows = await conn.QueryListAsync(
            "SELECT id, resource_type_id FROM resources WHERE id = ANY(@ids)",
            p => p.AddWithValue("ids", resourceIds.ToArray()),
            r => (r.GetGuid(0), r.GetGuid(1)), ct);

        return rows.ToDictionary(t => t.Item1, t => t.Item2);
    }

    public async Task<SchedulingSettingsInfo?> GetSettingsAsync(Guid siteId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);
        return await conn.QuerySingleOrDefaultAsync(
            $"SELECT {SettingsSelectColumns} FROM scheduling_settings WHERE site_id = @siteId",
            p => p.AddWithValue("siteId", siteId),
            SchedulingMapper.MapSettingsFromReader, ct);
    }

    public async Task<SchedulingSettingsInfo> UpsertSettingsAsync(Guid siteId, UpsertSchedulingSettingsRequest request, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);

        return (await conn.QuerySingleOrDefaultAsync($@"
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
            RETURNING {SettingsSelectColumnsQualified}",
            p =>
            {
                p.AddWithValue("siteId", siteId);
                p.AddWithValue("timeZone", request.TimeZone);
                p.AddWithValue("workingHoursEnabled", request.WorkingHoursEnabled);
                p.AddWithValue("workingDayStart", TimeSpan.Parse(request.WorkingDayStart));
                p.AddWithValue("workingDayEnd", TimeSpan.Parse(request.WorkingDayEnd));
                p.AddWithValue("weekendsEnabled", request.WeekendsEnabled);
                p.AddWithValue("publicHolidaysEnabled", request.PublicHolidaysEnabled);
                p.AddNullable("publicHolidayRegion", request.PublicHolidayRegion);
            }, SchedulingMapper.MapSettingsFromReader, ct))!;
    }

    public async Task<bool> DeleteSettingsAsync(Guid siteId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);
        return await conn.ExecuteAsync("DELETE FROM scheduling_settings WHERE site_id = @siteId",
            p => p.AddWithValue("siteId", siteId), ct) > 0;
    }
}
