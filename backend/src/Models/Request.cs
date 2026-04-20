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

    // Space and item references
    public Guid? SpaceId { get; init; }
    public string? RequestItemId { get; init; }

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

    // Computed property: is this request scheduled (allocated to a space)?
    public bool IsScheduled => SpaceId.HasValue && StartTs.HasValue && EndTs.HasValue;
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

    public Guid? SpaceId { get; init; }
    public string? RequestItemId { get; init; }

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

    public Guid? SpaceId { get; init; }
    public string? RequestItemId { get; init; }

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
}

/// <summary>
/// Request to schedule or unschedule a request.
/// To schedule: provide spaceId, startTs, and endTs.
/// To unschedule: provide all fields as null.
/// </summary>
public record ScheduleRequestRequest
{
    public Guid? SpaceId { get; init; }
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
