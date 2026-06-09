namespace Api.Models;

public enum UtilizationGranularity
{
    Day,
    Week,
    Month,
}

public record UtilizationBucket
{
    public required DateTime Start { get; init; }
    public required DateTime End { get; init; }
    public required decimal AllocatedPercent { get; init; }
    public required decimal EffectiveAvailabilityPercent { get; init; }
    public required bool IsExclusiveOccupied { get; init; }
}

public record UtilizationResponse
{
    public required DateTime From { get; init; }
    public required DateTime To { get; init; }
    public required string Granularity { get; init; }
    public required List<UtilizationBucket> Buckets { get; init; }
}

/// <summary>
/// One resource's utilization buckets, used by the bulk by-resource endpoint so
/// the People grid can load every row in a single request instead of one per person.
/// </summary>
public record ResourceUtilizationResponse
{
    public required Guid ResourceId { get; init; }
    public required List<UtilizationBucket> Buckets { get; init; }
}
