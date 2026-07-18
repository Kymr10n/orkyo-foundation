using Api.Constants;
using Api.Models;
using Api.Models.Reporting;
using Api.Repositories;
using Npgsql;

namespace Api.Services.Reporting;

/// <summary>
/// Read-only reporting queries scoped to a single tenant DB.
/// Tenant is always passed explicitly — never inferred from client input.
/// </summary>
public sealed class ReportingQueryService : IReportingQueryService
{
    private readonly IDbConnectionFactory _db;

    public ReportingQueryService(IDbConnectionFactory db)
    {
        _db = db;
    }

    /// <summary>Resolves the shared paging + date-range defaulting applied to every reporting query.</summary>
    private static (ReportingPageRequest Paged, DateTime From, DateTime To) ResolveWindow(ReportingQuery query) =>
        (query.ToPageRequest(), query.From ?? DateTime.UtcNow.AddMonths(-1), query.To ?? DateTime.UtcNow);

    public async Task<ReportingResult<SpaceUtilizationRow>> GetSpaceUtilizationAsync(
        TenantContext tenant, ReportingQuery query, CancellationToken ct = default)
    {
        var (paged, from, to) = ResolveWindow(query);
        var periodHours = (to - from).TotalHours;

        await using var conn = _db.CreateTenantConnection(tenant);

        var totalCount = 0;
        var rows = await conn.QueryListAsync($@"
            SELECT
                si.name                                       AS site_name,
                r.name                                        AS space_name,
                rg.name                                       AS group_name,
                @from                                         AS period_start,
                @to                                           AS period_end,
                COALESCE(SUM(
                    EXTRACT(EPOCH FROM (
                        LEAST(ra.end_utc, @to) - GREATEST(ra.start_utc, @from)
                    )) / 3600
                ), 0)                                         AS allocated_hours,
                COUNT(DISTINCT ra.request_id)                 AS request_count,
                COUNT(*) OVER ()                              AS total_count
            FROM spaces s
            JOIN resources r ON r.id = s.id
            JOIN sites si ON si.id = s.site_id
            LEFT JOIN resource_group_members rgm ON rgm.resource_id = s.id
            LEFT JOIN resource_groups rg ON rg.id = rgm.resource_group_id
            LEFT JOIN resource_assignments ra
                   ON ra.resource_id = s.id
                  AND ra.start_utc < @to AND ra.end_utc > @from
                  AND ra.assignment_status != '{AssignmentStatuses.Cancelled}'
            GROUP BY si.name, s.id, r.name, rg.name
            ORDER BY si.name, r.name
            LIMIT @limit OFFSET @offset",
            p =>
            {
                p.AddWithValue("from", from);
                p.AddWithValue("to", to);
                p.AddWithValue("limit", paged.PageSize);
                p.AddWithValue("offset", paged.Offset);
            },
            reader =>
            {
                totalCount = reader.GetInt32(7);
                var allocated = reader.GetDouble(5);
                var utilPct = periodHours > 0 ? Math.Round(allocated / periodHours * 100, 1) : 0;
                return new SpaceUtilizationRow
                {
                    SiteName = reader.GetString(0),
                    SpaceName = reader.GetString(1),
                    SpaceGroupName = reader.IsDBNull(2) ? null : reader.GetString(2),
                    PeriodStartUtc = from,
                    PeriodEndUtc = to,
                    AvailableHours = periodHours,
                    AllocatedHours = Math.Round(allocated, 2),
                    UtilizationPercent = utilPct,
                    OverbookedHours = 0,
                    RequestCount = reader.GetInt32(6),
                };
            }, ct);

        return ReportingResult<SpaceUtilizationRow>.Create(rows, totalCount, paged, query.ToMetadata());
    }

    public async Task<ReportingResult<ResourceUtilizationRow>> GetResourceUtilizationAsync(
        TenantContext tenant, ReportingQuery query, CancellationToken ct = default)
    {
        var (paged, from, to) = ResolveWindow(query);
        var periodHours = (to - from).TotalHours;

        await using var conn = _db.CreateTenantConnection(tenant);

        var totalCount = 0;
        var rows = await conn.QueryListAsync($@"
            SELECT
                rt.key                                        AS resource_type,
                r.name                                        AS resource_name,
                rg.name                                       AS group_name,
                @from                                         AS period_start,
                @to                                           AS period_end,
                r.base_availability_percent,
                COALESCE(SUM(
                    EXTRACT(EPOCH FROM (
                        LEAST(ra.end_utc, @to) - GREATEST(ra.start_utc, @from)
                    )) / 3600
                ), 0)                                         AS allocated_hours,
                COUNT(DISTINCT ra.request_id)                 AS request_count,
                COUNT(*) OVER ()                              AS total_count
            FROM resources r
            JOIN resource_types rt ON rt.id = r.resource_type_id
            LEFT JOIN resource_group_members rgm ON rgm.resource_id = r.id
            LEFT JOIN resource_groups rg ON rg.id = rgm.resource_group_id
            LEFT JOIN resource_assignments ra
                   ON ra.resource_id = r.id
                  AND ra.start_utc < @to AND ra.end_utc > @from
                  AND ra.assignment_status != '{AssignmentStatuses.Cancelled}'
            GROUP BY rt.key, r.id, r.name, r.base_availability_percent, rg.name
            ORDER BY rt.key, r.name
            LIMIT @limit OFFSET @offset",
            p =>
            {
                p.AddWithValue("from", from);
                p.AddWithValue("to", to);
                p.AddWithValue("limit", paged.PageSize);
                p.AddWithValue("offset", paged.Offset);
            },
            reader =>
            {
                totalCount = reader.GetInt32(8);
                var availPct = reader.IsDBNull(5) ? 100.0 : reader.GetDouble(5);
                var effectiveHours = periodHours * availPct / 100.0;
                var allocated = reader.GetDouble(6);
                var utilPct = effectiveHours > 0 ? Math.Round(allocated / effectiveHours * 100, 1) : 0;
                return new ResourceUtilizationRow
                {
                    ResourceType = reader.GetString(0),
                    ResourceName = reader.GetString(1),
                    ResourceGroupName = reader.IsDBNull(2) ? null : reader.GetString(2),
                    PeriodStartUtc = from,
                    PeriodEndUtc = to,
                    AvailableHours = Math.Round(effectiveHours, 2),
                    AllocatedHours = Math.Round(allocated, 2),
                    UtilizationPercent = utilPct,
                    OverbookedHours = Math.Max(0, Math.Round(allocated - effectiveHours, 2)),
                };
            }, ct);

        return ReportingResult<ResourceUtilizationRow>.Create(rows, totalCount, paged, query.ToMetadata());
    }

    public async Task<ReportingResult<AllocationRow>> GetAllocationsAsync(
        TenantContext tenant, ReportingQuery query, CancellationToken ct = default)
    {
        var (paged, from, to) = ResolveWindow(query);

        await using var conn = _db.CreateTenantConnection(tenant);

        var updatedSinceFilter = query.UpdatedSince.HasValue
            ? "AND ra.updated_at >= @updatedSince"
            : "";

        void BindFilters(NpgsqlParameterCollection p)
        {
            p.AddWithValue("from", from);
            p.AddWithValue("to", to);
            if (query.UpdatedSince.HasValue)
                p.AddWithValue("updatedSince", query.UpdatedSince.Value);
        }

        var totalCount = (int)await conn.ExecuteScalarAsync<long>($@"
            SELECT COUNT(*)
            FROM resource_assignments ra
            JOIN requests req ON req.id = ra.request_id
            JOIN resources r ON r.id = ra.resource_id
            JOIN resource_types rt ON rt.id = r.resource_type_id
            WHERE ra.start_utc < @to AND ra.end_utc > @from
              AND ra.assignment_status != '{AssignmentStatuses.Cancelled}'
              {updatedSinceFilter}", BindFilters, ct);

        var rows = await conn.QueryListAsync($@"
            SELECT
                ra.id                                         AS allocation_id,
                req.request_item_id                           AS request_reference,
                req.name                                      AS request_title,
                rt.key                                        AS resource_type,
                r.name                                        AS resource_name,
                si.name                                       AS site_name,
                ra.start_utc,
                ra.end_utc,
                EXTRACT(EPOCH FROM (ra.end_utc - ra.start_utc)) / 3600 AS duration_hours,
                req.status,
                ra.updated_at
            FROM resource_assignments ra
            JOIN requests req ON req.id = ra.request_id
            JOIN resources r ON r.id = ra.resource_id
            JOIN resource_types rt ON rt.id = r.resource_type_id
            LEFT JOIN spaces sp ON sp.id = r.id
            LEFT JOIN sites si ON si.id = sp.site_id
            WHERE ra.start_utc < @to AND ra.end_utc > @from
              AND ra.assignment_status != '{AssignmentStatuses.Cancelled}'
              {updatedSinceFilter}
            ORDER BY ra.start_utc DESC
            LIMIT @limit OFFSET @offset",
            p =>
            {
                BindFilters(p);
                p.AddWithValue("limit", paged.PageSize);
                p.AddWithValue("offset", paged.Offset);
            },
            reader => new AllocationRow
            {
                AllocationId = $"rpt_alloc_{reader.GetGuid(0):N}",
                RequestReference = reader.IsDBNull(1) ? null : reader.GetString(1),
                RequestTitle = reader.GetString(2),
                ResourceType = reader.GetString(3),
                ResourceName = reader.GetString(4),
                SiteName = reader.IsDBNull(5) ? null : reader.GetString(5),
                StartsAtUtc = reader.GetDateTime(6),
                EndsAtUtc = reader.GetDateTime(7),
                DurationHours = Math.Round(reader.GetDouble(8), 2),
                Status = reader.GetString(9),
                UpdatedAtUtc = reader.GetDateTime(10),
            }, ct);

        return ReportingResult<AllocationRow>.Create(rows, totalCount, paged, query.ToMetadata());
    }

    public async Task<ReportingResult<RequestThroughputRow>> GetRequestThroughputAsync(
        TenantContext tenant, ReportingQuery query, CancellationToken ct = default)
    {
        var (paged, from, to) = ResolveWindow(query);

        await using var conn = _db.CreateTenantConnection(tenant);

        // Single aggregate row (always returned, even with zero requests).
        var row = await conn.QuerySingleOrDefaultAsync($@"
            SELECT
                COUNT(*) FILTER (WHERE created_at >= @from AND created_at < @to)          AS created,
                COUNT(*) FILTER (WHERE status = '{RequestStatuses.InProgress}'
                                   AND updated_at >= @from AND updated_at < @to)          AS in_progress,
                COUNT(*) FILTER (WHERE status = '{RequestStatuses.Done}'
                                   AND updated_at >= @from AND updated_at < @to)          AS done,
                COUNT(*) FILTER (WHERE status = '{RequestStatuses.Cancelled}'
                                   AND updated_at >= @from AND updated_at < @to)          AS cancelled,
                AVG(EXTRACT(EPOCH FROM (start_ts - created_at)) / 3600)
                    FILTER (WHERE start_ts IS NOT NULL AND status != '{RequestStatuses.New}'
                              AND created_at >= @from AND created_at < @to)              AS avg_lead_hours
            FROM requests",
            p =>
            {
                p.AddWithValue("from", from);
                p.AddWithValue("to", to);
            },
            reader => new RequestThroughputRow
            {
                PeriodStartUtc = from,
                PeriodEndUtc = to,
                CreatedCount = reader.GetInt32(0),
                InProgressCount = reader.GetInt32(1),
                CompletedCount = reader.GetInt32(2),
                CancelledCount = reader.GetInt32(3),
                AverageLeadTimeHours = reader.IsDBNull(4) ? null : Math.Round(reader.GetDouble(4), 1),
            }, ct);

        return ReportingResult<RequestThroughputRow>.Create(row is null ? [] : [row], row is null ? 0 : 1, paged, query.ToMetadata());
    }

    public async Task<ReportingResult<ConflictRow>> GetConflictsAsync(
        TenantContext tenant, ReportingQuery query, CancellationToken ct = default)
    {
        var (paged, from, to) = ResolveWindow(query);

        await using var conn = _db.CreateTenantConnection(tenant);

        // Detect overbooking: same resource, overlapping non-cancelled assignments
        var totalCount = 0;
        var rows = await conn.QueryListAsync($@"
            SELECT
                rt.key                                        AS resource_type,
                res.name                                      AS resource_name,
                req.request_item_id                           AS request_ref,
                GREATEST(ra1.start_utc, ra2.start_utc)        AS overlap_start,
                LEAST(ra1.end_utc, ra2.end_utc)               AS overlap_end,
                EXTRACT(EPOCH FROM (
                    LEAST(ra1.end_utc, ra2.end_utc) -
                    GREATEST(ra1.start_utc, ra2.start_utc)
                )) / 3600                                     AS overlap_hours,
                COUNT(*) OVER ()                              AS total_count
            FROM resource_assignments ra1
            JOIN LATERAL (
                SELECT ra2.id, ra2.start_utc, ra2.end_utc
                FROM resource_assignments ra2
                WHERE ra2.resource_id = ra1.resource_id
                  AND ra2.assignment_status != '{AssignmentStatuses.Cancelled}'
                  AND ra2.start_utc < ra1.end_utc AND ra2.end_utc > ra1.start_utc
                  AND ra2.id > ra1.id
            ) ra2 ON true
            JOIN resources res ON res.id = ra1.resource_id
            JOIN resource_types rt ON rt.id = res.resource_type_id
            JOIN requests req ON req.id = ra1.request_id
            WHERE ra1.assignment_status != '{AssignmentStatuses.Cancelled}'
              AND ra1.start_utc < @to AND ra1.end_utc > @from
            ORDER BY overlap_start DESC
            LIMIT @limit OFFSET @offset",
            p =>
            {
                p.AddWithValue("from", from);
                p.AddWithValue("to", to);
                p.AddWithValue("limit", paged.PageSize);
                p.AddWithValue("offset", paged.Offset);
            },
            reader =>
            {
                totalCount = reader.GetInt32(6);
                return new ConflictRow
                {
                    ConflictType = "Overbooking",
                    ResourceType = reader.GetString(0),
                    ResourceName = reader.GetString(1),
                    RequestReference = reader.IsDBNull(2) ? null : reader.GetString(2),
                    StartsAtUtc = reader.GetDateTime(3),
                    EndsAtUtc = reader.GetDateTime(4),
                    OverbookedHours = Math.Round(reader.GetDouble(5), 2),
                };
            }, ct);

        return ReportingResult<ConflictRow>.Create(rows, totalCount, paged, query.ToMetadata());
    }

    public async Task<ReportingResult<AbsenceRow>> GetAbsencesAsync(
        TenantContext tenant, ReportingQuery query, bool peopleLevelEnabled,
        CancellationToken ct = default)
    {
        var (paged, from, to) = ResolveWindow(query);

        await using var conn = _db.CreateTenantConnection(tenant);

        void BindWindow(NpgsqlParameterCollection p)
        {
            p.AddWithValue("from", from);
            p.AddWithValue("to", to);
        }

        var totalCount = (int)await conn.ExecuteScalarAsync<long>(@"
            SELECT COUNT(*)
            FROM resource_absences ra
            JOIN resources r ON r.id = ra.resource_id
            WHERE ra.start_ts < @to AND ra.end_ts > @from
              AND ra.enabled = true", BindWindow, ct);

        // When people-level is disabled, mask resource names and aggregate by group
        var nameExpr = peopleLevelEnabled ? "r.name" : "NULL::text";

        var rows = await conn.QueryListAsync($@"
            SELECT
                rt.key                                        AS resource_type,
                {nameExpr}                                    AS resource_name,
                rg.name                                       AS group_name,
                ra.absence_type                               AS absence_category,
                ra.start_ts,
                ra.end_ts,
                EXTRACT(EPOCH FROM (
                    LEAST(ra.end_ts, @to) - GREATEST(ra.start_ts, @from)
                )) / 3600                                     AS absence_hours
            FROM resource_absences ra
            JOIN resources r ON r.id = ra.resource_id
            JOIN resource_types rt ON rt.id = r.resource_type_id
            LEFT JOIN resource_group_members rgm ON rgm.resource_id = r.id
            LEFT JOIN resource_groups rg ON rg.id = rgm.resource_group_id
            WHERE ra.start_ts < @to AND ra.end_ts > @from
              AND ra.enabled = true
            ORDER BY ra.start_ts DESC
            LIMIT @limit OFFSET @offset",
            p =>
            {
                BindWindow(p);
                p.AddWithValue("limit", paged.PageSize);
                p.AddWithValue("offset", paged.Offset);
            },
            reader => new AbsenceRow
            {
                ResourceType = reader.GetString(0),
                ResourceName = reader.IsDBNull(1) ? null : reader.GetString(1),
                ResourceGroupName = reader.IsDBNull(2) ? null : reader.GetString(2),
                AbsenceCategory = reader.IsDBNull(3) ? null : reader.GetString(3),
                StartsAtUtc = reader.GetDateTime(4),
                EndsAtUtc = reader.GetDateTime(5),
                AbsenceHours = Math.Round(reader.GetDouble(6), 2),
            }, ct);

        return ReportingResult<AbsenceRow>.Create(rows, totalCount, paged, query.ToMetadata());
    }

    public async Task<ReportingResult<CapacityVsDemandRow>> GetCapacityVsDemandAsync(
        TenantContext tenant, ReportingQuery query, CancellationToken ct = default)
    {
        var (paged, from, to) = ResolveWindow(query);

        await using var conn = _db.CreateTenantConnection(tenant);

        // Group by resource type + group, compute capacity from base_availability_percent
        var totalCount = (int)await conn.ExecuteScalarAsync<long>(@"
            SELECT COUNT(DISTINCT CONCAT(rt.key, '|', COALESCE(rg.name, '')))
            FROM resources r
            JOIN resource_types rt ON rt.id = r.resource_type_id
            LEFT JOIN resource_group_members rgm ON rgm.resource_id = r.id
            LEFT JOIN resource_groups rg ON rg.id = rgm.resource_group_id", null, ct);

        var periodHours = (to - from).TotalHours;

        // Each side is pre-aggregated in its own CTE before joining, so a resource's availability is
        // counted once per group it belongs to — NOT once per (group × assignment), which the previous
        // single-statement join did (Cartesian-inflating available_hours). allocated_hours and
        // demand_hours are unchanged: they were already one row per (resource, group, assignment).
        var rows = await conn.QueryListAsync($@"
            WITH resource_groups_expanded AS (
                -- one row per (resource, group); group_name NULL when the resource is in no group
                SELECT r.id                       AS resource_id,
                       r.resource_type_id,
                       r.base_availability_percent,
                       rg.name                     AS group_name
                FROM resources r
                LEFT JOIN resource_group_members rgm ON rgm.resource_id = r.id
                LEFT JOIN resource_groups rg ON rg.id = rgm.resource_group_id
            ),
            capacity AS (
                SELECT rt.key AS resource_type, rge.group_name,
                       SUM(COALESCE(rge.base_availability_percent, 100.0) / 100.0 * @periodHours) AS available_hours
                FROM resource_groups_expanded rge
                JOIN resource_types rt ON rt.id = rge.resource_type_id
                GROUP BY rt.key, rge.group_name
            ),
            allocated AS (
                SELECT rt.key AS resource_type, rge.group_name,
                       SUM(EXTRACT(EPOCH FROM (
                           LEAST(ra.end_utc, @to) - GREATEST(ra.start_utc, @from)
                       )) / 3600) AS allocated_hours
                FROM resource_groups_expanded rge
                JOIN resource_types rt ON rt.id = rge.resource_type_id
                JOIN resource_assignments ra
                       ON ra.resource_id = rge.resource_id
                      AND ra.assignment_status != '{AssignmentStatuses.Cancelled}'
                      AND ra.start_utc < @to AND ra.end_utc > @from
                GROUP BY rt.key, rge.group_name
            ),
            demand AS (
                SELECT rt.key AS resource_type, rge.group_name,
                       SUM(EXTRACT(EPOCH FROM (req.end_ts - req.start_ts)) / 3600) AS demand_hours
                FROM resource_groups_expanded rge
                JOIN resource_types rt ON rt.id = rge.resource_type_id
                JOIN resource_assignments ra
                       ON ra.resource_id = rge.resource_id
                      AND ra.assignment_status != '{AssignmentStatuses.Cancelled}'
                      AND ra.start_utc < @to AND ra.end_utc > @from
                JOIN requests req
                       ON req.id = ra.request_id
                      AND req.start_ts IS NOT NULL AND req.end_ts IS NOT NULL
                      AND req.start_ts >= @from AND req.end_ts <= @to
                GROUP BY rt.key, rge.group_name
            )
            SELECT
                c.resource_type                               AS resource_type,
                c.group_name                                  AS group_name,
                @from                                         AS period_start,
                @to                                           AS period_end,
                c.available_hours                             AS available_hours,
                COALESCE(a.allocated_hours, 0)                AS allocated_hours,
                COALESCE(d.demand_hours, 0)                   AS demand_hours
            FROM capacity c
            LEFT JOIN allocated a
                   ON a.resource_type = c.resource_type
                  AND a.group_name IS NOT DISTINCT FROM c.group_name
            LEFT JOIN demand d
                   ON d.resource_type = c.resource_type
                  AND d.group_name IS NOT DISTINCT FROM c.group_name
            ORDER BY c.resource_type, c.group_name
            LIMIT @limit OFFSET @offset",
            p =>
            {
                p.AddWithValue("from", from);
                p.AddWithValue("to", to);
                p.AddWithValue("periodHours", periodHours);
                p.AddWithValue("limit", paged.PageSize);
                p.AddWithValue("offset", paged.Offset);
            },
            reader =>
            {
                var available = reader.GetDouble(4);
                var allocated = reader.GetDouble(5);
                var demand = reader.GetDouble(6);
                return new CapacityVsDemandRow
                {
                    ResourceType = reader.GetString(0),
                    ResourceGroupName = reader.IsDBNull(1) ? null : reader.GetString(1),
                    PeriodStartUtc = from,
                    PeriodEndUtc = to,
                    AvailableHours = Math.Round(available, 2),
                    DemandHours = Math.Round(demand, 2),
                    AllocatedHours = Math.Round(allocated, 2),
                    UnallocatedDemandHours = Math.Round(Math.Max(0, demand - allocated), 2),
                    CapacityGapHours = Math.Round(Math.Max(0, demand - available), 2),
                };
            }, ct);

        return ReportingResult<CapacityVsDemandRow>.Create(rows, totalCount, paged, query.ToMetadata());
    }
}
