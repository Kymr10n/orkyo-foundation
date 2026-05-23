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
