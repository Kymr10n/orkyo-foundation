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
    // Columns selected from the view.
    private const string SelectFromView =
        @"id, name, description, parent_request_id, planning_mode, sort_order,
          site_id,
          request_item_id, icon,
          start_ts, end_ts, earliest_start_ts, latest_end_ts,
          minimal_duration_value, minimal_duration_unit,
          actual_duration_value, actual_duration_unit,
          status, scheduling_settings_apply, created_at, updated_at, assignments";

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

    public async Task<List<RequestInfo>> GetScheduledBySiteAsync(Guid siteId, CancellationToken ct = default)
    {
        await using var db = _connectionFactory.CreateOrgConnection(_orgContext);

        return await db.QueryListAsync($@"
            SELECT {SelectFromView}
            FROM v_requests_with_assignments
            WHERE scheduling_settings_apply = true
              AND start_ts IS NOT NULL
              AND EXISTS (
                SELECT 1 FROM resource_assignments ra
                JOIN resources res ON res.id = ra.resource_id
                JOIN resource_types rt ON rt.id = res.resource_type_id
                JOIN spaces s ON s.id = res.id
                WHERE ra.request_id = v_requests_with_assignments.id
                  AND rt.key = @spaceKey
                  AND ra.assignment_status != @cancelled
                  AND s.site_id = @siteId
              )",
            p =>
            {
                p.AddWithValue("siteId", siteId);
                p.AddWithValue("spaceKey", ResourceTypeKeys.Space);
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

        // All scheduled requests with a (non-cancelled) space assignment, tenant-wide. When a
        // [from,to] window is supplied (utilization grid) only bars overlapping it are returned;
        // without one (Conflicts page) the registry is all-time. No scheduling_settings_apply filter
        // — the registry mirrors what the grid surfaces for every scheduled bar.
        var windowed = from.HasValue && to.HasValue;
        var windowClause = windowed ? " AND start_ts <= @to AND end_ts >= @from" : "";
        var requests = await db.QueryListAsync($@"
            SELECT {SelectFromView}
            FROM v_requests_with_assignments
            WHERE start_ts IS NOT NULL{windowClause}
              AND EXISTS (
                SELECT 1 FROM resource_assignments ra
                JOIN resources res ON res.id = ra.resource_id
                JOIN resource_types rt ON rt.id = res.resource_type_id
                WHERE ra.request_id = v_requests_with_assignments.id
                  AND rt.key = @spaceKey
                  AND ra.assignment_status != @cancelled
              )",
            p =>
            {
                p.AddWithValue("spaceKey", ResourceTypeKeys.Space);
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
        return await db.QueryListAsync(@"
            SELECT id, start_ts, site_id
            FROM requests r
            WHERE start_ts IS NOT NULL AND start_ts <= @to AND end_ts >= @from
              AND EXISTS (
                SELECT 1 FROM resource_assignments ra
                JOIN resources res ON res.id = ra.resource_id
                JOIN resource_types rt ON rt.id = res.resource_type_id
                WHERE ra.request_id = r.id
                  AND rt.key = @spaceKey
                  AND ra.assignment_status != @cancelled
              )",
            p =>
            {
                p.AddWithValue("spaceKey", ResourceTypeKeys.Space);
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

        // Scheduled requests for this site whose bar overlaps [from,to]: start_ts <= to AND end_ts >= from.
        // A request belongs to the site if it is scoped to it (site_id) OR placed into one of its
        // spaces. The site_id arm makes a scheduled, space-less request appear on its site's calendar.
        var requests = await db.QueryListAsync($@"
            SELECT {SelectFromView}
            FROM v_requests_with_assignments
            WHERE start_ts IS NOT NULL
              AND start_ts <= @to AND end_ts >= @from
              AND (
                site_id = @siteId
                OR EXISTS (
                  SELECT 1 FROM resource_assignments ra
                  JOIN resources res ON res.id = ra.resource_id
                  JOIN resource_types rt ON rt.id = res.resource_type_id
                  JOIN spaces s ON s.id = res.id
                  WHERE ra.request_id = v_requests_with_assignments.id
                    AND rt.key = @spaceKey
                    AND ra.assignment_status != @cancelled
                    AND s.site_id = @siteId
                )
              )",
            p =>
            {
                p.AddWithValue("siteId", siteId);
                p.AddWithValue("from", from);
                p.AddWithValue("to", to);
                p.AddWithValue("spaceKey", ResourceTypeKeys.Space);
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
        // RequestInfo.IsScheduled = start_ts && end_ts && a non-cancelled Space assignment, so a timed
        // leaf missing its end_ts OR its Space assignment qualifies. These stay auto-schedulable and
        // would otherwise be invisible to the solver (they are excluded from both the unscheduled
        // backlog and the fixed-occupancy fetch, which filters IsScheduled).
        var requests = await db.QueryListAsync(
            $@"SELECT {SelectFromView} FROM v_requests_with_assignments
               WHERE start_ts IS NOT NULL AND planning_mode = '{PlanningModes.Leaf}'
                 AND (
                   end_ts IS NULL
                   OR NOT EXISTS (
                     SELECT 1 FROM resource_assignments ra
                     JOIN resources res ON res.id = ra.resource_id
                     JOIN resource_types rt ON rt.id = res.resource_type_id
                     WHERE ra.request_id = v_requests_with_assignments.id
                       AND rt.key = @spaceKey
                       AND ra.assignment_status != @cancelled
                   )
                 )
               ORDER BY parent_request_id NULLS FIRST, sort_order, created_at DESC",
            p =>
            {
                p.AddWithValue("spaceKey", ResourceTypeKeys.Space);
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

        // Validate resource_id if provided (resource must exist)
        if (request.ResourceId.HasValue
            && !await db.ExistsAsync("resources", request.ResourceId.Value, ct))
        {
            throw new ArgumentException("Invalid resource_id: resource does not exist");
        }

        // Validate site_id if provided (site must exist)
        if (request.SiteId.HasValue
            && !await db.ExistsAsync("sites", request.SiteId.Value, ct))
        {
            throw new ArgumentException("Invalid site_id: site does not exist");
        }

        // Implicit site-on-schedule: a request created directly into a space (no explicit site)
        // adopts that space's site. Mirrors UpdateScheduleAsync so every creation route agrees.
        var effectiveSiteId = request.SiteId;
        if (effectiveSiteId is null && request.ResourceId.HasValue)
        {
            effectiveSiteId = await db.QuerySingleOrDefaultAsync<Guid?>(
                "SELECT site_id FROM spaces WHERE id = @id",
                p => p.AddWithValue("id", request.ResourceId.Value),
                r => r.GetGuid(0), ct);
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

            // Create resource assignment if a resource + time window was provided.
            if (request.ResourceId.HasValue && request.StartTs.HasValue && request.EndTs.HasValue)
            {
                await WriteResourceAssignmentAsync(db, transaction, requestId, request.ResourceId.Value, request.StartTs.Value, request.EndTs.Value, ct);
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

        if (update.IsEmpty && request.Requirements == null && !request.ResourceId.HasValue)
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

        // Update resource assignment if caller is changing the resource.
        if (request.ResourceId.HasValue && finalStartTs.HasValue && finalEndTs.HasValue)
        {
            await CancelSpaceAssignmentAsync(db, transaction, id, ct);
            await WriteResourceAssignmentAsync(db, transaction, id, request.ResourceId.Value, finalStartTs.Value, finalEndTs.Value, ct);
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

        // Implicit site-on-schedule: a site-neutral request adopts the site of the space it is
        // scheduled into. COALESCE keeps an existing scope and is a no-op when no space is given
        // (the subquery yields NULL for a null/ non-space resource id).
        var cmd = new NpgsqlCommand(
            $@"UPDATE requests
               SET start_ts = @start_ts, end_ts = @end_ts,
                   actual_duration_value = @actual_duration_value,
                   actual_duration_unit  = @actual_duration_unit,
                   site_id = COALESCE(site_id, (SELECT site_id FROM spaces WHERE id = @resource_id))
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

        // Cancel existing space assignment and write the new one.
        await CancelSpaceAssignmentAsync(db, tx, updatedId.Value, ct);
        if (request.ResourceId.HasValue && request.StartTs.HasValue && request.EndTs.HasValue)
        {
            await WriteResourceAssignmentAsync(db, tx, updatedId.Value, request.ResourceId.Value, request.StartTs.Value, request.EndTs.Value, ct);
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
            // cancel the existing space assignment, then write the new one.
            if (!request.ResourceId.HasValue || !request.StartTs.HasValue || !request.EndTs.HasValue)
                continue;

            var cancel = new NpgsqlBatchCommand(CancelSpaceAssignmentSql);
            cancel.Parameters.AddWithValue("requestId", id);
            cancel.Parameters.AddWithValue("spaceKey", ResourceTypeKeys.Space);
            cancel.Parameters.AddWithValue("cancelled", AssignmentStatuses.Cancelled);
            batch.BatchCommands.Add(cancel);

            var write = new NpgsqlBatchCommand(WriteResourceAssignmentSql);
            write.Parameters.AddWithValue("requestId", id);
            write.Parameters.AddWithValue("resourceId", request.ResourceId.Value);
            write.Parameters.AddWithValue("startUtc", request.StartTs.Value);
            write.Parameters.AddWithValue("endUtc", request.EndTs.Value);
            batch.BatchCommands.Add(write);
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

    private const string CancelSpaceAssignmentSql = @"
            UPDATE resource_assignments ra
            SET assignment_status = @cancelled, updated_at = NOW()
            FROM resources res
            JOIN resource_types rt ON rt.id = res.resource_type_id AND rt.key = @spaceKey
            WHERE ra.request_id = @requestId
              AND ra.resource_id = res.id
              AND ra.assignment_status != @cancelled";

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

    private static async Task CancelSpaceAssignmentAsync(
        NpgsqlConnection conn, NpgsqlTransaction? tx, Guid requestId, CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(CancelSpaceAssignmentSql, conn, tx);
        cmd.Parameters.AddWithValue("requestId", requestId);
        cmd.Parameters.AddWithValue("spaceKey", ResourceTypeKeys.Space);
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
