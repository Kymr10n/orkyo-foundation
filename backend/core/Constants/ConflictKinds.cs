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

    /// <summary>
    /// The resource carries an absence over the booked window (maintenance, vacation, sickness),
    /// so it cannot do the work. An error, unlike <see cref="StartsInOffTime"/>: a site closure
    /// says the hours are unusual, an absence says the resource is gone.
    /// </summary>
    public const string ResourceUnavailable = "resource_unavailable";
    public const string SiteMismatch = "site_mismatch";
    public const string BelowMinDuration = "below_min_duration";
    public const string BeforeEarliestStart = "before_earliest_start";
    public const string AfterLatestEnd = "after_latest_end";

    /// <summary>
    /// The request is placed before the predecessor it waits for has finished, or its
    /// predecessor is not scheduled at all. Emitted on the successor, naming the predecessor
    /// as the peer.
    /// </summary>
    public const string DependencyViolation = "dependency_violation";
}
