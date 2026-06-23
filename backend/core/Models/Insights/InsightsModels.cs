namespace Api.Models.Insights;

/// <summary>
/// Server-built filter for the Insights dashboard. Tenant is implicit (per-database isolation);
/// only the site is an explicit, tenant-validated dimension. <see cref="Bucket"/> is required by the
/// trend endpoints and <see cref="ResourceType"/> by the utilization endpoint — the endpoint layer
/// validates and fails fast before constructing this.
/// </summary>
public record InsightsFilter
{
    public Guid? SiteId { get; init; }
    public required DateTime From { get; init; }
    public required DateTime To { get; init; }
    public string? Bucket { get; init; }
    public string? ResourceType { get; init; }
}

/// <summary>Where the numbers came from — surfaced subtly in the UI and stable across the live→snapshot swap.</summary>
public record InsightsMetadata
{
    public required DateTime CalculatedAt { get; init; }
    public required string SourceMode { get; init; }
}

public record InsightsPeriod
{
    public required DateTime From { get; init; }
    public required DateTime To { get; init; }
}

/// <summary>
/// Request counts. <c>Scheduled</c>/<c>Unscheduled</c> partition the non-cancelled requests
/// (scheduled = has a time window + space assignment). <c>Completed</c> (status=done) and
/// <c>Cancelled</c> are status views that may overlap <c>Scheduled</c>.
/// </summary>
public record RequestCounts
{
    public required int Total { get; init; }
    public required int Scheduled { get; init; }
    public required int Unscheduled { get; init; }
    public required int Completed { get; init; }
    public required int Cancelled { get; init; }
}

/// <summary>
/// Conflict counts grouped into stable analytics categories, mapped from the live
/// <c>ConflictInfo.Kind</c> values. <c>MissingResource</c> has no current source kind and is always 0
/// (honest placeholder, not faked).
/// </summary>
public record ConflictCounts
{
    public required int Total { get; init; }
    public required int Overbooking { get; init; }
    public required int CriteriaMismatch { get; init; }
    public required int ResourceUnavailable { get; init; }
    public required int ScheduleOutsideAvailability { get; init; }
    public required int MissingResource { get; init; }
}

/// <summary>Aggregate utilization over the period, per resource type. Null = no capacity configured (not 0%).</summary>
public record UtilizationSummary
{
    public required decimal? SpacesPercent { get; init; }
    public required decimal? PeoplePercent { get; init; }
    public required decimal? ToolsPercent { get; init; }
}

public record InsightsOverview
{
    public required InsightsPeriod Period { get; init; }
    public Guid? SiteId { get; init; }
    public required RequestCounts Requests { get; init; }
    public required ConflictCounts Conflicts { get; init; }
    public required UtilizationSummary Utilization { get; init; }
    public required InsightsMetadata Metadata { get; init; }
}

public record UtilizationSeriesPoint
{
    public required DateTime BucketStart { get; init; }
    public required DateTime BucketEnd { get; init; }
    public required long TotalCapacityMinutes { get; init; }
    public required long UsedCapacityMinutes { get; init; }
    public required long AvailableCapacityMinutes { get; init; }
    public required decimal? UtilizationPercent { get; init; }
    public required int ConflictCount { get; init; }
}

public record InsightsUtilization
{
    public required string ResourceType { get; init; }
    public required string Bucket { get; init; }
    public required IReadOnlyList<UtilizationSeriesPoint> Series { get; init; }
    public required InsightsMetadata Metadata { get; init; }
}

public record ConflictSeriesPoint
{
    public required DateTime BucketStart { get; init; }
    public required DateTime BucketEnd { get; init; }
    public required int Total { get; init; }
    public required int Overbooking { get; init; }
    public required int CriteriaMismatch { get; init; }
    public required int ResourceUnavailable { get; init; }
    public required int ScheduleOutsideAvailability { get; init; }
    public required int MissingResource { get; init; }
}

public record InsightsConflicts
{
    public required string Bucket { get; init; }
    public required IReadOnlyList<ConflictSeriesPoint> Series { get; init; }
    public required InsightsMetadata Metadata { get; init; }
}

public record RequestSeriesPoint
{
    public required DateTime BucketStart { get; init; }
    public required DateTime BucketEnd { get; init; }
    public required int Total { get; init; }
    public required int Scheduled { get; init; }
    public required int Unscheduled { get; init; }
    public required int Completed { get; init; }
    public required int Cancelled { get; init; }
}

public record InsightsRequests
{
    public required string Bucket { get; init; }
    public required IReadOnlyList<RequestSeriesPoint> Series { get; init; }
    public required InsightsMetadata Metadata { get; init; }
}
