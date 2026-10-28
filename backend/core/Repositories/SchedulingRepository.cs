using Api.Helpers;
using Api.Models;
using Api.Services;
using Npgsql;

namespace Api.Repositories;

public class SchedulingRepository : ISchedulingRepository
{
    private const string SettingsSelectColumns =
        "id, site_id, time_zone, working_hours_enabled, working_day_start, working_day_end, " +
        "weekends_enabled, public_holidays_enabled, public_holiday_region, created_at, updated_at, xmin AS row_version";

    private const string SettingsSelectColumnsQualified =
        "scheduling_settings.id, scheduling_settings.site_id, scheduling_settings.time_zone, " +
        "scheduling_settings.working_hours_enabled, scheduling_settings.working_day_start, scheduling_settings.working_day_end, " +
        "scheduling_settings.weekends_enabled, scheduling_settings.public_holidays_enabled, " +
        "scheduling_settings.public_holiday_region, scheduling_settings.created_at, scheduling_settings.updated_at, " +
        "scheduling_settings.xmin AS row_version";

    private readonly OrgContext _orgContext;
    private readonly IOrgDbConnectionFactory _connectionFactory;

    public SchedulingRepository(OrgContext orgContext, IOrgDbConnectionFactory connectionFactory)
    {
        _orgContext = orgContext;
        _connectionFactory = connectionFactory;
    }

    public async Task<Guid?> GetSiteIdForResourceAsync(Guid resourceId, CancellationToken ct = default)
    {
        // A resource's anchoring site: resources.home_site_id — immovable for a space, the
        // administrative anchor / idle-time location for a person or tool. Where a person
        // actually is during an assignment is derived elsewhere from the assignment itself.
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);
        var result = await conn.ExecuteScalarAsync<object>(
            @"SELECT home_site_id
              FROM resources
              WHERE id = @resourceId",
            p => p.AddWithValue("resourceId", resourceId), ct);
        return result is Guid siteId ? siteId : null;
    }

    public async Task<Dictionary<Guid, Guid>> GetSiteIdsForResourcesAsync(
        IReadOnlyList<Guid> resourceIds, CancellationToken ct = default)
    {
        if (resourceIds.Count == 0) return [];

        // A resource's anchoring site: resources.home_site_id — immovable for a space, the
        // administrative anchor for a person or tool. Single resolver so site logic isn't
        // scattered. Unsited resources are simply omitted.
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);
        var rows = await conn.QueryListAsync(
            @"SELECT id, home_site_id
              FROM resources
              WHERE id = ANY(@ids)
                AND home_site_id IS NOT NULL",
            p => p.AddWithValue("ids", resourceIds.ToArray()),
            r => (r.GetGuid("id"), r.GetGuid("home_site_id")), ct);

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
            r => (r.GetGuid("id"), r.GetGuid("resource_type_id")), ct);

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

    public async Task<Dictionary<Guid, SchedulingSettingsInfo>> GetSettingsBySitesAsync(
        IReadOnlyList<Guid> siteIds, CancellationToken ct = default)
    {
        if (siteIds.Count == 0) return [];

        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);
        var rows = await conn.QueryListAsync(
            $"SELECT {SettingsSelectColumns} FROM scheduling_settings WHERE site_id = ANY(@ids)",
            p => p.AddWithValue("ids", siteIds.ToArray()),
            SchedulingMapper.MapSettingsFromReader, ct);

        return rows.ToDictionary(s => s.SiteId);
    }

    // The columns a save writes. Each binds a parameter of the same name, so the one list builds
    // the INSERT, the upsert's SET (from EXCLUDED) and the version-guarded UPDATE's SET.
    private static readonly string[] SettingsColumns =
    [
        "time_zone", "working_hours_enabled", "working_day_start", "working_day_end",
        "weekends_enabled", "public_holidays_enabled", "public_holiday_region",
    ];

    private static string SettingsSet(string source) =>
        string.Join(", ", SettingsColumns.Select(c => $"{c} = {source}{c}"));

    public async Task<SchedulingSettingsInfo> SaveSettingsAsync(
        SchedulingSettingsInfo settings, uint? expectedVersion,
        IReadOnlyList<(Guid Id, uint Version, ScheduleRequestRequest Data)> recalculated, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        // A caller that read stored settings (version > 0) updates that row at that version only.
        // Any other save creates the row: without a version it upserts (last write wins); with
        // version 0 (the caller read the defaults) a row someone created meanwhile is left alone.
        var sql = expectedVersion is > 0
            ? $@"UPDATE scheduling_settings SET {SettingsSet("@")}
                 WHERE site_id = @site_id AND {RowVersion.Guard("scheduling_settings")}
                 RETURNING {SettingsSelectColumnsQualified}"
            : $@"INSERT INTO scheduling_settings (site_id, {string.Join(", ", SettingsColumns)})
                 VALUES (@site_id, {string.Join(", ", SettingsColumns.Select(c => "@" + c))})
                 ON CONFLICT (site_id) DO UPDATE SET {SettingsSet("EXCLUDED.")}
                 WHERE @{RowVersion.Parameter} IS NULL
                 RETURNING {SettingsSelectColumnsQualified}";

        var saved = await conn.QuerySingleOrDefaultAsync(sql,
            p =>
            {
                p.AddWithValue("site_id", settings.SiteId);
                p.AddWithValue("time_zone", settings.TimeZone);
                p.AddWithValue("working_hours_enabled", settings.WorkingHoursEnabled);
                p.AddWithValue("working_day_start", settings.WorkingDayStart.ToTimeSpan());
                p.AddWithValue("working_day_end", settings.WorkingDayEnd.ToTimeSpan());
                p.AddWithValue("weekends_enabled", settings.WeekendsEnabled);
                p.AddWithValue("public_holidays_enabled", settings.PublicHolidaysEnabled);
                p.AddNullable("public_holiday_region", settings.PublicHolidayRegion);
                p.AddExpectedVersion(expectedVersion);
            }, SchedulingMapper.MapSettingsFromReader, ct)
            ?? throw new PreconditionFailedException("scheduling settings");

        await RequestRepository.WriteRecalculatedSchedulesAsync(conn, tx, recalculated, ct);
        await tx.CommitAsync(ct);
        return saved;
    }

    public async Task<bool> DeleteSettingsAsync(Guid siteId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);
        return await conn.ExecuteAsync("DELETE FROM scheduling_settings WHERE site_id = @siteId",
            p => p.AddWithValue("siteId", siteId), ct) > 0;
    }
}
