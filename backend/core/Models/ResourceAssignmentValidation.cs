using System.Text.Json.Serialization;

namespace Api.Models;

/// <summary>
/// Severity level for validation results.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ValidationSeverity
{
    [JsonStringEnumMemberName("ok")]
    Ok,

    [JsonStringEnumMemberName("warning")]
    Warning,

    [JsonStringEnumMemberName("blocker")]
    Blocker
}

/// <summary>
/// Reason codes for assignment validation issues.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ValidationReasonCode
{
    [JsonStringEnumMemberName("resource.not-found")]
    ResourceNotFound,

    [JsonStringEnumMemberName("resource.inactive")]
    ResourceInactive,

    [JsonStringEnumMemberName("resource.type-mismatch")]
    ResourceTypeMismatch,

    [JsonStringEnumMemberName("capability.missing")]
    CapabilityMissing,

    [JsonStringEnumMemberName("offtime.overlap")]
    OffTimeOverlap,

    [JsonStringEnumMemberName("assignment.overbooked")]
    AssignmentOverbooked,

    [JsonStringEnumMemberName("nonworking.weekend")]
    NonWorkingWeekend,

    [JsonStringEnumMemberName("nonworking.holiday")]
    NonWorkingHoliday,

    [JsonStringEnumMemberName("allocation-mode.invalid")]
    InvalidAllocationMode,

    [JsonStringEnumMemberName("allocation-percent.invalid")]
    InvalidAllocationPercent
}

/// <summary>
/// A single validation issue (blocker or warning).
/// </summary>
public record ValidationIssue
{
    public required ValidationReasonCode Code { get; init; }
    public required string Message { get; init; }
    public Guid? ResourceId { get; init; }
    public Guid? ConflictingAssignmentId { get; init; }
    public Guid? ConflictingAvailabilityId { get; init; }
    public Guid? CriterionId { get; init; }
    public string? Details { get; init; }
}

/// <summary>
/// Request to validate an assignment before creating it.
///
/// <para><see cref="RequestId"/> is optional: when the caller is about to create
/// a new request and wants pre-save validation (Add Person dialog before Save),
/// it can be omitted. The validator then skips capability checks (because
/// requirements live on the request that doesn't exist yet) but still runs
/// off-time, weekend/holiday, and overbook checks against the resource.</para>
/// </summary>
public record ValidateResourceAssignmentRequest
{
    public Guid? RequestId { get; init; }
    public required Guid ResourceId { get; init; }
    public required DateTime StartUtc { get; init; }
    public required DateTime EndUtc { get; init; }
    public decimal? AllocationPercent { get; init; }
    public int? AllocationUnits { get; init; }
    public string? AllocationMode { get; init; }
}

/// <summary>
/// Result of validating an assignment.
/// </summary>
public record ValidationResult
{
    public required ValidationSeverity Severity { get; init; }
    public required List<ValidationIssue> Blockers { get; init; } = new();
    public required List<ValidationIssue> Warnings { get; init; } = new();
}
