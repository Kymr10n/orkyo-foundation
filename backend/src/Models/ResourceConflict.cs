namespace Api.Models;

public record ResourceConflict
{
    public required Guid ResourceId { get; init; }
    public required ResourceConflictType Type { get; init; }
    public required string Message { get; init; }
    public Guid? ConflictingAssignmentId { get; init; }
    public Guid? ConflictingAvailabilityId { get; init; }
}

public enum ResourceConflictType
{
    ResourceNotFound,
    ResourceInactive,
    CapabilityMissing,
    CapabilityNotApplicable,
    ResourceAbsent,
    ExclusiveOverlap,
    FractionalCapacityExceeded,
    InvalidAllocationMode,
    InvalidAllocationPercent,
    OffTimeOverlap
}
