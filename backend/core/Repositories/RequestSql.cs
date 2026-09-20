using Api.Helpers;
using Api.Models;
using Npgsql;

namespace Api.Repositories;

/// <summary>
/// The SQL fragments and requirement-hydration reads shared by MORE THAN ONE request
/// repository. The scheduled predicate and the view column list are one rule each, so no copy
/// can drift from another. A fragment with a single consuming file belongs in that file, not
/// here: this is a shared home, not the drawer everything SQL-shaped goes in.
///
/// Callers must have bound the parameters a fragment names — `@cancelled` for the scheduled
/// predicate — before running it; nothing enforces that, and a caller that forgets gets an
/// Npgsql error at run time.
/// </summary>
internal static class RequestSql
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
    internal static string FullyAssignedSql(string requestIdExpr) => $@"
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

    /// <summary>
    /// The requirement read's SELECT list and join, with the criterion columns aliased.
    /// </summary>
    /// <remarks>
    /// One home because the five <c>criterion_*</c> aliases are a contract with
    /// <see cref="RequestMapper.MapRequirementWithCriterionFromReader"/>, which reads by those
    /// names. Both reads — one request, and a batch of them — differ only in their WHERE and
    /// ORDER BY, so they interpolate this and supply their own. Two copies of the alias list
    /// meant renaming one alias broke the other at run time with nothing failing to compile.
    /// </remarks>
    internal const string RequirementSelect = @"
        SELECT rr.id, rr.request_id, rr.criterion_id, rr.value, rr.created_at,
               rr.operator, rr.allowed_values,
               c.id AS criterion_pk, c.name AS criterion_name, c.data_type AS criterion_data_type,
               c.unit AS criterion_unit, c.enum_values AS criterion_enum_values
        FROM request_requirements rr
        JOIN criteria c ON rr.criterion_id = c.id";

    // Columns selected from the view.
    internal const string SelectFromView =
        @"id, name, description, parent_request_id, planning_mode, sort_order,
          site_id,
          target_resource_type_keys,
          request_item_id, icon,
          start_ts, end_ts, earliest_start_ts, latest_end_ts,
          minimal_duration_value, minimal_duration_unit,
          actual_duration_value, actual_duration_unit,
          status, scheduling_settings_apply, created_at, updated_at, assignments,
          predecessor_logic, predecessor_logic_k";

    internal static async Task<RequestInfo> ReadByIdAsync(NpgsqlConnection conn, Guid id, CancellationToken ct = default)
    {
        var cmd = new NpgsqlCommand(
            $"SELECT {SelectFromView} FROM v_requests_with_assignments WHERE id = @id",
            conn);
        cmd.Parameters.AddWithValue("id", id);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return RequestMapper.MapFromReader(reader);
    }

    // ── Requirements helpers ──────────────────────────────────────────────────

    internal static async Task LoadRequirementsForRequests(List<RequestInfo> requests, NpgsqlConnection conn, CancellationToken ct = default)
    {
        // Empty is a no-op, handled here rather than at each call site: every caller used to
        // carry the same `requests.Count > 0` guard, and an unguarded one would have issued
        // `WHERE request_id = ANY('{}')`.
        if (requests.Count == 0) return;

        var requestIds = requests.Select(r => r.Id).ToArray();
        var requirementsMap = new Dictionary<Guid, List<RequestRequirementInfo>>();

        var cmd = new NpgsqlCommand(
            RequirementSelect + @"
            WHERE rr.request_id = ANY(@request_ids)
            ORDER BY rr.request_id, c.name", conn);
        cmd.Parameters.AddWithValue("request_ids", requestIds);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var requestId = reader.GetGuid("request_id");
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
}
