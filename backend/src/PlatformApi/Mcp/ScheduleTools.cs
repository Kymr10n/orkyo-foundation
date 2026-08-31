using System.ComponentModel;
using Api.Models;
using Api.Security;
using Api.Services;
using FluentValidation;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Api.PlatformApi.Mcp;

/// <summary>
/// The schedule board as an agent sees it: what work exists, what it can run on, where it clashes,
/// and the two ways to move a single piece of it.
///
/// Every tool is a thin wrapper over the same service an HTTP endpoint calls — no business logic
/// lives here. Two rules keep it honest:
///
/// 1. <b>Authorization is not re-implemented.</b> The endpoint group already required tenant
///    membership; a write tool additionally calls <see cref="McpToolGuards.RequireWrite"/>, which
///    reads the same Role &gt;= Editor threshold <c>RequireEditAccess</c> enforces over HTTP. It is
///    checked per tool rather than by the group's verb-aware write gate because MCP puts every call,
///    read and write alike, behind one POST: gating on the verb would demand Editor to list tools.
/// 2. <b>Conflicts are surfaced, never swallowed.</b> A scheduling call that produces a conflict
///    returns it to the model, which is what the UI does for a human.
///
/// The annotations matter as much as the code. A client uses <c>ReadOnly</c> and <c>Destructive</c>
/// to decide whether to interpose a confirmation, and under our stateless transport — where the
/// server cannot elicit one itself — they are the only signal it has.
/// </summary>
[McpServerToolType]
public sealed class ScheduleTools
{
    private readonly IRequestService _requests;
    private readonly ISchedulingService _scheduling;
    private readonly IResourceService _resources;
    private readonly IResourceAssignmentService _assignments;
    private readonly IConflictService _conflicts;
    private readonly ISiteService _sites;
    private readonly IValidator<ScheduleRequestRequest> _scheduleValidator;
    private readonly IValidator<CreateResourceAssignmentRequest> _assignmentValidator;
    private readonly IAuthorizationContext _authorization;

    public ScheduleTools(
        IRequestService requests,
        ISchedulingService scheduling,
        IResourceService resources,
        IResourceAssignmentService assignments,
        IConflictService conflicts,
        ISiteService sites,
        IValidator<ScheduleRequestRequest> scheduleValidator,
        IValidator<CreateResourceAssignmentRequest> assignmentValidator,
        IAuthorizationContext authorization)
    {
        _requests = requests;
        _scheduling = scheduling;
        _resources = resources;
        _assignments = assignments;
        _conflicts = conflicts;
        _sites = sites;
        _scheduleValidator = scheduleValidator;
        _assignmentValidator = assignmentValidator;
        _authorization = authorization;
    }

    [McpServerTool(Name = "list_sites", Title = "List sites",
        ReadOnly = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("List this tenant's sites. Most planning tools take an optional site id, and "
        + "auto_schedule_preview requires one — call this first to find it.")]
    public async Task<IReadOnlyList<SiteInfo>> ListSitesAsync(CancellationToken ct = default)
        => await _sites.GetAllAsync(ct);

    [McpServerTool(Name = "list_requests", Title = "List requests",
        ReadOnly = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("List work requests in this tenant. Use it to find the request id a scheduling "
        + "tool needs, and to see what is already scheduled.")]
    public async Task<RequestListResult> ListRequestsAsync(
        [Description("Only unscheduled requests (the backlog) when true; only scheduled when false.")]
        bool? scheduled = null,
        [Description("Case-insensitive substring match on the request name.")]
        string? nameContains = null,
        [Description("Maximum rows to return (1-200, default 50).")]
        int limit = 50,
        CancellationToken ct = default)
    {
        var capped = Math.Clamp(limit, 1, 200);

        // SearchAsync already applies name/scheduled filtering and a limit in SQL; going through
        // it rather than filtering a full list in memory keeps the tool cheap on large tenants.
        var results = await _requests.SearchAsync(nameContains, scheduled, capped, ct: ct);

        return new RequestListResult(results.Count, [.. results.Select(r => r.ToSummary())]);
    }

    [McpServerTool(Name = "list_resources", Title = "List resources",
        ReadOnly = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("List the resources work can be scheduled onto: stations (machines, workstations, "
        + "rooms) and assets (people, tools). Use it to find the resource id an assignment needs.")]
    public async Task<ResourceListResult> ListResourcesAsync(
        [Description("Filter to one resource type by its key, e.g. 'machine' or 'person'.")]
        string? resourceTypeKey = null,
        [Description("Case-insensitive substring match on the resource name.")]
        string? search = null,
        [Description("Include deactivated resources. Defaults to active only.")]
        bool includeInactive = false,
        CancellationToken ct = default)
    {
        var filter = new ResourceListFilter
        {
            ResourceTypeKey = resourceTypeKey,
            Search = search,
            IsActive = includeInactive ? null : true,
        };

        var results = await _resources.GetAllAsync(filter, ct);

        return new ResourceListResult(results.Count, [.. results.Select(r => r.ToSummary())]);
    }

    [McpServerTool(Name = "list_conflicts", Title = "List conflicts",
        ReadOnly = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("List scheduling conflicts in this tenant — overbooked resources, capability "
        + "mismatches, work placed outside availability. Call it after changing the schedule to "
        + "check the change did not break something else.")]
    public async Task<ConflictListResult> ListConflictsAsync(
        [Description("Start of the window to check, ISO-8601 UTC. Omit for all time.")]
        DateTime? from = null,
        [Description("End of the window to check, ISO-8601 UTC. Omit for all time.")]
        DateTime? to = null,
        CancellationToken ct = default)
    {
        var conflicts = await _conflicts.GetAllAsync(from, to, ct);

        return new ConflictListResult(
            conflicts.Count,
            [.. conflicts.Select(c => new RequestConflictSummary(
                c.RequestId, [.. c.Conflicts.Select(d => d.ToSummary())]))]);
    }

    // Destructive because it overwrites an existing placement; idempotent because the same
    // arguments land the request in the same place however many times they are sent.
    [McpServerTool(Name = "reschedule_request", Title = "Reschedule a request",
        Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Move a request to a new time window on a resource. This is the same operation "
        + "as dragging the request on the schedule board: it rewrites the request's own schedule "
        + "and its resource booking together. To move, provide resourceId, startTs AND endTs "
        + "(find the current resource with list_requests if it should stay put). To unschedule, "
        + "omit all three. Requires 'schedule:write'.")]
    public async Task<RescheduleResult> RescheduleRequestAsync(
        [Description("The request to move.")] Guid requestId,
        [Description("New start, ISO-8601 UTC. Required together with endTs and resourceId.")]
        DateTime? startTs = null,
        [Description("New end, ISO-8601 UTC. Required together with startTs and resourceId.")]
        DateTime? endTs = null,
        [Description("Resource to place the work on. Required when scheduling; omit only to unschedule.")]
        Guid? resourceId = null,
        CancellationToken ct = default)
    {
        McpToolGuards.RequireWrite(_authorization, "reschedule_request");

        var requested = new ScheduleRequestRequest
        {
            ResourceId = resourceId,
            StartTs = startTs,
            EndTs = endTs,
        };
        // The same validator PATCH /api/requests/{id}/schedule runs. Skipping it let a tool
        // persist end < start or a lone start with no end — shapes the endpoint refuses.
        await McpToolGuards.EnsureValidAsync(_scheduleValidator, requested, ct);

        // Both calls, in this order — the same pair the HTTP endpoint makes. The scheduling
        // service normalises the window against the request's duration and calendar before the
        // write; skipping it would let a tool produce a schedule the UI could not have produced.
        var adjusted = await _scheduling.ApplySchedulingToScheduleAsync(requestId, requested, ct);
        var updated = await _requests.UpdateScheduleAsync(requestId, adjusted, ct);

        if (updated is null)
            throw new McpException($"No request found with id {requestId}.");

        // Report the conflicts this request now has: a move that succeeds can still be a move that
        // overbooks, and the agent needs to see that rather than assume success means clean.
        // Tenant-wide rather than windowed to the new dates — the same call the board makes —
        // because a precedence conflict can involve work far outside the window just moved.
        var conflicts = await _conflicts.GetAllAsync(ct: ct);
        var mine = conflicts.FirstOrDefault(c => c.RequestId == requestId);

        return new RescheduleResult(
            updated.ToSummary(),
            mine is null ? [] : [.. mine.Conflicts.Select(d => d.ToSummary())]);
    }

    // Additive rather than destructive — it creates a booking without overwriting one — and NOT
    // idempotent: sending it twice books the resource twice.
    [McpServerTool(Name = "assign_resource", Title = "Assign a resource",
        Destructive = false, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
    [Description("Book a resource onto a request for a time window. Returns the assignment, or "
        + "the conflict that blocked it. Requires 'schedule:write'.")]
    public async Task<AssignResourceResult> AssignResourceAsync(
        [Description("The request the resource will work on.")] Guid requestId,
        [Description("The resource to book.")] Guid resourceId,
        [Description("Start of the booking, ISO-8601 UTC.")] DateTime startUtc,
        [Description("End of the booking, ISO-8601 UTC.")] DateTime endUtc,
        [Description("Share of the resource's capacity, 0-100. Omit for the resource's default.")]
        decimal? allocationPercent = null,
        CancellationToken ct = default)
    {
        McpToolGuards.RequireWrite(_authorization, "assign_resource");

        var request = new CreateResourceAssignmentRequest
        {
            RequestId = requestId,
            ResourceId = resourceId,
            StartUtc = startUtc,
            EndUtc = endUtc,
            AllocationPercent = allocationPercent,
        };
        // The same validator the HTTP endpoint runs. Its own docstring is the reason this
        // cannot be skipped: a zero-length window "silently matches nothing in the overlap
        // queries" — a booking invisible to conflict detection.
        await McpToolGuards.EnsureValidAsync(_assignmentValidator, request, ct);

        var (assignment, conflict) = await _assignments.CreateAsync(request, ct);

        // A blocking conflict is a real answer, not an error: the agent is expected to read it and
        // pick a different resource or window. A soft conflict rides along with a successful
        // booking for the same reason — silence would imply it was clean.
        return new AssignResourceResult(
            Assigned: assignment is not null,
            Assignment: assignment?.ToSummary(),
            Conflict: conflict?.ToSummary());
    }
}
