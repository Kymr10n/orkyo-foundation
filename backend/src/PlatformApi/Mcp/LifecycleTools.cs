using System.ComponentModel;
using Api.Models;
using Api.Repositories;
using Api.Security;
using Api.Services;
using FluentValidation;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Api.PlatformApi.Mcp;

/// <summary>
/// Creating work and taking capacity away: the tools that add to the schedule rather than
/// rearranging what is already in it.
///
/// These widen what a <c>schedule:write</c> token can do — a credential that could previously only
/// move existing work can now create requests, draw dependency edges, and mark a resource
/// unavailable. The scope name is unchanged for compatibility, so tokens issued before these tools
/// existed gained the ability the moment they shipped. Three things contain that deliberately:
///
/// 1. <b><c>create_request</c> cannot schedule.</b> The underlying model accepts start and end
///    timestamps; this tool does not expose them. New work lands in the backlog, and placing it
///    then goes through <c>reschedule_request</c> or <c>auto_schedule_apply</c>, both of which
///    report conflicts or demand a fingerprint.
/// 2. <b>Honest annotations.</b> With a stateless transport the server cannot ask for confirmation,
///    so <c>Destructive</c> is the only signal a client has for interposing one itself.
/// 3. <b>Every call is logged with its acting token</b> — by <see cref="McpToolPipeline"/>, so a
///    tool cannot forget it.
///
/// Absence tools take <see cref="IResourceAbsenceRepository"/> directly, following the precedent
/// set by <c>ResourceEndpoints</c> — there is no absence service, and inventing one for MCP alone
/// would be an abstraction with a single caller.
/// </summary>
[McpServerToolType]
public sealed class LifecycleTools
{
    private readonly IRequestService _requests;
    private readonly IRequestDependencyService _dependencies;
    private readonly IResourceAbsenceRepository _absences;
    private readonly IValidator<CreateRequestRequest> _createRequestValidator;
    private readonly IValidator<CreateDependencyRequest> _dependencyValidator;
    private readonly IValidator<CreateResourceAbsenceRequest> _absenceValidator;
    private readonly IAuthorizationContext _authorization;

    public LifecycleTools(
        IRequestService requests,
        IRequestDependencyService dependencies,
        IResourceAbsenceRepository absences,
        IValidator<CreateRequestRequest> createRequestValidator,
        IValidator<CreateDependencyRequest> dependencyValidator,
        IValidator<CreateResourceAbsenceRequest> absenceValidator,
        IAuthorizationContext authorization)
    {
        _requests = requests;
        _dependencies = dependencies;
        _absences = absences;
        _createRequestValidator = createRequestValidator;
        _dependencyValidator = dependencyValidator;
        _absenceValidator = absenceValidator;
        _authorization = authorization;
    }

    // ── Requests ─────────────────────────────────────────────────────────────

    [McpServerTool(Name = "create_request", Title = "Create a request",
        Destructive = false, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
    [Description("Create a new work request in the backlog. It is created UNSCHEDULED on purpose — "
        + "use reschedule_request or auto_schedule_apply to place it, so the placement is checked "
        + "for conflicts. Calling this twice creates two requests. Requires 'schedule:write'.")]
    public async Task<RequestSummary> CreateRequestAsync(
        [Description("What the work is called.")] string name,
        [Description("How long the work takes, in the given unit.")] int durationValue,
        [Description("Unit for the duration: minutes, hours or days.")] DurationUnit durationUnit,
        [Description("Longer description of the work.")] string? description = null,
        [Description("Site the work belongs to. Omit for site-neutral. Get ids from list_sites.")]
        Guid? siteId = null,
        [Description("Parent request, to nest this under an existing one.")]
        Guid? parentRequestId = null,
        [Description("Resource type keys this work needs, e.g. ['machine'].")]
        IReadOnlyList<string>? targetResourceTypeKeys = null,
        CancellationToken ct = default)
    {
        McpToolGuards.RequireWrite(_authorization, "create_request");

        // StartTs/EndTs are deliberately not parameters: an agent must not be able to place work
        // through the one path that never checks a conflict.
        var request = new CreateRequestRequest
        {
            Name = name,
            Description = description,
            SiteId = siteId,
            ParentRequestId = parentRequestId,
            TargetResourceTypeKeys = targetResourceTypeKeys,
            MinimalDurationValue = durationValue,
            MinimalDurationUnit = durationUnit,
        };
        await McpToolGuards.EnsureValidAsync(_createRequestValidator, request, ct);

        var created = await _requests.CreateAsync(request, ct);
        return created.ToSummary();
    }

    // ── Dependencies ─────────────────────────────────────────────────────────

    [McpServerTool(Name = "link_requests", Title = "Link two requests",
        Destructive = false, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
    [Description("Make one request wait for another: the successor cannot start until the "
        + "predecessor finishes, plus any lag. Cycles are rejected. Requires 'schedule:write'.")]
    public async Task<RequestDependencyInfo> LinkRequestsAsync(
        [Description("The request that must finish first.")] Guid predecessorRequestId,
        [Description("The request that waits.")] Guid successorRequestId,
        [Description("Minutes of gap required between them. Defaults to none.")]
        int lagMinutes = 0,
        CancellationToken ct = default)
    {
        McpToolGuards.RequireWrite(_authorization, "link_requests");

        var request = new CreateDependencyRequest
        {
            PredecessorRequestId = predecessorRequestId,
            LagMinutes = lagMinutes,
        };
        await McpToolGuards.EnsureValidAsync(_dependencyValidator, request, ct);

        var created = await _dependencies.CreateAsync(successorRequestId, request, ct);
        return created;
    }

    [McpServerTool(Name = "unlink_requests", Title = "Remove a dependency",
        Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Remove a dependency edge between two requests. Use list_dependencies to find the "
        + "dependency id. Requires 'schedule:write'.")]
    public async Task<bool> UnlinkRequestsAsync(
        [Description("The request the edge belongs to (the successor).")] Guid requestId,
        [Description("The dependency id, from list_dependencies.")] Guid dependencyId,
        CancellationToken ct = default)
    {
        McpToolGuards.RequireWrite(_authorization, "unlink_requests");

        var removed = await _dependencies.DeleteAsync(requestId, dependencyId, ct);
        if (!removed)
            throw new McpException(
                $"No dependency {dependencyId} on request {requestId}. Call list_dependencies to "
                + "see the edges that exist.");

        return true;
    }

    // ── Resource availability ────────────────────────────────────────────────

    [McpServerTool(Name = "list_resource_absences", Title = "List resource absences",
        ReadOnly = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("List the periods a resource is unavailable — holidays, maintenance, downtime. "
        + "Use it to find the absence id unblock_resource_time needs.")]
    public async Task<IReadOnlyList<ResourceAbsenceInfo>> ListResourceAbsencesAsync(
        [Description("The resource to inspect. Get ids from list_resources.")] Guid resourceId,
        CancellationToken ct = default)
        => await _absences.GetByResourceAsync(resourceId, ct);

    [McpServerTool(Name = "block_resource_time", Title = "Block resource time",
        Destructive = false, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
    [Description("Mark a resource unavailable for a period, so scheduling will not place work on "
        + "it then. This does NOT move bookings that already sit in the window — check "
        + "list_conflicts afterwards. Requires 'schedule:write'.")]
    public async Task<ResourceAbsenceInfo> BlockResourceTimeAsync(
        [Description("The resource to make unavailable.")] Guid resourceId,
        [Description("Why it is unavailable: vacation, sickness, unavailable, maintenance.")]
        AbsenceType absenceType,
        [Description("Short label shown on the calendar.")] string title,
        [Description("Start of the period, ISO-8601 UTC.")] DateTime startTs,
        [Description("End of the period, ISO-8601 UTC.")] DateTime endTs,
        [Description("Longer note about the absence.")] string? notes = null,
        CancellationToken ct = default)
    {
        McpToolGuards.RequireWrite(_authorization, "block_resource_time");

        var request = new CreateResourceAbsenceRequest
        {
            AbsenceType = absenceType,
            Title = title,
            Notes = notes,
            StartTs = startTs,
            EndTs = endTs,
        };
        await McpToolGuards.EnsureValidAsync(_absenceValidator, request, ct);

        var created = await _absences.CreateAsync(resourceId, request, ct);
        return created;
    }

    [McpServerTool(Name = "unblock_resource_time", Title = "Unblock resource time",
        Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Remove an absence, making the resource schedulable again for that period. "
        + "Requires 'schedule:write'.")]
    public async Task<bool> UnblockResourceTimeAsync(
        [Description("The resource the absence belongs to.")] Guid resourceId,
        [Description("The absence id, from list_resource_absences.")] Guid absenceId,
        CancellationToken ct = default)
    {
        McpToolGuards.RequireWrite(_authorization, "unblock_resource_time");

        // Check the absence actually belongs to the named resource before deleting, exactly as the
        // HTTP endpoint does. Without it a hallucinated id would delete an unrelated absence.
        var existing = await _absences.GetByIdAsync(absenceId, ct);
        if (existing is null || existing.ResourceId != resourceId)
            throw new McpException(
                $"No absence {absenceId} on resource {resourceId}. Call list_resource_absences to "
                + "see what that resource actually has.");

        var removed = await _absences.DeleteAsync(absenceId, ct);
        return removed;
    }

}
