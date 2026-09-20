using Api.Constants;
using Api.Helpers;
using Api.Models;
using Api.Services;

using static Api.Repositories.RequestSql;

namespace Api.Repositories;

/// <inheritdoc cref="IRequestScheduleReadRepository" />
public class RequestScheduleReadRepository : IRequestScheduleReadRepository
{
    // "Touches this site", answered by any one assigned resource being there. With a single space
    // per request this is exactly the old rt.key='space' AND res.home_site_id=@siteId test; with
    // several types it keeps a request visible at every site it actually occupies rather than
    // hiding it from all of them the moment one resource travels.
    //
    // Single consumer, so it lives here rather than in RequestSql. Binds @cancelled and @siteId.
    private static string AssignedAtSiteSql(string requestIdExpr) => $@"
        EXISTS (
            SELECT 1 FROM resource_assignments ra
            JOIN resources res ON res.id = ra.resource_id
            WHERE ra.request_id = {requestIdExpr}
              AND ra.assignment_status != @cancelled
              AND res.home_site_id = @siteId
        )";


    private readonly OrgContext _orgContext;
    private readonly IOrgDbConnectionFactory _connectionFactory;

    public RequestScheduleReadRepository(OrgContext orgContext, IOrgDbConnectionFactory connectionFactory)
    {
        _orgContext = orgContext;
        _connectionFactory = connectionFactory;
    }

    public async Task<List<RequestInfo>> GetScheduledBySiteAsync(Guid siteId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);

        return await conn.QueryListAsync($@"
            SELECT {SelectFromView}
            FROM v_requests_with_assignments
            WHERE scheduling_settings_apply = true
              AND start_ts IS NOT NULL AND end_ts IS NOT NULL
              AND {FullyAssignedSql("v_requests_with_assignments.id")}
              AND {AssignedAtSiteSql("v_requests_with_assignments.id")}",
            p =>
            {
                p.AddWithValue("siteId", siteId);
                p.AddWithValue("cancelled", AssignmentStatuses.Cancelled);
            },
            RequestMapper.MapFromReader,
            ct);
    }

    public Task<List<RequestInfo>> GetScheduledAsync(CancellationToken ct = default)
        => GetScheduledCoreAsync(null, null, ct);

    public Task<List<RequestInfo>> GetScheduledAsync(DateTime from, DateTime to, CancellationToken ct = default)
        => GetScheduledCoreAsync(from, to, ct);

    private async Task<List<RequestInfo>> GetScheduledCoreAsync(DateTime? from, DateTime? to, CancellationToken ct)
    {
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);

        // All fully assigned scheduled requests, tenant-wide. When a
        // [from,to] window is supplied (utilization grid) only bars overlapping it are returned;
        // without one (Conflicts page) the registry is all-time. No scheduling_settings_apply filter
        // — the registry mirrors what the grid surfaces for every scheduled bar.
        var windowed = from.HasValue && to.HasValue;
        var windowClause = windowed ? " AND start_ts < @to AND end_ts > @from" : "";
        var requests = await conn.QueryListAsync($@"
            SELECT {SelectFromView}
            FROM v_requests_with_assignments
            WHERE start_ts IS NOT NULL AND end_ts IS NOT NULL{windowClause}
              AND {FullyAssignedSql("v_requests_with_assignments.id")}",
            p =>
            {
                p.AddWithValue("cancelled", AssignmentStatuses.Cancelled);
                if (windowed)
                {
                    p.AddWithValue("from", from!.Value);
                    p.AddWithValue("to", to!.Value);
                }
            },
            RequestMapper.MapFromReader,
            ct);

        await LoadRequirementsForRequests(requests, conn, ct);

        return requests;
    }

    public async Task<List<ScheduledRequestLite>> GetScheduledLiteAsync(DateTime from, DateTime to, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);

        // Lightweight projection of the windowed GetScheduledAsync row set: identical WHERE clause
        // (the view adds no row filter over requests), but a plain SELECT — no assignments
        // aggregation, no requirements hydration.
        return await conn.QueryListAsync($@"
            SELECT id, start_ts, site_id
            FROM requests r
            WHERE start_ts IS NOT NULL AND start_ts < @to AND end_ts > @from
              AND {FullyAssignedSql("r.id")}",
            p =>
            {
                p.AddWithValue("cancelled", AssignmentStatuses.Cancelled);
                p.AddWithValue("from", from);
                p.AddWithValue("to", to);
            },
            reader => new ScheduledRequestLite(
                reader.GetGuid("id"),
                reader.GetDateTime("start_ts"),
                reader.GetNullableGuid("site_id")),
            ct);
    }

    public async Task<List<RequestInfo>> GetScheduledBySiteWindowAsync(
        Guid siteId, DateTime from, DateTime to, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);

        // Scheduled requests for this site whose bar overlaps the half-open window [from,to):
        // start_ts < to AND end_ts > from — the same convention every other window query and
        // AssignmentOverlapIndex use, so a bar touching a boundary is in exactly one window.
        // A request belongs to the site if it is scoped to it (site_id) OR holds a resource that
        // lives there. The site_id arm makes a scheduled, unassigned request appear on its site's
        // calendar.
        var requests = await conn.QueryListAsync($@"
            SELECT {SelectFromView}
            FROM v_requests_with_assignments
            WHERE start_ts IS NOT NULL
              AND start_ts < @to AND end_ts > @from
              AND (
                site_id = @siteId
                OR {AssignedAtSiteSql("v_requests_with_assignments.id")}
              )",
            p =>
            {
                p.AddWithValue("siteId", siteId);
                p.AddWithValue("from", from);
                p.AddWithValue("to", to);
                p.AddWithValue("cancelled", AssignmentStatuses.Cancelled);
            },
            RequestMapper.MapFromReader,
            ct);

        await LoadRequirementsForRequests(requests, conn, ct);

        return requests;
    }

    public async Task<List<RequestInfo>> GetUnscheduledAsync(
        Guid? siteId = null, bool includeSiteNeutral = true, bool includeRequirements = false, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);

        // Only leaf requests are directly schedulable (see RequestService.UpdateAsync), so the
        // drag-to-schedule backlog excludes groups — their null start_ts is a derived state, not an
        // unscheduled one. Unscheduled leaf *children* of a group still surface (they're the units
        // you place); only the group nodes drop.
        //
        // Site scoping: when a site is given, return that site's backlog plus (by default) the
        // site-neutral rows, which are schedulable at any site and adopt a site once placed. A null
        // siteId keeps the tenant-wide backlog (used until a caller passes a site).
        var siteFilter = siteId is null
            ? ""
            : includeSiteNeutral
                ? "AND (site_id = @siteId OR site_id IS NULL) "
                : "AND site_id = @siteId ";

        var requests = await conn.QueryListAsync(
            $"SELECT {SelectFromView} FROM v_requests_with_assignments " +
            $"WHERE start_ts IS NULL AND planning_mode = '{PlanningModes.Leaf}' " +
            siteFilter +
            "ORDER BY parent_request_id NULLS FIRST, sort_order, created_at DESC",
            p =>
            {
                if (siteId is not null) p.AddWithValue("siteId", siteId.Value);
            },
            RequestMapper.MapFromReader,
            ct);

        if (includeRequirements)
            await LoadRequirementsForRequests(requests, conn, ct);

        return requests;
    }

    public async Task<List<RequestInfo>> GetPartiallyScheduledLeavesAsync(
        bool includeRequirements = false, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);

        // Leaf requests that carry a start_ts but are NOT fully scheduled — the exact complement of
        // GetUnscheduledAsync (start_ts IS NULL) among leaves. "Not fully scheduled" mirrors
        // RequestInfo.IsScheduled = start_ts && end_ts && every targeted resource type carrying a
        // non-cancelled assignment, so a timed leaf missing its end_ts OR one of those assignments
        // qualifies. These stay auto-schedulable and
        // would otherwise be invisible to the solver (they are excluded from both the unscheduled
        // backlog and the fixed-occupancy fetch, which filters IsScheduled).
        var requests = await conn.QueryListAsync(
            $@"SELECT {SelectFromView} FROM v_requests_with_assignments
               WHERE start_ts IS NOT NULL AND planning_mode = '{PlanningModes.Leaf}'
                 AND (
                   end_ts IS NULL
                   OR NOT ({FullyAssignedSql("v_requests_with_assignments.id")})
                 )
               ORDER BY parent_request_id NULLS FIRST, sort_order, created_at DESC",
            p =>
            {
                p.AddWithValue("cancelled", AssignmentStatuses.Cancelled);
            },
            RequestMapper.MapFromReader,
            ct);

        if (includeRequirements)
            await LoadRequirementsForRequests(requests, conn, ct);

        return requests;
    }
}
