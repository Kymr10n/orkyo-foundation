namespace Api.Models;

/// <summary>
/// One request's position in the dependency network.
///
/// All four instants are UTC timestamps at minute precision — the resolution the scheduler
/// plans in. Calendar time, not one site's working hours: the network can span sites that
/// keep different hours, and a lag is elapsed time (paint cures over the weekend too).
/// </summary>
public record CriticalPathNode
{
    public required Guid RequestId { get; init; }
    public required string Name { get; init; }

    /// <summary>The soonest this can start once its predecessors have finished.</summary>
    public required DateTime EarliestStart { get; init; }
    public required DateTime EarliestFinish { get; init; }

    /// <summary>The latest it can start without pushing the whole network out.</summary>
    public required DateTime LatestStart { get; init; }
    public required DateTime LatestFinish { get; init; }

    /// <summary>
    /// Minutes of slack. Zero means any delay here delays everything downstream — that is what
    /// puts a request on the critical path.
    /// </summary>
    public required int TotalFloatMinutes { get; init; }

    public required bool IsCritical { get; init; }

    /// <summary>
    /// True when the request already has a placement. Its dates here can still sit later than
    /// that placement: a predecessor finishing after it pushes the earliest dates out, and the
    /// pass reports where the work can actually happen rather than where it is currently drawn.
    /// </summary>
    public required bool IsScheduled { get; init; }
}

/// <summary>
/// The dependency network with its critical path marked. Every instant is a UTC timestamp and
/// every duration is in minutes — the resolution the scheduler itself plans in.
/// </summary>
public record CriticalPathResult
{
    public required IReadOnlyList<CriticalPathNode> Nodes { get; init; }
    public required IReadOnlyList<RequestDependencyInfo> Edges { get; init; }

    /// <summary>Minutes from the network's earliest start to its latest finish.</summary>
    public required int DurationMinutes { get; init; }

    /// <summary>
    /// Anything the caller has to know to read the result honestly — requests skipped for want
    /// of a duration, or a cycle that made ordering impossible.
    /// </summary>
    public required IReadOnlyList<string> Diagnostics { get; init; }
}
