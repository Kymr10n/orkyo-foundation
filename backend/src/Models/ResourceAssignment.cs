namespace Api.Models;

public record ResourceAssignmentInfo
{
    public required Guid Id { get; init; }
    public required Guid RequestId { get; init; }
    public required Guid ResourceId { get; init; }
    public required DateTime StartUtc { get; init; }
    public required DateTime EndUtc { get; init; }
    public decimal? AllocationPercent { get; init; }
    public int? AllocationUnits { get; init; }
    public required string AssignmentStatus { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}

public record CreateResourceAssignmentRequest
{
    public required Guid RequestId { get; init; }
    public required Guid ResourceId { get; init; }
    public required DateTime StartUtc { get; init; }
    public required DateTime EndUtc { get; init; }
    public decimal? AllocationPercent { get; init; }
    public int? AllocationUnits { get; init; }
}
