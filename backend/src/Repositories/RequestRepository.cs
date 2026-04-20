using System.Data;
using System.Text.Json;
using Api.Helpers;
using Api.Models;
using Api.Services;
using Npgsql;

namespace Api.Repositories;

public class RequestRepository : IRequestRepository
{
    private const string SelectColumns =
        @"id, name, description, parent_request_id, planning_mode, sort_order,
          space_id, request_item_id,
          start_ts, end_ts, earliest_start_ts, latest_end_ts,
          minimal_duration_value, minimal_duration_unit,
          actual_duration_value, actual_duration_unit,
          status, scheduling_settings_apply, created_at, updated_at";

    private readonly OrgContext _orgContext;
    private readonly IOrgDbConnectionFactory _connectionFactory;

    public RequestRepository(OrgContext orgContext, IOrgDbConnectionFactory connectionFactory)
    {
        _orgContext = orgContext;
        _connectionFactory = connectionFactory;
    }

    public async Task<List<RequestInfo>> GetAllAsync(bool includeRequirements = false)
    {
        await using var db = _connectionFactory.CreateOrgConnection(_orgContext);
        await db.OpenAsync();

        var requests = new List<RequestInfo>();
        var cmd = new NpgsqlCommand(
            $"SELECT {SelectColumns} FROM requests ORDER BY parent_request_id NULLS FIRST, sort_order, created_at DESC", db);

        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                requests.Add(RequestMapper.MapFromReader(reader));
            }
        }

        if (includeRequirements && requests.Any())
        {
            await LoadRequirementsForRequests(requests, db);
        }

        return requests;
    }

    public async Task<PagedResult<RequestInfo>> GetAllAsync(PageRequest page, bool includeRequirements = false)
    {
        await using var db = _connectionFactory.CreateOrgConnection(_orgContext);
        await db.OpenAsync();

        var result = await DbQueryHelper.ExecutePagedQueryAsync(
            db,
            page,
            countSql: "SELECT COUNT(*) FROM requests",
            querySql: $"SELECT {SelectColumns} FROM requests ORDER BY parent_request_id NULLS FIRST, sort_order, created_at DESC LIMIT @limit OFFSET @offset",
            addParams: null,
            mapper: RequestMapper.MapFromReader);

        if (includeRequirements && result.Items.Any())
        {
            var items = result.Items.ToList();
            await LoadRequirementsForRequests(items, db);
            return result with { Items = items };
        }

        return result;
    }

    public async Task<List<RequestInfo>> GetScheduledBySiteAsync(Guid siteId)
    {
        await using var db = _connectionFactory.CreateOrgConnection(_orgContext);
        await db.OpenAsync();

        var cmd = new NpgsqlCommand($@"
            SELECT {SelectColumns}
            FROM requests
            WHERE scheduling_settings_apply = true
              AND start_ts IS NOT NULL
              AND space_id IN (SELECT id FROM spaces WHERE site_id = @siteId)", db);
        cmd.Parameters.AddWithValue("siteId", siteId);

        var requests = new List<RequestInfo>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            requests.Add(RequestMapper.MapFromReader(reader));
        }

        return requests;
    }

    public async Task<RequestInfo?> GetByIdAsync(Guid id, bool includeRequirements = true)
    {
        await using var db = _connectionFactory.CreateOrgConnection(_orgContext);
        await db.OpenAsync();

        var cmd = new NpgsqlCommand(
            $"SELECT {SelectColumns} FROM requests WHERE id = @id", db);
        cmd.Parameters.AddWithValue("id", id);

        using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        var request = RequestMapper.MapFromReader(reader);
        reader.Close();

        if (includeRequirements)
        {
            request = request with { Requirements = await LoadRequirements(id, db) };
        }

        return request;
    }

    public async Task<RequestInfo> CreateAsync(CreateRequestRequest request)
    {
        await using var db = _connectionFactory.CreateOrgConnection(_orgContext);
        await db.OpenAsync();

        // Validate space_id if provided
        if (request.SpaceId.HasValue && !await DbQueryHelper.ExistsAsync(db, "spaces", request.SpaceId.Value))
        {
            throw new ArgumentException("Invalid space_id: space does not exist");
        }

        await using var transaction = await db.BeginTransactionAsync();

        try
        {
            var cmd = new NpgsqlCommand(
                $@"INSERT INTO requests (name, description, parent_request_id, planning_mode, sort_order,
                                        space_id, request_item_id,
                                        start_ts, end_ts, earliest_start_ts, latest_end_ts,
                                        minimal_duration_value, minimal_duration_unit,
                                        actual_duration_value, actual_duration_unit,
                                        status, scheduling_settings_apply)
                   VALUES (@name, @description, @parent_request_id, @planning_mode, @sort_order,
                           @space_id, @request_item_id,
                           @start_ts, @end_ts, @earliest_start_ts, @latest_end_ts,
                           @minimal_duration_value, @minimal_duration_unit,
                           @actual_duration_value, @actual_duration_unit,
                           @status, @scheduling_settings_apply)
                   RETURNING {SelectColumns}",
                db, transaction);

            cmd.Parameters.AddWithValue("name", request.Name);
            cmd.Parameters.AddWithValue("description", (object?)request.Description ?? DBNull.Value);
            cmd.Parameters.AddWithValue("parent_request_id", (object?)request.ParentRequestId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("planning_mode", EnumMapper.ToDbValue(request.PlanningMode));
            cmd.Parameters.AddWithValue("sort_order", request.SortOrder);
            cmd.Parameters.AddWithValue("space_id", (object?)request.SpaceId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("request_item_id", (object?)request.RequestItemId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("start_ts", (object?)request.StartTs ?? DBNull.Value);
            cmd.Parameters.AddWithValue("end_ts", (object?)request.EndTs ?? DBNull.Value);
            cmd.Parameters.AddWithValue("earliest_start_ts", (object?)request.EarliestStartTs ?? DBNull.Value);
            cmd.Parameters.AddWithValue("latest_end_ts", (object?)request.LatestEndTs ?? DBNull.Value);
            cmd.Parameters.AddWithValue("minimal_duration_value", request.MinimalDurationValue);
            cmd.Parameters.AddWithValue("minimal_duration_unit", EnumMapper.ToDbValue(request.MinimalDurationUnit));
            cmd.Parameters.AddWithValue("actual_duration_value", (object?)request.ActualDurationValue ?? DBNull.Value);
            cmd.Parameters.AddWithValue("actual_duration_unit", request.ActualDurationUnit.HasValue ? EnumMapper.ToDbValue(request.ActualDurationUnit.Value) : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("status", EnumMapper.ToDbValue(request.Status));
            cmd.Parameters.AddWithValue("scheduling_settings_apply", request.SchedulingSettingsApply);

            using var reader = await cmd.ExecuteReaderAsync();
            await reader.ReadAsync();
            var createdRequest = RequestMapper.MapFromReader(reader);
            reader.Close();

            if (request.Requirements != null && request.Requirements.Any())
            {
                var requirements = await CreateRequirements(createdRequest.Id, request.Requirements, db, transaction);
                createdRequest = createdRequest with { Requirements = requirements };
            }
            else
            {
                createdRequest = createdRequest with { Requirements = new List<RequestRequirementInfo>() };
            }

            await transaction.CommitAsync();
            return createdRequest;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    // Table-driven mapping of request fields → SQL columns.
    private static readonly (string Column, Func<UpdateRequestRequest, bool> HasValue, Func<UpdateRequestRequest, object> GetValue)[] UpdateFieldMap =
    {
        ("name",                      r => r.Name != null,                    r => r.Name!),
        ("description",               r => r.Description != null,             r => r.Description!),
        ("parent_request_id",         r => r.ParentRequestId.HasValue,        r => r.ParentRequestId!.Value),
        ("planning_mode",             r => r.PlanningMode.HasValue,           r => EnumMapper.ToDbValue(r.PlanningMode!.Value)),
        ("sort_order",                r => r.SortOrder.HasValue,              r => r.SortOrder!.Value),
        ("space_id",                  r => r.SpaceId.HasValue,                r => r.SpaceId!.Value),
        ("request_item_id",           r => r.RequestItemId != null,           r => r.RequestItemId!),
        ("start_ts",                  r => r.StartTs.HasValue,                r => r.StartTs!.Value),
        ("end_ts",                    r => r.EndTs.HasValue,                  r => r.EndTs!.Value),
        ("earliest_start_ts",         r => r.EarliestStartTs.HasValue,        r => r.EarliestStartTs!.Value),
        ("latest_end_ts",             r => r.LatestEndTs.HasValue,            r => r.LatestEndTs!.Value),
        ("minimal_duration_value",    r => r.MinimalDurationValue.HasValue,   r => r.MinimalDurationValue!.Value),
        ("minimal_duration_unit",     r => r.MinimalDurationUnit.HasValue,    r => EnumMapper.ToDbValue(r.MinimalDurationUnit!.Value)),
        ("actual_duration_value",     r => r.ActualDurationValue.HasValue,    r => r.ActualDurationValue!.Value),
        ("actual_duration_unit",      r => r.ActualDurationUnit.HasValue,     r => EnumMapper.ToDbValue(r.ActualDurationUnit!.Value)),
        ("status",                    r => r.Status.HasValue,                 r => EnumMapper.ToDbValue(r.Status!.Value)),
        ("scheduling_settings_apply", r => r.SchedulingSettingsApply.HasValue, r => r.SchedulingSettingsApply!.Value),
    };

    public async Task<RequestInfo?> UpdateAsync(Guid id, UpdateRequestRequest request)
    {
        await using var db = _connectionFactory.CreateOrgConnection(_orgContext);
        await db.OpenAsync();

        var fetchCmd = new NpgsqlCommand("SELECT start_ts, end_ts FROM requests WHERE id = @id", db);
        fetchCmd.Parameters.AddWithValue("id", id);

        DateTime? currentStartTs;
        DateTime? currentEndTs;
        await using (var fetchReader = await fetchCmd.ExecuteReaderAsync())
        {
            if (!await fetchReader.ReadAsync())
            {
                return null;
            }
            currentStartTs = fetchReader.IsDBNull(0) ? null : fetchReader.GetDateTime(0);
            currentEndTs = fetchReader.IsDBNull(1) ? null : fetchReader.GetDateTime(1);
        }

        var finalStartTs = request.StartTs ?? currentStartTs;
        var finalEndTs = request.EndTs ?? currentEndTs;
        if (finalStartTs.HasValue && finalEndTs.HasValue && finalEndTs.Value <= finalStartTs.Value)
        {
            throw new ArgumentException("End time must be after start time");
        }

        var setClauses = new List<string>();
        var parameters = new List<(string Name, object Value)>();
        foreach (var (column, hasValue, getValue) in UpdateFieldMap)
        {
            if (!hasValue(request)) continue;
            setClauses.Add($"{column} = @{column}");
            parameters.Add((column, getValue(request)));
        }

        if (setClauses.Count == 0 && request.Requirements == null)
        {
            throw new ArgumentException("No fields to update");
        }

        await using var transaction = request.Requirements != null
            ? await db.BeginTransactionAsync()
            : null;

        RequestInfo updatedRequest;
        if (setClauses.Count > 0)
        {
            var cmd = new NpgsqlCommand
            {
                Connection = db,
                Transaction = transaction,
                CommandText = $"UPDATE requests SET {string.Join(", ", setClauses)} WHERE id = @id RETURNING {SelectColumns}",
            };
            cmd.Parameters.AddWithValue("id", id);
            foreach (var (name, value) in parameters)
            {
                cmd.Parameters.AddWithValue(name, value);
            }

            await using var reader = await cmd.ExecuteReaderAsync();
            await reader.ReadAsync();
            updatedRequest = RequestMapper.MapFromReader(reader);
        }
        else
        {
            var existing = await GetByIdAsync(id, includeRequirements: false);
            if (existing == null) return null;
            updatedRequest = existing;
        }

        if (request.Requirements != null)
        {
            await using var deleteCmd = new NpgsqlCommand(
                "DELETE FROM request_requirements WHERE request_id = @request_id", db, transaction);
            deleteCmd.Parameters.AddWithValue("request_id", id);
            await deleteCmd.ExecuteNonQueryAsync();

            var requirements = request.Requirements.Count > 0
                ? await CreateRequirements(id, request.Requirements, db, transaction!)
                : new List<RequestRequirementInfo>();

            updatedRequest = updatedRequest with { Requirements = requirements };
            await transaction!.CommitAsync();
        }
        else
        {
            updatedRequest = updatedRequest with { Requirements = await LoadRequirements(id, db) };
        }

        return updatedRequest;
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        await using var db = _connectionFactory.CreateOrgConnection(_orgContext);
        await db.OpenAsync();
        var rowsAffected = await DbQueryHelper.ExecuteDeleteAsync(db, "DELETE FROM requests WHERE id = @id", id);
        return rowsAffected > 0;
    }

    public async Task<bool> ExistsAsync(Guid id)
    {
        await using var db = _connectionFactory.CreateOrgConnection(_orgContext);
        await db.OpenAsync();
        return await DbQueryHelper.ExistsAsync(db, "requests", id);
    }

    public async Task<RequestInfo?> UpdateScheduleAsync(Guid id, ScheduleRequestRequest request)
    {
        await using var db = _connectionFactory.CreateOrgConnection(_orgContext);
        await db.OpenAsync();

        if (!await DbQueryHelper.ExistsAsync(db, "requests", id))
        {
            return null;
        }

        if (request.SpaceId.HasValue && !await DbQueryHelper.ExistsAsync(db, "spaces", request.SpaceId.Value))
        {
            throw new ArgumentException("Invalid space_id: space does not exist");
        }

        int? actualDurationValue = request.ActualDurationValue;
        string? actualDurationUnit = request.ActualDurationUnit.HasValue
            ? EnumMapper.ToDbValue(request.ActualDurationUnit.Value)
            : null;

        if (actualDurationValue == null && request.StartTs.HasValue && request.EndTs.HasValue)
        {
            var totalMinutes = (int)(request.EndTs.Value - request.StartTs.Value).TotalMinutes;
            actualDurationValue = totalMinutes;
            actualDurationUnit = "minutes";
        }

        var cmd = new NpgsqlCommand(
            $@"UPDATE requests
               SET space_id = @space_id, start_ts = @start_ts, end_ts = @end_ts,
                   actual_duration_value = @actual_duration_value, actual_duration_unit = @actual_duration_unit
               WHERE id = @id
               RETURNING {SelectColumns}",
            db);

        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("space_id", (object?)request.SpaceId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("start_ts", (object?)request.StartTs ?? DBNull.Value);
        cmd.Parameters.AddWithValue("end_ts", (object?)request.EndTs ?? DBNull.Value);
        cmd.Parameters.AddWithValue("actual_duration_value", (object?)actualDurationValue ?? DBNull.Value);
        cmd.Parameters.AddWithValue("actual_duration_unit", (object?)actualDurationUnit ?? DBNull.Value);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return RequestMapper.MapFromReader(reader);
        }

        return null;
    }

    public async Task<int> BatchUpdateSchedulesAsync(IReadOnlyList<(Guid Id, ScheduleRequestRequest Data)> updates)
    {
        if (updates.Count == 0) return 0;

        await using var db = _connectionFactory.CreateOrgConnection(_orgContext);
        await db.OpenAsync();
        await using var tx = await db.BeginTransactionAsync();

        await using var batch = new NpgsqlBatch(db, tx);
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
                  SET space_id = @space_id, start_ts = @start_ts, end_ts = @end_ts,
                      actual_duration_value = @actual_duration_value, actual_duration_unit = @actual_duration_unit
                  WHERE id = @id");
            cmd.Parameters.AddWithValue("id", id);
            cmd.Parameters.AddWithValue("space_id", (object?)request.SpaceId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("start_ts", (object?)request.StartTs ?? DBNull.Value);
            cmd.Parameters.AddWithValue("end_ts", (object?)request.EndTs ?? DBNull.Value);
            cmd.Parameters.AddWithValue("actual_duration_value", (object?)actualDurationValue ?? DBNull.Value);
            cmd.Parameters.AddWithValue("actual_duration_unit", (object?)actualDurationUnit ?? DBNull.Value);
            batch.BatchCommands.Add(cmd);
        }

        var rowsAffected = await batch.ExecuteNonQueryAsync();
        await tx.CommitAsync();
        return rowsAffected;
    }

    public async Task<RequestRequirementInfo> AddRequirementAsync(Guid requestId, AddRequirementRequest requirement)
    {
        await using var db = _connectionFactory.CreateOrgConnection(_orgContext);
        await db.OpenAsync();

        if (!await DbQueryHelper.ExistsAsync(db, "requests", requestId))
        {
            throw new InvalidOperationException($"Request with ID {requestId} not found");
        }

        if (!await DbQueryHelper.ExistsAsync(db, "criteria", requirement.CriterionId))
        {
            throw new ArgumentException("Invalid criterion_id: criterion does not exist");
        }

        var cmd = new NpgsqlCommand(@"
            INSERT INTO request_requirements (request_id, criterion_id, value)
            VALUES (@request_id, @criterion_id, @value::jsonb)
            ON CONFLICT (request_id, criterion_id)
            DO UPDATE SET value = EXCLUDED.value
            RETURNING id, request_id, criterion_id, value, created_at
        ", db);

        cmd.Parameters.AddWithValue("request_id", requestId);
        cmd.Parameters.AddWithValue("criterion_id", requirement.CriterionId);
        cmd.Parameters.AddWithValue("value", requirement.Value.GetRawText());

        using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        return RequestMapper.MapRequirementFromReader(reader);
    }

    public async Task<bool> DeleteRequirementAsync(Guid requestId, Guid requirementId)
    {
        await using var db = _connectionFactory.CreateOrgConnection(_orgContext);
        await db.OpenAsync();

        var cmd = new NpgsqlCommand("DELETE FROM request_requirements WHERE id = @id AND request_id = @request_id", db);
        cmd.Parameters.AddWithValue("id", requirementId);
        cmd.Parameters.AddWithValue("request_id", requestId);

        var rowsAffected = await cmd.ExecuteNonQueryAsync();
        return rowsAffected > 0;
    }

    private async Task<List<RequestRequirementInfo>> LoadRequirements(Guid requestId, NpgsqlConnection db)
    {
        var requirements = new List<RequestRequirementInfo>();
        var cmd = new NpgsqlCommand(@"
            SELECT rr.id, rr.request_id, rr.criterion_id, rr.value, rr.created_at,
                   c.id, c.name, c.data_type, c.unit, c.enum_values
            FROM request_requirements rr
            JOIN criteria c ON rr.criterion_id = c.id
            WHERE rr.request_id = @request_id
            ORDER BY c.name
        ", db);
        cmd.Parameters.AddWithValue("request_id", requestId);

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            requirements.Add(RequestMapper.MapRequirementWithCriterionFromReader(reader));
        }

        return requirements;
    }

    private async Task LoadRequirementsForRequests(List<RequestInfo> requests, NpgsqlConnection db)
    {
        var requestIds = requests.Select(r => r.Id).ToArray();
        var requirementsMap = new Dictionary<Guid, List<RequestRequirementInfo>>();

        var cmd = new NpgsqlCommand(@"
            SELECT rr.id, rr.request_id, rr.criterion_id, rr.value, rr.created_at,
                   c.id, c.name, c.data_type, c.unit, c.enum_values
            FROM request_requirements rr
            JOIN criteria c ON rr.criterion_id = c.id
            WHERE rr.request_id = ANY(@request_ids)
            ORDER BY rr.request_id, c.name
        ", db);
        cmd.Parameters.AddWithValue("request_ids", requestIds);

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var requestId = reader.GetGuid(1);
            if (!requirementsMap.ContainsKey(requestId))
            {
                requirementsMap[requestId] = new List<RequestRequirementInfo>();
            }

            requirementsMap[requestId].Add(RequestMapper.MapRequirementWithCriterionFromReader(reader));
        }

        for (int i = 0; i < requests.Count; i++)
        {
            requests[i] = requests[i] with
            {
                Requirements = requirementsMap.TryGetValue(requests[i].Id, out var reqs)
                    ? reqs
                    : new List<RequestRequirementInfo>()
            };
        }
    }

    private async Task<List<RequestRequirementInfo>> CreateRequirements(
        Guid requestId,
        List<CreateRequestRequirementRequest> requirements,
        NpgsqlConnection db,
        NpgsqlTransaction transaction)
    {
        var valueClauses = new List<string>();
        var cmd = new NpgsqlCommand { Connection = db, Transaction = transaction };

        for (int i = 0; i < requirements.Count; i++)
        {
            valueClauses.Add($"(@request_id, @criterion_id_{i}, @value_{i}::jsonb)");
            cmd.Parameters.AddWithValue($"criterion_id_{i}", requirements[i].CriterionId);
            cmd.Parameters.AddWithValue($"value_{i}", requirements[i].Value.GetRawText());
        }

        cmd.Parameters.AddWithValue("request_id", requestId);
        cmd.CommandText = $@"
            INSERT INTO request_requirements (request_id, criterion_id, value)
            VALUES {string.Join(", ", valueClauses)}
            RETURNING id, request_id, criterion_id, value, created_at";

        var inserted = new List<RequestRequirementInfo>();
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                inserted.Add(RequestMapper.MapRequirementFromReader(reader));
            }
        }

        var criterionIds = requirements.Select(r => r.CriterionId).Distinct().ToArray();
        var criteriaCmd = new NpgsqlCommand(@"
            SELECT id, name, data_type, unit, enum_values
            FROM criteria
            WHERE id = ANY(@ids)", db, transaction);
        criteriaCmd.Parameters.AddWithValue("ids", criterionIds);

        var criteriaMap = new Dictionary<Guid, CriterionBasicInfo>();
        await using (var criteriaReader = await criteriaCmd.ExecuteReaderAsync())
        {
            while (await criteriaReader.ReadAsync())
            {
                var criterion = RequestMapper.MapCriterionFromReader(criteriaReader);
                criteriaMap[criterion.Id] = criterion;
            }
        }

        for (int i = 0; i < inserted.Count; i++)
        {
            if (criteriaMap.TryGetValue(inserted[i].CriterionId, out var criterion))
            {
                inserted[i] = inserted[i] with { Criterion = criterion };
            }
        }

        return inserted;
    }

    public async Task<List<RequestInfo>> GetChildrenAsync(Guid parentId)
    {
        await using var db = _connectionFactory.CreateOrgConnection(_orgContext);
        await db.OpenAsync();

        var cmd = new NpgsqlCommand(
            $"SELECT {SelectColumns} FROM requests WHERE parent_request_id = @parent_id ORDER BY sort_order, created_at",
            db);
        cmd.Parameters.AddWithValue("parent_id", parentId);

        var results = new List<RequestInfo>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(RequestMapper.MapFromReader(reader));
        }
        return results;
    }

    public async Task<RequestInfo?> MoveAsync(Guid id, Guid? newParentId, int sortOrder)
    {
        await using var db = _connectionFactory.CreateOrgConnection(_orgContext);
        await db.OpenAsync();

        var cmd = new NpgsqlCommand(
            $@"UPDATE requests
               SET parent_request_id = @parent_id, sort_order = @sort_order, updated_at = NOW()
               WHERE id = @id
               RETURNING {SelectColumns}",
            db);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("parent_id", (object?)newParentId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("sort_order", sortOrder);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return RequestMapper.MapFromReader(reader);
        }
        return null;
    }

    public async Task<int> GetDescendantCountAsync(Guid id)
    {
        await using var db = _connectionFactory.CreateOrgConnection(_orgContext);
        await db.OpenAsync();

        var cmd = new NpgsqlCommand(
            @"WITH RECURSIVE subtree AS (
                SELECT id FROM requests WHERE parent_request_id = @id
                UNION ALL
                SELECT r.id FROM requests r JOIN subtree s ON r.parent_request_id = s.id
              )
              SELECT COUNT(*)::int FROM subtree",
            db);
        cmd.Parameters.AddWithValue("id", id);

        return (int)(await cmd.ExecuteScalarAsync())!;
    }

    public async Task<bool> WouldCreateCycleAsync(Guid requestId, Guid newParentId)
    {
        if (requestId == newParentId) return true;

        await using var db = _connectionFactory.CreateOrgConnection(_orgContext);
        await db.OpenAsync();

        var cmd = new NpgsqlCommand(
            @"WITH RECURSIVE ancestors AS (
                SELECT parent_request_id FROM requests WHERE id = @new_parent_id
                UNION ALL
                SELECT r.parent_request_id FROM requests r JOIN ancestors a ON r.id = a.parent_request_id
                WHERE r.parent_request_id IS NOT NULL
              )
              SELECT EXISTS(SELECT 1 FROM ancestors WHERE parent_request_id = @request_id)",
            db);
        cmd.Parameters.AddWithValue("request_id", requestId);
        cmd.Parameters.AddWithValue("new_parent_id", newParentId);

        return (bool)(await cmd.ExecuteScalarAsync())!;
    }

    public async Task<PlanningMode?> GetPlanningModeAsync(Guid id)
    {
        await using var db = _connectionFactory.CreateOrgConnection(_orgContext);
        await db.OpenAsync();

        var cmd = new NpgsqlCommand("SELECT planning_mode FROM requests WHERE id = @id", db);
        cmd.Parameters.AddWithValue("id", id);

        var result = await cmd.ExecuteScalarAsync();
        if (result is null or DBNull) return null;

        return EnumMapper.ToPlanningMode((string)result);
    }

    public async Task<bool> HasChildrenAsync(Guid id)
    {
        await using var db = _connectionFactory.CreateOrgConnection(_orgContext);
        await db.OpenAsync();

        var cmd = new NpgsqlCommand(
            "SELECT EXISTS(SELECT 1 FROM requests WHERE parent_request_id = @id)", db);
        cmd.Parameters.AddWithValue("id", id);

        return (bool)(await cmd.ExecuteScalarAsync())!;
    }

    public async Task<int> DeleteSubtreeAsync(Guid id)
    {
        await using var db = _connectionFactory.CreateOrgConnection(_orgContext);
        await db.OpenAsync();

        var count = await GetDescendantCountAsync(id);

        var cmd = new NpgsqlCommand("DELETE FROM requests WHERE id = @id", db);
        cmd.Parameters.AddWithValue("id", id);
        var deleted = await cmd.ExecuteNonQueryAsync();

        return deleted > 0 ? count + 1 : 0;
    }
}
