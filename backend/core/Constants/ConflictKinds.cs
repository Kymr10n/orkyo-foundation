namespace Api.Constants;

/// <summary>
/// Conflict kind discriminators for <c>ConflictInfo.Kind</c>. These are the FE string
/// union values consumed by the frontend conflicts registry, produced by
/// <c>ConflictService</c> and aggregated by <c>InsightsService</c> — keep all three
/// in sync through these constants.
/// </summary>
public static class ConflictKinds
{
    public const string ConnectorMismatch = "connector_mismatch";
    public const string Overlap = "overlap";
    public const string CapacityExceeded = "capacity_exceeded";
    public const string StartsInOffTime = "starts_in_off_time";
    public const string SiteMismatch = "site_mismatch";
    public const string BelowMinDuration = "below_min_duration";
    public const string BeforeEarliestStart = "before_earliest_start";
    public const string AfterLatestEnd = "after_latest_end";
}
