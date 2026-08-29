namespace Api.Models;

/// <summary>
/// One request's position in the dependency network.
///
/// All four dates are whole days. The scheduler plans in day buckets, durations are ceilinged
/// to days, and lag is converted the same way — reporting hours here would be false precision
/// dressed up as accuracy.
/// </summary>
public record CriticalPathNode
{
    public required Guid RequestId { get; init; }
    public required string Name { get; init; }

    /// <summary>The soonest this can start once every predecessor has finished.</summary>
    public required DateOnly EarliestStart { get; init; }
    public required DateOnly EarliestFinish { get; init; }

    /// <summary>The latest it can start without pushing the whole network out.</summary>
    public required DateOnly LatestStart { get; init; }
    public required DateOnly LatestFinish { get; init; }

    /// <summary>
    /// Days of slack. Zero means any delay here delays everything downstream — that is what
    /// puts a request on the critical path.
    /// </summary>
    public required int TotalFloatDays { get; init; }

    public required bool IsCritical { get; init; }

    /// <summary>
    /// True when the request already has a placement. Its dates here can still sit later than
    /// that placement: a predecessor finishing after it pushes the earliest dates out, and the
    /// pass reports where the work can actually happen rather than where it is currently drawn.
    /// </summary>
    public required bool IsScheduled { get; init; }
}

/// <summary>
/// The dependency network with its critical path marked. Every date and duration is in whole
/// days — the granularity the scheduler itself plans in.
/// </summary>
public record CriticalPathResult
{
    public required IReadOnlyList<CriticalPathNode> Nodes { get; init; }
    public required IReadOnlyList<RequestDependencyInfo> Edges { get; init; }

    /// <summary>Whole days from the network's earliest start to its latest finish.</summary>
    public required int DurationDays { get; init; }

    /// <summary>
    /// Anything the caller has to know to read the result honestly — requests skipped for want
    /// of a duration, or a cycle that made ordering impossible.
    /// </summary>
    public required IReadOnlyList<string> Diagnostics { get; init; }
}
