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
    InvalidAllocationPercent,

    // Site/location mismatches (Home-Site / Current-Site model). When no execution site can be
    // resolved (site-neutral request, no space yet) there is simply no constraint, so no code.
    /// <summary>A space's site differs from the request's site scope (hard blocker).</summary>
    [JsonStringEnumMemberName("site.mismatch-space")]
    SiteMismatchSpace,

    /// <summary>A person/tool's current site differs from the execution site but cross-site is allowed (warning).</summary>
    [JsonStringEnumMemberName("site.mismatch-person")]
    SiteMismatchPerson,

    /// <summary>A person/tool is assigned outside its current site and cross-site is not allowed (hard blocker).</summary>
    [JsonStringEnumMemberName("site.cross-not-allowed")]
    SiteCrossNotAllowed
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

    /// <summary>
    /// Optional id of an existing assignment to exclude from overbook checks.
    /// Set this when re-validating an already-committed assignment (e.g. the
    /// conflicts view) so the assignment does not overlap with itself. Null on
    /// the creation path, where the assignment does not yet exist.
    /// </summary>
    public Guid? ExcludeAssignmentId { get; init; }
}

/// <summary>
/// Batch validation request: validate many assignment pairings in one round-trip.
/// Used by the conflicts view, which evaluates every scheduled request at once.
/// </summary>
public record ValidateResourceAssignmentBatchRequest
{
    public required List<ValidateResourceAssignmentRequest> Items { get; init; } = new();
}

/// <summary>
/// One entry of a batch validation response. Echoes <see cref="RequestId"/> and
/// <see cref="ResourceId"/> so the caller can correlate results back to inputs
/// without relying on positional ordering.
/// </summary>
public record AssignmentValidationBatchItem
{
    public required Guid? RequestId { get; init; }
    public required Guid ResourceId { get; init; }
    public required ValidationResult Result { get; init; }
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
