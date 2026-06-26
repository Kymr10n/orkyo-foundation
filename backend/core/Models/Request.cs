using System.Text.Json;
using System.Text.Json.Serialization;
using Api.Constants;

namespace Api.Models;

/// <summary>
/// Duration unit for request scheduling.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DurationUnit
{
    [JsonStringEnumMemberName("minutes")]
    Minutes,

    [JsonStringEnumMemberName("hours")]
    Hours,

    [JsonStringEnumMemberName("days")]
    Days,

    [JsonStringEnumMemberName("weeks")]
    Weeks,

    [JsonStringEnumMemberName("months")]
    Months,

    [JsonStringEnumMemberName("years")]
    Years
}

/// <summary>
/// Planning mode for request tree hierarchy.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PlanningMode
{
    [JsonStringEnumMemberName("leaf")]
    Leaf,

    [JsonStringEnumMemberName("summary")]
    Summary,

    [JsonStringEnumMemberName("container")]
    Container
}

/// <summary>
/// Request status.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RequestStatus
{
    [JsonStringEnumMemberName("planned")]
    Planned,

    [JsonStringEnumMemberName("in_progress")]
    InProgress,

    [JsonStringEnumMemberName("done")]
    Done,

    [JsonStringEnumMemberName("cancelled")]
    Cancelled
}

/// <summary>
/// Complete request information.
/// </summary>
public record RequestInfo
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }

    // Tree hierarchy
    public Guid? ParentRequestId { get; init; }
    public required PlanningMode PlanningMode { get; init; }
    public int SortOrder { get; init; }

    /// <summary>Site this request is scoped to. NULL = site-neutral (schedulable at any site).</summary>
    public Guid? SiteId { get; init; }

    public string? RequestItemId { get; init; }

    /// <summary>
    /// All non-cancelled resource assignments for this request.
    /// Ordered by (ResourceTypeKey, StartUtc) — stable for snapshot tests.
    /// </summary>
    public required IReadOnlyList<ResourceAssignmentInfo> Assignments { get; init; }

    // Display icon (short string ID resolved to a lucide-react icon on the frontend).
    public string? Icon { get; init; }

    // Time constraints (nullable for unscheduled requests)
    public DateTime? StartTs { get; init; }
    public DateTime? EndTs { get; init; }

    // Scheduling constraints
    public DateTime? EarliestStartTs { get; init; }
    public DateTime? LatestEndTs { get; init; }

    // Minimal Duration (minimum time needed)
    public required int MinimalDurationValue { get; init; }
    public required DurationUnit MinimalDurationUnit { get; init; }

    // Actual Duration (actual scheduled duration when allocated)
    public int? ActualDurationValue { get; init; }
    public DurationUnit? ActualDurationUnit { get; init; }

    // Status
    public required RequestStatus Status { get; init; }

    // Metadata
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }

    // Requirements (optional, populated when requested)
    public List<RequestRequirementInfo>? Requirements { get; init; }

    // Scheduling
    public required bool SchedulingSettingsApply { get; init; }

    // Computed: scheduled when a Space resource is assigned and time window is set.
    public bool IsScheduled => StartTs.HasValue && EndTs.HasValue
        && Assignments.Any(a => a.ResourceTypeKey == ResourceTypeKeys.Space);
}

/// <summary>
/// Request requirement (criterion value).
/// </summary>
public record RequestRequirementInfo
{
    public required Guid Id { get; init; }
    public required Guid RequestId { get; init; }
    public required Guid CriterionId { get; init; }
    public required JsonElement Value { get; init; } // JSONB value
    public DateTime CreatedAt { get; init; }

    // Typed operator support (Phase 3)
    public string? Operator { get; init; } // e.g. ">=", "<=", "=" for Number criteria
    public JsonElement? AllowedValues { get; init; } // Set of allowed values for Enum criteria

    // Populated from join
    public CriterionBasicInfo? Criterion { get; init; }
}

/// <summary>
/// Basic criterion info for requirements.
/// </summary>
public record CriterionBasicInfo
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required CriterionDataType DataType { get; init; }
    public string? Unit { get; init; }
    public List<string>? EnumValues { get; init; }
}

/// <summary>
/// Request to create a new request.
/// </summary>
public record CreateRequestRequest
{
    public required string Name { get; init; }
    public string? Description { get; init; }

    // Tree hierarchy
    public Guid? ParentRequestId { get; init; }
    public PlanningMode PlanningMode { get; init; } = PlanningMode.Leaf;
    public int SortOrder { get; init; }

    /// <summary>Site scope. NULL = site-neutral (schedulable at any site).</summary>
    public Guid? SiteId { get; init; }

    public Guid? ResourceId { get; init; }
    public string? RequestItemId { get; init; }

    public string? Icon { get; init; }

    public DateTime? StartTs { get; init; }
    public DateTime? EndTs { get; init; }

    public DateTime? EarliestStartTs { get; init; }
    public DateTime? LatestEndTs { get; init; }

    public required int MinimalDurationValue { get; init; }
    public required DurationUnit MinimalDurationUnit { get; init; }

    public int? ActualDurationValue { get; init; }
    public DurationUnit? ActualDurationUnit { get; init; }

    public RequestStatus Status { get; init; } = RequestStatus.Planned;

    public bool SchedulingSettingsApply { get; init; } = true;

    // Requirements to create
    public List<CreateRequestRequirementRequest>? Requirements { get; init; }
}

/// <summary>
/// Request to update an existing request.
/// </summary>
public record UpdateRequestRequest
{
    public string? Name { get; init; }
    public string? Description { get; init; }

    // Tree hierarchy
    public Guid? ParentRequestId { get; init; }
    public PlanningMode? PlanningMode { get; init; }
    public int? SortOrder { get; init; }

    /// <summary>Site scope. A non-null value (re)scopes the request. To clear it back to "any site"
    /// (NULL), send null together with <see cref="ChangeSiteId"/> = true.</summary>
    public Guid? SiteId { get; init; }

    /// <summary>
    /// Distinguishes "do not change site" from "set site to NULL (any site)".
    /// When true, a null SiteId is honored (cleared); when false, a null SiteId is preserved.
    /// A non-null SiteId always applies regardless of this flag.
    /// </summary>
    public bool ChangeSiteId { get; init; }

    public Guid? ResourceId { get; init; }
    public string? RequestItemId { get; init; }

    public string? Icon { get; init; }

    public DateTime? StartTs { get; init; }
    public DateTime? EndTs { get; init; }

    public DateTime? EarliestStartTs { get; init; }
    public DateTime? LatestEndTs { get; init; }

    public int? MinimalDurationValue { get; init; }
    public DurationUnit? MinimalDurationUnit { get; init; }

    public int? ActualDurationValue { get; init; }
    public DurationUnit? ActualDurationUnit { get; init; }

    public RequestStatus? Status { get; init; }

    public bool? SchedulingSettingsApply { get; init; }

    // Requirements to update (replaces all existing requirements)
    public List<CreateRequestRequirementRequest>? Requirements { get; init; }
}

/// <summary>
/// Request to create a request requirement.
/// </summary>
public record CreateRequestRequirementRequest
{
    public required Guid CriterionId { get; init; }
    public required JsonElement Value { get; init; }
    public string? Operator { get; init; }
    public JsonElement? AllowedValues { get; init; }
}

/// <summary>
/// Request to update a request requirement.
/// </summary>
public record UpdateRequestRequirementRequest
{
    public required JsonElement Value { get; init; }
}

/// <summary>
/// Request to add a requirement to an existing request.
/// </summary>
public record AddRequirementRequest
{
    public required Guid CriterionId { get; init; }
    public required JsonElement Value { get; init; }
    // Phase 3: Typed operator support
    public string? Operator { get; init; }
    public JsonElement? AllowedValues { get; init; }
}

/// <summary>
/// Request to schedule or unschedule a request.
/// To schedule: provide resourceId, startTs, and endTs.
/// To unschedule: provide all fields as null.
/// </summary>
public record ScheduleRequestRequest
{
    public Guid? ResourceId { get; init; }
    public DateTime? StartTs { get; init; }
    public DateTime? EndTs { get; init; }
    public int? ActualDurationValue { get; init; }
    public DurationUnit? ActualDurationUnit { get; init; }
}

/// <summary>
/// Request to move/reparent a request in the tree.
/// </summary>
public record MoveRequestRequest
{
    public Guid? NewParentRequestId { get; init; }
    public int SortOrder { get; init; }
}

/// <summary>
/// Response for subtree deletion with descendant count.
/// </summary>
public record DeleteSubtreeResponse
{
    public required int DeletedCount { get; init; }
}

/// <summary>
/// One requirement on a candidate request annotated with whether this resource satisfies it.
/// </summary>
public record CandidateRequirementInfo(string Label, bool Satisfied);

/// <summary>
/// A request that overlaps a given period and is not yet assigned to the queried resource.
/// Returned by <c>GET /api/resources/{id}/candidate-requests</c>.
/// </summary>
public record CandidateRequestInfo(
    Guid RequestId,
    string Name,
    DateTime? StartTs,
    DateTime? EndTs,
    List<CandidateRequirementInfo> Requirements,
    /// <summary>Non-null when this person already has a non-cancelled assignment on this request.</summary>
    Guid? AssignmentId
);

/// <summary>
/// Extension methods for RequestInfo.
/// </summary>
public static class RequestInfoExtensions
{
    /// <summary>
    /// Gets the space resource ID from request assignments, if any.
    /// </summary>
    public static Guid? GetSpaceResourceId(this RequestInfo r) =>
        r.Assignments.FirstOrDefault(a => a.ResourceTypeKey == ResourceTypeKeys.Space)?.ResourceId;
}
