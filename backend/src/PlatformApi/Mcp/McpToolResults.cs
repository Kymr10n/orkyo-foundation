using Api.Models;

namespace Api.PlatformApi.Mcp;

/// <summary>
/// Result shapes for the tools that deliberately return a <em>projection</em> rather than a domain
/// record — a subset, a derivation, or a pairing the domain has no type for.
///
/// Tools whose service already produces a well-shaped response record return it directly instead of
/// appearing here: <c>CriticalPathResult</c>, <c>RequestDependencyInfo</c>, <c>InsightsBottlenecks</c>,
/// <c>AutoSchedulePreviewResponse</c>, <c>ResourceAbsenceInfo</c> and <c>SiteInfo</c> are already
/// serialized verbatim by HTTP endpoints, so mirroring them here would be a hand-copy that drifts
/// the first time a field is added.
///
/// These records live beside the tools rather than in <c>core/Models</c> because they are wire
/// shapes for one transport; putting them in core would enter foundation's published package
/// surface and make every future field a compatibility question.
///
/// Serialization note: the MCP SDK uses <c>JsonSerializerDefaults.Web</c>, so these PascalCase
/// members emit the same camelCase the previous hand-written anonymous objects produced. Renaming a
/// member is therefore a breaking change for any connected agent.
/// </summary>
internal static class McpToolResultsDoc;

// ── list_requests ────────────────────────────────────────────────────────────

/// <param name="IsScheduled">
/// Derived, not stored: a request is scheduled once it has both ends. Precomputed here because it
/// is the single most common thing an agent branches on, and making it infer the rule from two
/// nullable timestamps invites it to get the edge case wrong.
/// </param>
public sealed record RequestSummary(
    Guid Id,
    string Name,
    DateTime? StartTs,
    DateTime? EndTs,
    Guid? SiteId,
    bool IsScheduled);

public sealed record RequestListResult(int Count, IReadOnlyList<RequestSummary> Requests);

// ── list_resources ───────────────────────────────────────────────────────────

/// <summary>
/// The identity and bookability of a resource. Deliberately omits floorplan geometry, attributes
/// and capability sets: they are bulk data an agent does not reason over, and they would dominate
/// the response on a tenant with hundreds of resources.
/// </summary>
public sealed record ResourceSummary(
    Guid Id,
    string Name,
    string ResourceTypeKey,
    bool IsActive,
    Guid? HomeSiteId);

public sealed record ResourceListResult(int Count, IReadOnlyList<ResourceSummary> Resources);

// ── list_conflicts / reschedule_request ──────────────────────────────────────

/// <summary>One conflict, flattened out of <see cref="ConflictInfo"/>'s editor-oriented shape.</summary>
public sealed record ConflictSummary(
    string Kind,
    string Severity,
    string Message,
    Guid? ResourceId,
    Guid? PeerRequestId);

public sealed record RequestConflictSummary(Guid RequestId, IReadOnlyList<ConflictSummary> Conflicts);

public sealed record ConflictListResult(int Count, IReadOnlyList<RequestConflictSummary> Conflicts);

/// <summary>
/// The moved request paired with the conflicts it now has. The pairing is the point: a move that
/// succeeds can still be a move that overbooks, and reporting only the request would let an agent
/// read success as cleanliness.
/// </summary>
public sealed record RescheduleResult(
    RequestSummary Request,
    IReadOnlyList<ConflictSummary> Conflicts);

// ── assign_resource ──────────────────────────────────────────────────────────

public sealed record AssignmentSummary(
    Guid Id,
    Guid RequestId,
    Guid ResourceId,
    DateTime StartUtc,
    DateTime EndUtc);

/// <param name="Assigned">
/// False when a blocking conflict stopped the booking. That is a real answer rather than an error:
/// the agent is expected to read <paramref name="Conflict"/> and pick another resource or window.
/// </param>
/// <param name="Conflict">
/// Present on both outcomes. A soft conflict does not block a manual assignment, so a booking can
/// succeed and still carry one — hiding it would imply the booking was clean.
/// </param>
public sealed record AssignResourceResult(
    bool Assigned,
    AssignmentSummary? Assignment,
    ResourceConflictSummary? Conflict);

public sealed record ResourceConflictSummary(string Type, string Message);

// ── Projection helpers ───────────────────────────────────────────────────────

/// <summary>
/// Domain record → wire projection. Kept next to the records so the mapping is one hop from the
/// shape it produces, and so no tool hand-rolls a second version of it.
/// </summary>
public static class McpProjections
{
    public static RequestSummary ToSummary(this RequestInfo r) =>
        new(r.Id, r.Name, r.StartTs, r.EndTs, r.SiteId,
            IsScheduled: r.StartTs is not null && r.EndTs is not null);

    public static ResourceSummary ToSummary(this ResourceInfo r) =>
        new(r.Id, r.Name, r.ResourceTypeKey, r.IsActive, r.HomeSiteId);

    public static ConflictSummary ToSummary(this ConflictInfo c) =>
        new(c.Kind, c.Severity, c.Message, c.ResourceId, c.PeerRequestId);

    public static AssignmentSummary ToSummary(this ResourceAssignmentInfo a) =>
        new(a.Id, a.RequestId, a.ResourceId, a.StartUtc, a.EndUtc);

    public static ResourceConflictSummary ToSummary(this ResourceConflict c) =>
        new(c.Type.ToString(), c.Message);
}
