using System.Data;
using System.Text.Json;
using Api.Constants;
using Api.Helpers;
using Api.Models;
using Api.Services;
using Npgsql;

namespace Api.Repositories;

public class RequestRepository : IRequestRepository
{
    // ── The scheduled predicate ───────────────────────────────────────────────────
    // "Scheduled" is a domain rule, not a query detail: a request is scheduled when every
    // resource type it targets has a non-cancelled assignment. It was previously written out
    // in full at each read site and once more inside analytics_request_summary_v, every copy
    // hard-coding rt.key = 'space'. One copy drifting from the others is a silently wrong
    // answer in the conflicts registry or the utilization grid, so it lives here once.
    //
    // The EXISTS guard is load-bearing. Without it a request targeting nothing satisfies the
    // NOT EXISTS vacuously and reports itself scheduled while holding no resource at all.
    private static string FullyAssignedSql(string requestIdExpr) => $@"
        EXISTS (SELECT 1 FROM request_target_resource_types t
                 WHERE t.request_id = {requestIdExpr})
        AND NOT EXISTS (
            SELECT 1 FROM request_target_resource_types t
             WHERE t.request_id = {requestIdExpr}
               AND NOT EXISTS (
                   SELECT 1 FROM resource_assignments ra
                   JOIN resources res ON res.id = ra.resource_id
                   WHERE ra.request_id = {requestIdExpr}
                     AND res.resource_type_id = t.resource_type_id
                     AND ra.assignment_status != @cancelled
               )
        )";

    // "Touches this site", answered by any one assigned resource being there. With a single
    // space per request this is exactly the old rt.key='space' AND res.home_site_id=@siteId
    // test; with several types it keeps a request visible at every site it actually occupies
    // rather than hiding it from all of them the moment one resource travels.
    private static string AssignedAtSiteSql(string requestIdExpr) => $@"
        EXISTS (
            SELECT 1 FROM resource_assignments ra
            JOIN resources res ON res.id = ra.resource_id
            WHERE ra.request_id = {requestIdExpr}
              AND ra.assignment_status != @cancelled
              AND res.home_site_id = @siteId
        )";

    // Columns selected from the view.
    private const string SelectFromView =
        @"id, name, description, parent_request_id, planning_mode, sort_order,
          site_id,
          target_resource_type_keys,
          request_item_id, icon,
          start_ts, end_ts, earliest_start_ts, latest_end_ts,
          minimal_duration_value, minimal_duration_unit,
          actual_duration_value, actual_duration_unit,
          status, scheduling_settings_apply, created_at, updated_at, assignments,
          predecessor_logic, predecessor_logic_k";

    private readonly OrgContext _orgContext;
    private readonly IOrgDbConnectionFactory _connectionFactory;

    public RequestRepository(OrgContext orgContext, IOrgDbConnectionFactory connectionFactory)
    {
        _orgContext = orgContext;
        _connectionFactory = connectionFactory;
    }

    public async Task<List<RequestInfo>> GetAllAsync(bool includeRequirements = false, CancellationToken ct = default)
    {
        await using var db = _connectionFactory.CreateOrgConnection(_orgContext);

        var requests = await db.QueryListAsync(
            $"SELECT {SelectFromView} FROM v_requests_with_assignments ORDER BY parent_request_id NULLS FIRST, sort_order, created_at DESC",
            bind: null,
            RequestMapper.MapFromReader,
            ct);

        if (includeRequirements && requests.Count > 0)
            await LoadRequirementsForRequests(requests, db, ct);

        return requests;
    }

    public async Task<PagedResult<RequestInfo>> GetAllAsync(PageRequest page, bool includeRequirements = false, CancellationToken ct = default)
    {
        await using var db = _connectionFactory.CreateOrgConnection(_orgContext);

        var result = await db.QueryPagedAsync(
            page,
            countSql: "SELECT COUNT(*) FROM v_requests_with_assignments",
            querySql: $"SELECT {SelectFromView} FROM v_requests_with_assignments ORDER BY parent_request_id NULLS FIRST, sort_order, created_at DESC LIMIT @limit OFFSET @offset",
            bind: null,
            map: RequestMapper.MapFromReader,
            ct: ct);

        if (includeRequirements && result.Items.Count > 0)
        {
            var items = result.Items.ToList();
            await LoadRequirementsForRequests(items, db, ct);
            return result with { Items = items };
        }

        return result;
    }

    public async Task<List<RequestInfo>> SearchAsync(
        string? nameContains, bool? scheduled, int limit, RequestSort sort = RequestSort.Default,
        CancellationToken ct = default)
    {
        await using var db = _connectionFactory.CreateOrgConnection(_orgContext);

        // Both filters are optional and applied in SQL, so a caller asking for a handful of
        // rows reads a handful rather than the whole table. "Scheduled" reuses
        // FullyAssignedSql deliberately: RequestInfo.IsScheduled is the same rule, and that
        // rule is already written in three places — this must not become a fourth.
        return await db.QueryListAsync($@"
            SELECT {SelectFromView}
            FROM v_requests_with_assignments
            WHERE (@query::text IS NULL OR name ILIKE @query)
              AND (@scheduled::boolean IS NULL
                   OR (start_ts IS NOT NULL AND end_ts IS NOT NULL
                       AND {FullyAssignedSql("v_requests_with_assignments.id")}) = @scheduled)
            ORDER BY {OrderBySql(sort)}
            LIMIT @limit",
            p =>
            {
                p.AddWithValue("query", string.IsNullOrWhiteSpace(nameContains)
                    ? DBNull.Value
                    : $"%{EscapeLike(nameContains)}%");
                p.AddWithValue("scheduled", scheduled.HasValue ? scheduled.Value : DBNull.Value);
                p.AddWithValue("limit", limit);
                p.AddWithValue("cancelled", AssignmentStatuses.Cancelled);
            },
            RequestMapper.MapFromReader,
            ct);
    }

    /// <summary>
    /// The ORDER BY for a sort, chosen from a fixed set — the caller names a case, never
    /// SQL, so no user or model input reaches the statement.
    ///
    /// <see cref="RequestSort.LongestDuration"/> ranks by the scheduled window, which the
    /// database can measure directly. It deliberately does not rank unscheduled requests by
    /// their minimal duration: that conversion is a business rule owned by
    /// <see cref="SchedulingEngine.DurationToMinutes"/>, and re-expressing it in SQL would
    /// make it two rules that can drift. Rows with no window sort last.
    /// </summary>
    private static string OrderBySql(RequestSort sort) => sort switch
    {
        RequestSort.LongestDuration => "(end_ts - start_ts) DESC NULLS LAST, created_at DESC",
        RequestSort.EarliestStart => "start_ts ASC NULLS LAST, created_at DESC",
        RequestSort.Name => "name ASC",
        _ => "parent_request_id NULLS FIRST, sort_order, created_at DESC",
    };

    /// <summary>
    /// Neutralises LIKE wildcards in user text so a name containing % or _ matches literally.
    /// The backslash is the default ILIKE escape character in Postgres.
    /// </summary>
    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    public async Task<List<RequestInfo>> GetScheduledBySiteAsync(Guid siteId, CancellationToken ct = default)
    {
        await using var db = _connectionFactory.CreateOrgConnection(_orgContext);

        return await db.QueryListAsync($@"
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
        await using var db = _connectionFactory.CreateOrgConnection(_orgContext);

        // All fully assigned scheduled requests, tenant-wide. When a
        // [from,to] window is supplied (utilization grid) only bars overlapping it are returned;
        // without one (Conflicts page) the registry is all-time. No scheduling_settings_apply filter
        // — the registry mirrors what the grid surfaces for every scheduled bar.
        var windowed = from.HasValue && to.HasValue;
        var windowClause = windowed ? " AND start_ts < @to AND end_ts > @from" : "";
        var requests = await db.QueryListAsync($@"
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

        if (requests.Count > 0)
            await LoadRequirementsForRequests(requests, db, ct);

        return requests;
    }

    public async Task<List<ScheduledRequestLite>> GetScheduledLiteAsync(DateTime from, DateTime to, CancellationToken ct = default)
    {
        await using var db = _connectionFactory.CreateOrgConnection(_orgContext);

        // Lightweight projection of the windowed GetScheduledAsync row set: identical WHERE clause
        // (the view adds no row filter over requests), but a plain SELECT — no assignments
        // aggregation, no requirements hydration.
        return await db.QueryListAsync($@"
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
                reader.GetGuid(0),
                reader.GetDateTime(1),
                reader.IsDBNull(2) ? null : reader.GetGuid(2)),
            ct);
    }

    public async Task<List<RequestInfo>> GetScheduledBySiteWindowAsync(
        Guid siteId, DateTime from, DateTime to, CancellationToken ct = default)
    {
        await using var db = _connectionFactory.CreateOrgConnection(_orgContext);

        // Scheduled requests for this site whose bar overlaps the half-open window [from,to):
        // start_ts < to AND end_ts > from — the same convention every other window query and
        // AssignmentOverlapIndex use, so a bar touching a boundary is in exactly one window.
        // A request belongs to the site if it is scoped to it (site_id) OR holds a resource that
        // lives there. The site_id arm makes a scheduled, unassigned request appear on its site's
        // calendar.
        var requests = await db.QueryListAsync($@"
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

        if (requests.Count > 0)
            await LoadRequirementsForRequests(requests, db, ct);

        return requests;
    }

    public async Task<List<RequestInfo>> GetUnscheduledAsync(
        Guid? siteId = null, bool includeSiteNeutral = true, bool includeRequirements = false, CancellationToken ct = default)
    {
        await using var db = _connectionFactory.CreateOrgConnection(_orgContext);

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

        var requests = await db.QueryListAsync(
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

        if (includeRequirements && requests.Count > 0)
            await LoadRequirementsForRequests(requests, db, ct);

        return requests;
    }

    public async Task<List<RequestInfo>> GetPartiallyScheduledLeavesAsync(
        bool includeRequirements = false, CancellationToken ct = default)
    {
        await using var db = _connectionFactory.CreateOrgConnection(_orgContext);

        // Leaf requests that carry a start_ts but are NOT fully scheduled — the exact complement of
        // GetUnscheduledAsync (start_ts IS NULL) among leaves. "Not fully scheduled" mirrors
        // RequestInfo.IsScheduled = start_ts && end_ts && every targeted resource type carrying a
        // non-cancelled assignment, so a timed leaf missing its end_ts OR one of those assignments
        // qualifies. These stay auto-schedulable and
        // would otherwise be invisible to the solver (they are excluded from both the unscheduled
        // backlog and the fixed-occupancy fetch, which filters IsScheduled).
        var requests = await db.QueryListAsync(
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

        if (includeRequirements && requests.Count > 0)
            await LoadRequirementsForRequests(requests, db, ct);

        return requests;
    }

    public async Task<RequestInfo?> GetByIdAsync(Guid id, bool includeRequirements = true, CancellationToken ct = default)
    {
        await using var db = _connectionFactory.CreateOrgConnection(_orgContext);

        var request = await db.QuerySingleOrDefaultAsync<RequestInfo?>(
            $"SELECT {SelectFromView} FROM v_requests_with_assignments WHERE id = @id",
            p => p.AddWithValue("id", id),
            RequestMapper.MapFromReader,
            ct);

        if (request is null)
            return null;

        if (includeRequirements)
            request = request with { Requirements = await LoadRequirements(id, db, ct) };

        return request;
    }

    public async Task<List<RequestInfo>> GetByIdsAsync(
        IReadOnlyList<Guid> ids, bool includeRequirements = true, CancellationToken ct = default)
    {
        if (ids.Count == 0) return [];

        await using var db = _connectionFactory.CreateOrgConnection(_orgContext);

        var requests = await db.QueryListAsync(
            $"SELECT {SelectFromView} FROM v_requests_with_assignments WHERE id = ANY(@ids)",
            p => p.AddWithValue("ids", ids.ToArray()),
            RequestMapper.MapFromReader,
            ct);

        if (includeRequirements && requests.Count > 0)
            await LoadRequirementsForRequests(requests, db, ct);

        return requests;
    }

    private async Task<RequestInfo> ReadByIdAsync(NpgsqlConnection db, Guid id, CancellationToken ct = default)
    {
        var cmd = new NpgsqlCommand(
            $"SELECT {SelectFromView} FROM v_requests_with_assignments WHERE id = @id",
            db);
        cmd.Parameters.AddWithValue("id", id);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return RequestMapper.MapFromReader(reader);
    }

    public async Task<RequestInfo> CreateAsync(CreateRequestRequest request, CancellationToken ct = default)
    {
        await using var db = _connectionFactory.CreateOrgConnection(_orgContext);
        await db.OpenAsync(ct);

        // Validate resource ids if provided (every resource must exist)
        foreach (var resourceId in request.ResourceIds ?? [])
        {
            if (!await db.ExistsAsync("resources", resourceId, ct))
                throw new ArgumentException($"Invalid resource id: {resourceId} does not exist");
        }

        // Validate site_id if provided (site must exist)
        if (request.SiteId.HasValue
            && !await db.ExistsAsync("sites", request.SiteId.Value, ct))
        {
            throw new ArgumentException("Invalid site_id: site does not exist");
        }

        // Implicit site-on-schedule: a request created directly into a resource (no explicit site)
        // adopts that resource's home site. Mirrors UpdateScheduleAsync so every creation route agrees.
        var effectiveSiteId = request.SiteId;
        if (effectiveSiteId is null && request.ResourceIds is { Count: > 0 } siteBearers)
        {
            effectiveSiteId = await db.QuerySingleOrDefaultAsync<Guid?>(
                // Only an immovable resource dictates the request's site. Reading home_site_id
                // for any resource would make scheduling onto a person drag the request to that
                // person's home office — the spaces table used to prevent that by simply having
                // no row for people. With several resources the immovable ones all sit at the
                // same site by construction, so any one of them answers.
                @"SELECT home_site_id FROM resources
                   WHERE id = ANY(@ids) AND NOT cross_site_allowed AND home_site_id IS NOT NULL
                   LIMIT 1",
                p => p.AddWithValue("ids", siteBearers.ToArray()),
                r => r.IsDBNull(0) ? null : r.GetGuid(0), ct);
        }

        await using var transaction = await db.BeginTransactionAsync(ct);

        try
        {
            var cmd = new NpgsqlCommand(
                $@"INSERT INTO requests (name, description, parent_request_id, planning_mode, sort_order,
                                        site_id, request_item_id, icon,
                                        start_ts, end_ts, earliest_start_ts, latest_end_ts,
                                        minimal_duration_value, minimal_duration_unit,
                                        actual_duration_value, actual_duration_unit,
                                        status, scheduling_settings_apply)
                   VALUES (@name, @description, @parent_request_id, @planning_mode, @sort_order,
                           @site_id, @request_item_id, @icon,
                           @start_ts, @end_ts, @earliest_start_ts, @latest_end_ts,
                           @minimal_duration_value, @minimal_duration_unit,
                           @actual_duration_value, @actual_duration_unit,
                           @status, @scheduling_settings_apply)
                   RETURNING id",
                db, transaction);

            cmd.Parameters.AddWithValue("name", request.Name);
            cmd.Parameters.AddNullable("description", request.Description);
            cmd.Parameters.AddNullable("parent_request_id", request.ParentRequestId);
            cmd.Parameters.AddWithValue("planning_mode", EnumMapper.ToDbValue(request.PlanningMode));
            cmd.Parameters.AddWithValue("sort_order", request.SortOrder);
            cmd.Parameters.AddNullable("site_id", effectiveSiteId);
            cmd.Parameters.AddNullable("request_item_id", request.RequestItemId);
            cmd.Parameters.AddNullable("icon", request.Icon);
            cmd.Parameters.AddNullable("start_ts", request.StartTs);
            cmd.Parameters.AddNullable("end_ts", request.EndTs);
            cmd.Parameters.AddNullable("earliest_start_ts", request.EarliestStartTs);
            cmd.Parameters.AddNullable("latest_end_ts", request.LatestEndTs);
            cmd.Parameters.AddWithValue("minimal_duration_value", request.MinimalDurationValue);
            cmd.Parameters.AddWithValue("minimal_duration_unit", EnumMapper.ToDbValue(request.MinimalDurationUnit));
            cmd.Parameters.AddNullable("actual_duration_value", request.ActualDurationValue);
            cmd.Parameters.AddWithValue("actual_duration_unit", request.ActualDurationUnit.HasValue
                ? EnumMapper.ToDbValue(request.ActualDurationUnit.Value)
                : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("status", EnumMapper.ToDbValue(request.Status));
            cmd.Parameters.AddWithValue("scheduling_settings_apply", request.SchedulingSettingsApply);

            using var reader = await cmd.ExecuteReaderAsync(ct);
            await reader.ReadAsync(ct);
            var requestId = reader.GetGuid(0);
            reader.Close();

            // Targets first: they are what the assignment write validates against.
            //
            // A caller that says nothing means "this needs a place" — ONE place. Targeting every
            // placeable type would mean needing a room *and* a mill *and* a drill, which no
            // request wants and nothing could satisfy. So: the tenant's single placeable type,
            // preferring `space` where it still exists so the historical meaning is unchanged,
            // and falling to the lowest key otherwise. Stated explicitly rather than left empty —
            // an empty target list is a real state (a request needing no resource) and must not be
            // reachable by omission.
            var targets = request.TargetResourceTypeKeys
                ?? await db.QueryListAsync(
                    "SELECT key FROM resource_types WHERE has_geometry AND is_active "
                    + "ORDER BY (key = 'space') DESC, key LIMIT 1",
                    null, r => r.GetString(0), ct);
            // With no types activated yet the fallback finds nothing — refuse rather than
            // write the empty list the comment above rules out.
            if (request.TargetResourceTypeKeys is null && targets.Count == 0)
                throw new ArgumentException(
                    "No active placeable resource type exists. Activate one under Configuration, "
                    + "or specify target resource types explicitly.");
            await WriteTargetResourceTypesAsync(db, transaction, requestId, targets, ct);

            // Create resource assignment if a resource + time window was provided.
            if (request.ResourceIds is { Count: > 0 } newResources
                && request.StartTs.HasValue && request.EndTs.HasValue)
            {
                await WriteRequestResourcesAsync(
                    db, transaction, requestId, newResources,
                    request.StartTs.Value, request.EndTs.Value, ct);
            }

            if (request.Requirements is { Count: > 0 })
            {
                await CreateRequirements(requestId, request.Requirements, db, transaction, ct);
            }

            await transaction.CommitAsync(ct);

            // Re-read from view to get full object with assignments
            var createdRequest = await ReadByIdAsync(db, requestId, ct);

            if (request.Requirements is { Count: > 0 })
            {
                createdRequest = createdRequest with
                {
                    Requirements = await LoadRequirements(requestId, db, ct),
                };
            }
            else
            {
                createdRequest = createdRequest with
                {
                    Requirements = [],
                };
            }

            return createdRequest;
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<RequestInfo?> UpdateAsync(Guid id, UpdateRequestRequest request, CancellationToken ct = default)
    {
        await using var db = _connectionFactory.CreateOrgConnection(_orgContext);
        await db.OpenAsync(ct);

        var fetchCmd = new NpgsqlCommand("SELECT start_ts, end_ts FROM requests WHERE id = @id", db);
        fetchCmd.Parameters.AddWithValue("id", id);

        DateTime? currentStartTs;
        DateTime? currentEndTs;
        await using (var fetchReader = await fetchCmd.ExecuteReaderAsync(ct))
        {
            if (!await fetchReader.ReadAsync(ct))
                return null;
            currentStartTs = fetchReader.IsDBNull(0) ? null : fetchReader.GetDateTime(0);
            currentEndTs = fetchReader.IsDBNull(1) ? null : fetchReader.GetDateTime(1);
        }

        var finalStartTs = request.StartTs ?? currentStartTs;
        var finalEndTs = request.EndTs ?? currentEndTs;
        if (finalStartTs.HasValue && finalEndTs.HasValue && finalEndTs.Value <= finalStartTs.Value)
            throw new ArgumentException("End time must be after start time");

        var update = new UpdateBuilder();
        update.SetIfNotNull("name", request.Name);
        update.SetIfNotNull("description", request.Description);
        if (request.ParentRequestId.HasValue) update.Set("parent_request_id", request.ParentRequestId.Value);
        if (request.PlanningMode.HasValue) update.Set("planning_mode", EnumMapper.ToDbValue(request.PlanningMode.Value));
        if (request.SortOrder.HasValue) update.Set("sort_order", request.SortOrder.Value);
        // The pair travels together: naming the logic rewrites k as well, so a k left over from
        // a previous k_of_n cannot survive a switch to all/any and violate the CHECK.
        if (request.PredecessorLogic.HasValue)
        {
            update.Set("predecessor_logic", EnumMapper.ToDbValue(request.PredecessorLogic.Value));
            // k only for k_of_n; anything else writes NULL. A k_of_n with no k is refused by
            // the validator, and by the CHECK constraint if it ever gets past it.
            object k = request.PredecessorLogic.Value == PredecessorLogic.KOfN
                && request.PredecessorLogicK is { } value
                    ? value
                    : DBNull.Value;
            update.Set("predecessor_logic_k", k);
        }
        if (request.SiteId.HasValue) update.Set("site_id", request.SiteId.Value);
        else if (request.ChangeSiteId) update.Set("site_id", (object)DBNull.Value);
        update.SetIfNotNull("request_item_id", request.RequestItemId);
        update.SetIfNotNull("icon", request.Icon);
        if (request.StartTs.HasValue) update.Set("start_ts", request.StartTs.Value);
        if (request.EndTs.HasValue) update.Set("end_ts", request.EndTs.Value);
        if (request.EarliestStartTs.HasValue) update.Set("earliest_start_ts", request.EarliestStartTs.Value);
        if (request.LatestEndTs.HasValue) update.Set("latest_end_ts", request.LatestEndTs.Value);
        if (request.MinimalDurationValue.HasValue) update.Set("minimal_duration_value", request.MinimalDurationValue.Value);
        if (request.MinimalDurationUnit.HasValue) update.Set("minimal_duration_unit", EnumMapper.ToDbValue(request.MinimalDurationUnit.Value));
        if (request.ActualDurationValue.HasValue) update.Set("actual_duration_value", request.ActualDurationValue.Value);
        if (request.ActualDurationUnit.HasValue) update.Set("actual_duration_unit", EnumMapper.ToDbValue(request.ActualDurationUnit.Value));
        if (request.Status.HasValue) update.Set("status", EnumMapper.ToDbValue(request.Status.Value));
        if (request.SchedulingSettingsApply.HasValue) update.Set("scheduling_settings_apply", request.SchedulingSettingsApply.Value);

        if (update.IsEmpty && request.Requirements == null
            && request.TargetResourceTypeKeys == null
            && request.ResourceIds is not { Count: > 0 })
            throw new ArgumentException("No fields to update");

        await using var transaction = await db.BeginTransactionAsync(ct);

        if (!update.IsEmpty)
        {
            var cmd = new NpgsqlCommand
            {
                Connection = db,
                Transaction = transaction,
                CommandText = $"UPDATE requests SET {update.SetClause} WHERE id = @id",
            };
            cmd.Parameters.AddWithValue("id", id);
            update.Apply(cmd.Parameters);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        // Targets before assignments: a payload that adds a type and a resource of it in one
        // call must see the new target when the assignment is validated.
        //
        // Same rule as requirements: NULL leaves the targets alone, a supplied list replaces
        // them wholesale. An empty list is meaningful — a request that needs no resource.
        if (request.TargetResourceTypeKeys is { } targetKeys)
        {
            await WriteTargetResourceTypesAsync(db, transaction, id, targetKeys, ct);
        }

        // Update resource assignment if caller is changing the resource.
        if (request.ResourceIds is { Count: > 0 } updatedResources
            && finalStartTs.HasValue && finalEndTs.HasValue)
        {
            await WriteRequestResourcesAsync(
                db, transaction, id, updatedResources, finalStartTs.Value, finalEndTs.Value, ct);
        }

        // An edit that changes only the time names no resource, so the branch above does not run
        // and every booking would keep the old window — including the placeable one (#159).
        if (finalStartTs.HasValue && finalEndTs.HasValue)
        {
            await SyncAssignmentWindowsAsync(
                db, transaction, id, finalStartTs.Value, finalEndTs.Value, ct);
        }

        // Replace requirements wholesale if the caller supplied a (possibly empty) list.
        if (request.Requirements != null)
        {
            await using var deleteCmd = new NpgsqlCommand(
                "DELETE FROM request_requirements WHERE request_id = @request_id", db, transaction);
            deleteCmd.Parameters.AddWithValue("request_id", id);
            await deleteCmd.ExecuteNonQueryAsync(ct);
            if (request.Requirements.Count > 0)
                await CreateRequirements(id, request.Requirements, db, transaction, ct);
        }

        await transaction.CommitAsync(ct);

        // Single re-read for the full object (view supplies assignments).
        var updatedRequest = await ReadByIdAsync(db, id, ct);
        return updatedRequest with { Requirements = await LoadRequirements(id, db, ct) };
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = _connectionFactory.CreateOrgConnection(_orgContext);
        await db.OpenAsync(ct);
        var rowsAffected = await db.ExecuteAsync("DELETE FROM requests WHERE id = @id",
            p => p.AddWithValue("id", id), ct);
        return rowsAffected > 0;
    }

    public async Task<bool> ExistsAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = _connectionFactory.CreateOrgConnection(_orgContext);
        await db.OpenAsync(ct);
        return await db.ExistsAsync("requests", id, ct);
    }

    public async Task<RequestInfo?> UpdateScheduleAsync(Guid id, ScheduleRequestRequest request, CancellationToken ct = default)
    {
        await using var db = _connectionFactory.CreateOrgConnection(_orgContext);
        await db.OpenAsync(ct);

        if (!await db.ExistsAsync("requests", id, ct))
            return null;

        if (request.ResourceId.HasValue
            && !await db.ExistsAsync("resources", request.ResourceId.Value, ct))
        {
            throw new ArgumentException("Invalid resource_id: resource does not exist");
        }

        int? actualDurationValue = request.ActualDurationValue;
        string? actualDurationUnit = request.ActualDurationUnit.HasValue
            ? EnumMapper.ToDbValue(request.ActualDurationUnit.Value)
            : null;

        if (actualDurationValue == null && request.StartTs.HasValue && request.EndTs.HasValue)
        {
            actualDurationValue = (int)(request.EndTs.Value - request.StartTs.Value).TotalMinutes;
            actualDurationUnit = "minutes";
        }

        await using var tx = await db.BeginTransactionAsync(ct);

        // Implicit site-on-schedule: a site-neutral request adopts the home site of the resource it
        // is scheduled into. COALESCE keeps an existing scope and is a no-op when no resource is
        // given (the subquery yields NULL for a null/unknown resource id).
        var cmd = new NpgsqlCommand(
            $@"UPDATE requests
               SET start_ts = @start_ts, end_ts = @end_ts,
                   actual_duration_value = @actual_duration_value,
                   actual_duration_unit  = @actual_duration_unit,
                   site_id = COALESCE(site_id, (SELECT home_site_id FROM resources
                                                 WHERE id = @resource_id AND NOT cross_site_allowed))
               WHERE id = @id
               RETURNING id",
            db, tx);

        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddNullable("resource_id", request.ResourceId);
        cmd.Parameters.AddNullable("start_ts", request.StartTs);
        cmd.Parameters.AddNullable("end_ts", request.EndTs);
        cmd.Parameters.AddNullable("actual_duration_value", actualDurationValue);
        cmd.Parameters.AddNullable("actual_duration_unit", actualDurationUnit);

        var updatedId = (Guid?)await cmd.ExecuteScalarAsync(ct);
        if (!updatedId.HasValue)
        {
            await tx.RollbackAsync(ct);
            return null;
        }

        // With a resource: replace the one occupying that type's slot. Without: this call is
        // an unschedule, so every targeted slot is cleared.
        if (request.ResourceId.HasValue && request.StartTs.HasValue && request.EndTs.HasValue)
        {
            await CancelSameTypeAssignmentAsync(db, tx, updatedId.Value, request.ResourceId.Value, ct);
            await WriteResourceAssignmentAsync(db, tx, updatedId.Value, request.ResourceId.Value, request.StartTs.Value, request.EndTs.Value, ct);
            // The write above only covers the slot being replaced; the request's other bookings
            // have to follow it onto the new window (#159).
            await SyncAssignmentWindowsAsync(db, tx, updatedId.Value, request.StartTs.Value, request.EndTs.Value, ct);
        }
        else
        {
            await CancelTargetedAssignmentsAsync(db, tx, updatedId.Value, ct);
        }

        await tx.CommitAsync(ct);

        // Re-read from view to get full object with assignments
        return await ReadByIdAsync(db, updatedId.Value, ct);
    }

    public async Task<int> BatchUpdateSchedulesAsync(IReadOnlyList<(Guid Id, ScheduleRequestRequest Data)> updates, CancellationToken ct = default)
    {
        if (updates.Count == 0) return 0;

        await using var db = _connectionFactory.CreateOrgConnection(_orgContext);
        await db.OpenAsync(ct);
        await using var tx = await db.BeginTransactionAsync(ct);

        await using var batch = new NpgsqlBatch(db, tx);
        var requestUpdateCommands = new List<NpgsqlBatchCommand>(updates.Count);
        foreach (var (id, request) in updates)
        {
            int? actualDurationValue = request.ActualDurationValue;
            string? actualDurationUnit = request.ActualDurationUnit.HasValue
                ? EnumMapper.ToDbValue(request.ActualDurationUnit.Value)
                : null;

            if (actualDurationValue == null && request.StartTs.HasValue && request.EndTs.HasValue)
            {
                actualDurationValue = (int)(request.EndTs.Value - request.StartTs.Value).TotalMinutes;
                actualDurationUnit = "minutes";
            }

            var cmd = new NpgsqlBatchCommand(
                @"UPDATE requests
                  SET start_ts = @start_ts, end_ts = @end_ts,
                      actual_duration_value = @actual_duration_value,
                      actual_duration_unit  = @actual_duration_unit
                  WHERE id = @id");
            cmd.Parameters.AddWithValue("id", id);
            cmd.Parameters.AddNullable("start_ts", request.StartTs);
            cmd.Parameters.AddNullable("end_ts", request.EndTs);
            cmd.Parameters.AddNullable("actual_duration_value", actualDurationValue);
            cmd.Parameters.AddNullable("actual_duration_unit", actualDurationUnit);
            batch.BatchCommands.Add(cmd);
            requestUpdateCommands.Add(cmd);

            // Update resource assignments for each scheduled item, in the same batch:
            // cancel whatever held this resource's type slot, then write the new one.
            if (!request.ResourceId.HasValue || !request.StartTs.HasValue || !request.EndTs.HasValue)
                continue;

            var cancel = new NpgsqlBatchCommand(CancelSameTypeAssignmentSql);
            cancel.Parameters.AddWithValue("requestId", id);
            cancel.Parameters.AddWithValue("resourceId", request.ResourceId.Value);
            cancel.Parameters.AddWithValue("cancelled", AssignmentStatuses.Cancelled);
            batch.BatchCommands.Add(cancel);

            var write = new NpgsqlBatchCommand(WriteResourceAssignmentSql);
            write.Parameters.AddWithValue("requestId", id);
            write.Parameters.AddWithValue("resourceId", request.ResourceId.Value);
            write.Parameters.AddWithValue("startUtc", request.StartTs.Value);
            write.Parameters.AddWithValue("endUtc", request.EndTs.Value);
            batch.BatchCommands.Add(write);

            // The auto-scheduler solves one resource type at a time, so a multi-type request
            // arrives here with only that type's resource named. Its other bookings still have
            // to follow the new window (#159). Deliberately not added to requestUpdateCommands:
            // only the request UPDATEs count toward the returned total.
            var sync = new NpgsqlBatchCommand(SyncAssignmentWindowsSql);
            sync.Parameters.AddWithValue("requestId", id);
            sync.Parameters.AddWithValue("startUtc", request.StartTs.Value);
            sync.Parameters.AddWithValue("endUtc", request.EndTs.Value);
            sync.Parameters.AddWithValue("cancelled", AssignmentStatuses.Cancelled);
            batch.BatchCommands.Add(sync);
        }

        await batch.ExecuteNonQueryAsync(ct);

        // Only the request UPDATEs count — the assignment statements must not inflate the total.
        var rowsAffected = (int)requestUpdateCommands.Aggregate(0UL, (sum, c) => sum + c.Rows);

        await tx.CommitAsync(ct);
        return rowsAffected;
    }

    public async Task<RequestRequirementInfo> AddRequirementAsync(Guid requestId, AddRequirementRequest requirement, CancellationToken ct = default)
    {
        await using var db = _connectionFactory.CreateOrgConnection(_orgContext);

        if (!await db.ExistsAsync("requests", requestId, ct))
            throw new NotFoundException("Request", requestId);

        if (!await db.ExistsAsync("criteria", requirement.CriterionId, ct))
            throw new ArgumentException("Invalid criterion_id: criterion does not exist");

        // Phase 3: Validate criterion is applicable to requests
        var applicableToRequests = await db.ExecuteScalarAsync<bool?>(
            "SELECT applicable_to_requests FROM criteria WHERE id = @criterionId",
            p => p.AddWithValue("criterionId", requirement.CriterionId), ct);
        if (applicableToRequests == false)
            throw new InvalidOperationException(
                $"Criterion {requirement.CriterionId} is not applicable to requests");

        return (await db.QuerySingleOrDefaultAsync(@"
            INSERT INTO request_requirements (request_id, criterion_id, value, operator, allowed_values)
            VALUES (@request_id, @criterion_id, @value::jsonb, @operator, @allowed_values::jsonb)
            ON CONFLICT (request_id, criterion_id) DO UPDATE SET
                value = EXCLUDED.value,
                operator = EXCLUDED.operator,
                allowed_values = EXCLUDED.allowed_values
            RETURNING id, request_id, criterion_id, value, operator, allowed_values, created_at",
            p =>
            {
                p.AddWithValue("request_id", requestId);
                p.AddWithValue("criterion_id", requirement.CriterionId);
                p.AddWithValue("value", requirement.Value.GetRawText());
                p.AddWithValue("operator", requirement.Operator is null ? (object)DBNull.Value : requirement.Operator);
                p.AddWithValue("allowed_values", requirement.AllowedValues is null ? (object)DBNull.Value : requirement.AllowedValues.Value.GetRawText());
            },
            RequestMapper.MapRequirementFromReader,
            ct))!;
    }

    public async Task<bool> DeleteRequirementAsync(Guid requestId, Guid requirementId, CancellationToken ct = default)
    {
        await using var db = _connectionFactory.CreateOrgConnection(_orgContext);

        return await db.ExecuteAsync(
            "DELETE FROM request_requirements WHERE id = @id AND request_id = @request_id",
            p =>
            {
                p.AddWithValue("id", requirementId);
                p.AddWithValue("request_id", requestId);
            }, ct) > 0;
    }

    // ── Resource assignment helpers ───────────────────────────────────────────

    // Every resource booked for a request is booked for *the request's* window, so when that
    // window moves they all move with it. Cancel-then-write only ever rewrites the type slot
    // being replaced, which left the other types — and any person attached on the request's own
    // tab — sitting on the old time, still occupying the resource for availability, conflicts
    // and utilization (issue #159). Not scoped by type or by target: that scoping is the bug.
    // The inequality guard keeps updated_at honest — an already-correct row is not touched.
    private const string SyncAssignmentWindowsSql = @"
            UPDATE resource_assignments
               SET start_utc = @startUtc, end_utc = @endUtc, updated_at = NOW()
             WHERE request_id = @requestId
               AND assignment_status != @cancelled
               AND (start_utc <> @startUtc OR end_utc <> @endUtc)";

    // The ON CONFLICT predicate MUST stay byte-identical to the migration's partial unique
    // index predicate (uq_resource_assignments_active). AssignmentStatuses.Cancelled == 'Cancelled',
    // so interpolating it yields the same SQL while keeping the literal traceable to the constant.
    private const string WriteResourceAssignmentSql = $@"
            INSERT INTO resource_assignments
                (request_id, resource_id, start_utc, end_utc)
            VALUES (@requestId, @resourceId, @startUtc, @endUtc)
            ON CONFLICT (request_id, resource_id)
                WHERE assignment_status != '{AssignmentStatuses.Cancelled}'
            DO UPDATE SET start_utc = EXCLUDED.start_utc,
                          end_utc   = EXCLUDED.end_utc,
                          updated_at = NOW()";

    // Cancel-then-write is what keeps one resource per targeted type. Keyed on the type of the
    // resource being written, not on 'space': assigning a van must replace the previous van and
    // leave the room and the technician alone. If this predicate and the request's targets ever
    // disagree, the result is a silently orphaned or doubled assignment.
    //
    // The incoming resource is excluded so re-assigning the same resource updates its existing
    // row (WriteResourceAssignmentSql upserts on request_id+resource_id where not cancelled)
    // rather than cancelling it and inserting a duplicate.
    private const string CancelSameTypeAssignmentSql = @"
            UPDATE resource_assignments ra
            SET assignment_status = @cancelled, updated_at = NOW()
            FROM resources res, resources incoming
            WHERE ra.resource_id = res.id
              AND incoming.id = @resourceId
              AND res.resource_type_id = incoming.resource_type_id
              AND ra.request_id = @requestId
              AND ra.resource_id <> @resourceId
              AND ra.assignment_status != @cancelled";

    // Unscheduling clears the targeted slots and only those. Scoping to the request's target
    // types is what today's rt.key='space' test meant when 'space' was the only target a
    // request could have; it leaves ad-hoc attachments (people added on the request's own tab,
    // which are not a target type) untouched, exactly as cancelling spaces used to.
    private const string CancelTargetedAssignmentsSql = @"
            UPDATE resource_assignments ra
            SET assignment_status = @cancelled, updated_at = NOW()
            FROM resources res
            JOIN request_target_resource_types t ON t.resource_type_id = res.resource_type_id
            WHERE ra.resource_id = res.id
              AND t.request_id = @requestId
              AND ra.request_id = @requestId
              AND ra.assignment_status != @cancelled";

    /// <summary>
    /// Assigns each resource to the request, replacing whatever held its type's slot. Rejects a
    /// payload naming two resources of the same type — writing both would silently cancel the
    /// first, so the caller would get one assignment back having asked for two.
    /// </summary>
    private static async Task WriteRequestResourcesAsync(
        NpgsqlConnection db, NpgsqlTransaction tx, Guid requestId,
        IReadOnlyList<Guid> resourceIds, DateTime startTs, DateTime endTs, CancellationToken ct)
    {
        await using (var check = new NpgsqlCommand(
            @"SELECT count(*) FROM (
                  SELECT 1 FROM resources WHERE id = ANY(@ids)
                   GROUP BY resource_type_id HAVING count(*) > 1
              ) dup",
            db, tx))
        {
            check.Parameters.AddWithValue("ids", resourceIds.Distinct().ToArray());
            if ((long)(await check.ExecuteScalarAsync(ct) ?? 0L) > 0)
                throw new ArgumentException(
                    "A request holds at most one resource per type; the payload names several of the same type.");
        }

        // A resource whose type the request never asked for would be invisible to the
        // scheduled predicate — which iterates the targets — while still occupying the
        // resource and counting toward its site. Neither assigned nor rejected is the one
        // outcome with no defensible reading, so reject it.
        await using (var untargeted = new NpgsqlCommand(
            @"SELECT r.name FROM resources r
               WHERE r.id = ANY(@ids)
                 AND NOT EXISTS (
                     SELECT 1 FROM request_target_resource_types t
                      WHERE t.request_id = @requestId
                        AND t.resource_type_id = r.resource_type_id)
               LIMIT 1",
            db, tx))
        {
            untargeted.Parameters.AddWithValue("ids", resourceIds.Distinct().ToArray());
            untargeted.Parameters.AddWithValue("requestId", requestId);
            if (await untargeted.ExecuteScalarAsync(ct) is string offender)
                throw new ArgumentException(
                    $"'{offender}' is a resource of a type this request does not ask for. "
                    + "Add the type to the request's needs first.");
        }

        foreach (var resourceId in resourceIds.Distinct())
        {
            await CancelSameTypeAssignmentAsync(db, tx, requestId, resourceId, ct);
            await WriteResourceAssignmentAsync(db, tx, requestId, resourceId, startTs, endTs, ct);
        }
    }

    private static async Task WriteResourceAssignmentAsync(
        NpgsqlConnection conn, NpgsqlTransaction? tx,
        Guid requestId, Guid resourceId, DateTime startUtc, DateTime endUtc, CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(WriteResourceAssignmentSql, conn, tx);
        cmd.Parameters.AddWithValue("requestId", requestId);
        cmd.Parameters.AddWithValue("resourceId", resourceId);
        cmd.Parameters.AddWithValue("startUtc", startUtc);
        cmd.Parameters.AddWithValue("endUtc", endUtc);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Moves every live booking on a request onto the request's own window. Called wherever the
    /// request's schedule changes, so a resource is never left booked for a time the work no
    /// longer happens.
    /// </summary>
    private static async Task SyncAssignmentWindowsAsync(
        NpgsqlConnection conn, NpgsqlTransaction? tx, Guid requestId,
        DateTime startUtc, DateTime endUtc, CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(SyncAssignmentWindowsSql, conn, tx);
        cmd.Parameters.AddWithValue("requestId", requestId);
        cmd.Parameters.AddWithValue("startUtc", startUtc);
        cmd.Parameters.AddWithValue("endUtc", endUtc);
        cmd.Parameters.AddWithValue("cancelled", AssignmentStatuses.Cancelled);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task CancelSameTypeAssignmentAsync(
        NpgsqlConnection conn, NpgsqlTransaction? tx, Guid requestId, Guid resourceId, CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(CancelSameTypeAssignmentSql, conn, tx);
        cmd.Parameters.AddWithValue("requestId", requestId);
        cmd.Parameters.AddWithValue("resourceId", resourceId);
        cmd.Parameters.AddWithValue("cancelled", AssignmentStatuses.Cancelled);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task CancelTargetedAssignmentsAsync(
        NpgsqlConnection conn, NpgsqlTransaction? tx, Guid requestId, CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(CancelTargetedAssignmentsSql, conn, tx);
        cmd.Parameters.AddWithValue("requestId", requestId);
        cmd.Parameters.AddWithValue("cancelled", AssignmentStatuses.Cancelled);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ── Candidate requests ────────────────────────────────────────────────────

    public async Task<List<(RequestInfo Request, Guid? AssignmentId)>> GetCandidatesOverlappingAsync(Guid resourceId, DateTime start, DateTime end, CancellationToken ct = default)
    {
        await using var db = _connectionFactory.CreateOrgConnection(_orgContext);

        var rows = await db.QueryListAsync<(RequestInfo, Guid?)>($@"
            SELECT {SelectFromView},
                   (SELECT ra.id FROM resource_assignments ra
                    WHERE ra.request_id = v_requests_with_assignments.id
                      AND ra.resource_id = @resourceId
                      AND ra.assignment_status != @cancelled
                    LIMIT 1) AS assignment_id
            FROM v_requests_with_assignments
            WHERE status IN ('{RequestStatuses.New}', '{RequestStatuses.InProgress}')
              AND start_ts IS NOT NULL
              AND end_ts IS NOT NULL
              AND start_ts < @end
              AND end_ts > @start
            ORDER BY start_ts, name",
            p =>
            {
                p.AddWithValue("resourceId", resourceId);
                p.AddWithValue("start", start);
                p.AddWithValue("end", end);
                p.AddWithValue("cancelled", AssignmentStatuses.Cancelled);
            },
            reader =>
            {
                var req = RequestMapper.MapFromReader(reader);
                var assignmentId = reader.IsDBNull(reader.GetOrdinal("assignment_id"))
                    ? (Guid?)null
                    : reader.GetGuid(reader.GetOrdinal("assignment_id"));
                return (req, assignmentId);
            },
            ct);

        if (rows.Count > 0)
        {
            var requests = rows.Select(r => r.Item1).ToList();
            await LoadRequirementsForRequests(requests, db, ct);
            // LoadRequirementsForRequests replaces RequestInfo instances via `with`; sync back.
            for (var i = 0; i < rows.Count; i++)
                rows[i] = (requests[i], rows[i].Item2);
        }

        return rows;
    }

    // ── Requirements helpers ──────────────────────────────────────────────────

    private async Task<List<RequestRequirementInfo>> LoadRequirements(Guid requestId, NpgsqlConnection db, CancellationToken ct = default)
    {
        return await db.QueryListAsync(@"
            SELECT rr.id, rr.request_id, rr.criterion_id, rr.value, rr.created_at,
                   rr.operator, rr.allowed_values,
                   c.id, c.name, c.data_type, c.unit, c.enum_values
            FROM request_requirements rr
            JOIN criteria c ON rr.criterion_id = c.id
            WHERE rr.request_id = @request_id
            ORDER BY c.name",
            p => p.AddWithValue("request_id", requestId),
            RequestMapper.MapRequirementWithCriterionFromReader,
            ct);
    }

    private async Task LoadRequirementsForRequests(List<RequestInfo> requests, NpgsqlConnection db, CancellationToken ct = default)
    {
        var requestIds = requests.Select(r => r.Id).ToArray();
        var requirementsMap = new Dictionary<Guid, List<RequestRequirementInfo>>();

        var cmd = new NpgsqlCommand(@"
            SELECT rr.id, rr.request_id, rr.criterion_id, rr.value, rr.created_at,
                   rr.operator, rr.allowed_values,
                   c.id, c.name, c.data_type, c.unit, c.enum_values
            FROM request_requirements rr
            JOIN criteria c ON rr.criterion_id = c.id
            WHERE rr.request_id = ANY(@request_ids)
            ORDER BY rr.request_id, c.name", db);
        cmd.Parameters.AddWithValue("request_ids", requestIds);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var requestId = reader.GetGuid(1);
            if (!requirementsMap.TryGetValue(requestId, out var list))
            {
                list = [];
                requirementsMap[requestId] = list;
            }
            list.Add(RequestMapper.MapRequirementWithCriterionFromReader(reader));
        }

        for (var i = 0; i < requests.Count; i++)
        {
            requests[i] = requests[i] with
            {
                Requirements = requirementsMap.TryGetValue(requests[i].Id, out var reqs)
                    ? reqs
                    : [],
            };
        }
    }

    /// <summary>
    /// Replaces a request's target resource types wholesale. Unknown keys are rejected rather
    /// than skipped: silently dropping one would leave a request needing less than the caller
    /// asked for, and a request that needs less is a request that reports itself scheduled.
    /// </summary>
    private static async Task WriteTargetResourceTypesAsync(
        NpgsqlConnection db, NpgsqlTransaction tx, Guid requestId,
        IReadOnlyList<string> typeKeys, CancellationToken ct)
    {
        await using (var del = new NpgsqlCommand(
            "DELETE FROM request_target_resource_types WHERE request_id = @request_id", db, tx))
        {
            del.Parameters.AddWithValue("request_id", requestId);
            await del.ExecuteNonQueryAsync(ct);
        }

        if (typeKeys.Count == 0) return;

        await using var ins = new NpgsqlCommand(
            @"INSERT INTO request_target_resource_types (request_id, resource_type_id)
              SELECT @request_id, rt.id FROM resource_types rt WHERE rt.key = ANY(@keys)",
            db, tx);
        ins.Parameters.AddWithValue("request_id", requestId);
        ins.Parameters.AddWithValue("keys", typeKeys.Distinct().ToArray());
        var written = await ins.ExecuteNonQueryAsync(ct);

        if (written != typeKeys.Distinct().Count())
        {
            throw new ArgumentException(
                "One or more target resource type keys do not exist: "
                + string.Join(", ", typeKeys));
        }
    }

    private static async Task<List<RequestRequirementInfo>> CreateRequirements(
        Guid requestId,
        List<CreateRequestRequirementRequest> requirements,
        NpgsqlConnection db,
        NpgsqlTransaction transaction,
        CancellationToken ct = default)
    {
        var valueClauses = new List<string>();
        var cmd = new NpgsqlCommand { Connection = db, Transaction = transaction };

        for (var i = 0; i < requirements.Count; i++)
        {
            valueClauses.Add($"(@request_id, @criterion_id_{i}, @value_{i}::jsonb, @operator_{i}, @allowed_values_{i}::jsonb)");
            var req = requirements[i];
            cmd.Parameters.AddWithValue($"criterion_id_{i}", req.CriterionId);
            cmd.Parameters.AddWithValue($"value_{i}", req.Value.GetRawText());
            cmd.Parameters.AddWithValue($"operator_{i}", req.Operator is null ? (object)DBNull.Value : req.Operator);
            cmd.Parameters.AddWithValue($"allowed_values_{i}", req.AllowedValues is null ? (object)DBNull.Value : req.AllowedValues.Value.GetRawText());
        }

        cmd.Parameters.AddWithValue("request_id", requestId);
        cmd.CommandText = $@"
            INSERT INTO request_requirements (request_id, criterion_id, value, operator, allowed_values)
            VALUES {string.Join(", ", valueClauses)}
            RETURNING id, request_id, criterion_id, value, operator, allowed_values, created_at";

        var createdRequirements = new List<RequestRequirementInfo>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            createdRequirements.Add(RequestMapper.MapRequirementFromReader(reader));
        return createdRequirements;
    }

    // ── Tree hierarchy methods ────────────────────────────────────────────────

    public async Task<List<RequestInfo>> GetChildrenAsync(Guid parentId, CancellationToken ct = default)
    {
        await using var db = _connectionFactory.CreateOrgConnection(_orgContext);

        return await db.QueryListAsync(
            $"SELECT {SelectFromView} FROM v_requests_with_assignments WHERE parent_request_id = @parent_id ORDER BY sort_order, created_at",
            p => p.AddWithValue("parent_id", parentId),
            RequestMapper.MapFromReader,
            ct);
    }

    public async Task<RequestInfo?> MoveAsync(Guid id, Guid? newParentId, int sortOrder, CancellationToken ct = default)
    {
        await using var db = _connectionFactory.CreateOrgConnection(_orgContext);

        var updatedId = await db.ExecuteScalarAsync<Guid?>(
            $@"UPDATE requests
               SET parent_request_id = @parent_id, sort_order = @sort_order, updated_at = NOW()
               WHERE id = @id
               RETURNING id",
            p =>
            {
                p.AddWithValue("id", id);
                p.AddNullable("parent_id", newParentId);
                p.AddWithValue("sort_order", sortOrder);
            }, ct);
        if (!updatedId.HasValue)
            return null;

        // Re-read from view to get full object with assignments
        return await ReadByIdAsync(db, updatedId.Value, ct);
    }

    public async Task<int> GetDescendantCountAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = _connectionFactory.CreateOrgConnection(_orgContext);

        return await db.ExecuteScalarAsync<int>(
            @"WITH RECURSIVE subtree AS (
                SELECT id FROM requests WHERE parent_request_id = @id
                UNION ALL
                SELECT r.id FROM requests r JOIN subtree s ON r.parent_request_id = s.id
              )
              SELECT COUNT(*)::int FROM subtree",
            p => p.AddWithValue("id", id), ct);
    }

    public async Task<bool> WouldCreateCycleAsync(Guid requestId, Guid newParentId, CancellationToken ct = default)
    {
        if (requestId == newParentId) return true;

        await using var db = _connectionFactory.CreateOrgConnection(_orgContext);

        return await db.ExecuteScalarAsync<bool>(
            @"WITH RECURSIVE ancestors AS (
                SELECT parent_request_id FROM requests WHERE id = @new_parent_id
                UNION ALL
                SELECT r.parent_request_id FROM requests r JOIN ancestors a ON r.id = a.parent_request_id
                WHERE r.parent_request_id IS NOT NULL
              )
              SELECT EXISTS(SELECT 1 FROM ancestors WHERE parent_request_id = @request_id)",
            p =>
            {
                p.AddWithValue("request_id", requestId);
                p.AddWithValue("new_parent_id", newParentId);
            }, ct);
    }

    public async Task<PlanningMode?> GetPlanningModeAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = _connectionFactory.CreateOrgConnection(_orgContext);

        var result = await db.ExecuteScalarAsync<string>(
            "SELECT planning_mode FROM requests WHERE id = @id",
            p => p.AddWithValue("id", id), ct);
        if (result is null) return null;
        return EnumMapper.ToPlanningMode(result);
    }

    /// <summary>
    /// The STORED status, not the effective one. RequestInfo reports status derived from the
    /// schedule, so a caller that needs to know what a transition is moving away from — the
    /// execution gate — cannot read it there.
    /// </summary>
    public async Task<RequestStatus?> GetStoredStatusAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = _connectionFactory.CreateOrgConnection(_orgContext);

        var result = await db.ExecuteScalarAsync<string>(
            "SELECT status FROM requests WHERE id = @id",
            p => p.AddWithValue("id", id), ct);
        if (result is null) return null;
        return EnumMapper.FromDbValue<RequestStatus>(result);
    }

    public async Task<Dictionary<Guid, RequestStatus>> GetStoredStatusesAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken ct = default)
    {
        if (ids.Count == 0) return [];

        await using var db = _connectionFactory.CreateOrgConnection(_orgContext);

        var rows = await db.QueryListAsync(
            "SELECT id, status FROM requests WHERE id = ANY(@ids)",
            p => p.AddWithValue("ids", ids.ToArray()),
            r => (Id: r.GetGuid("id"), Status: EnumMapper.FromDbValue<RequestStatus>(r.GetString("status"))),
            ct);

        return rows.ToDictionary(r => r.Id, r => r.Status);
    }

    public async Task<bool> HasChildrenAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = _connectionFactory.CreateOrgConnection(_orgContext);

        return await db.ExecuteScalarAsync<bool>(
            "SELECT EXISTS(SELECT 1 FROM requests WHERE parent_request_id = @id)",
            p => p.AddWithValue("id", id), ct);
    }

    public async Task<int> DeleteSubtreeAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = _connectionFactory.CreateOrgConnection(_orgContext);

        // One statement: count the subtree (root + descendants, snapshotted before the delete)
        // and delete the root — children go via the FK cascade. Returns 0 when the root is absent.
        return await db.ExecuteScalarAsync<int>(
            @"WITH RECURSIVE subtree AS (
                SELECT id FROM requests WHERE id = @id
                UNION ALL
                SELECT r.id FROM requests r JOIN subtree s ON r.parent_request_id = s.id
              ),
              deleted AS (
                DELETE FROM requests WHERE id = @id RETURNING id
              )
              SELECT CASE WHEN EXISTS (SELECT 1 FROM deleted)
                          THEN (SELECT COUNT(*)::int FROM subtree)
                          ELSE 0 END",
            p => p.AddWithValue("id", id), ct);
    }
}
