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

    /// <summary>The latest it can start without pushing its chain's finish or a deadline out.</summary>
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

    /// <summary>The chain this request belongs to — see <see cref="CriticalPathChain.ChainId"/>.</summary>
    public required Guid ChainId { get; init; }
}

/// <summary>
/// One connected group of dependent requests. Float inside a chain is measured against the
/// chain's own finish, so separate chains never lend each other slack.
/// </summary>
public record CriticalPathChain
{
    /// <summary>The smallest request id in the chain — stable between calls.</summary>
    public required Guid ChainId { get; init; }

    /// <summary>The chain's requests in dependency order.</summary>
    public required IReadOnlyList<Guid> RequestIds { get; init; }

    public required string FirstName { get; init; }
    public required string LastName { get; init; }

    /// <summary>The earliest start of any request in the chain.</summary>
    public required DateTime Start { get; init; }

    /// <summary>The earliest finish of the chain's last request.</summary>
    public required DateTime Finish { get; init; }

    /// <summary>The earliest deadline in the chain, or null when no request carries one.</summary>
    public DateTime? Deadline { get; init; }

    /// <summary>
    /// Minutes between a deadline and the earliest finish of the request that carries it, the
    /// smallest over the chain. Negative means the chain misses a deadline. Null without one.
    /// </summary>
    public int? SlackMinutes { get; init; }
}

/// <summary>
/// The dependency network with its critical path marked. Every instant is a UTC timestamp and
/// every duration is in minutes — the resolution the scheduler itself plans in.
/// </summary>
public record CriticalPathResult
{
    public required IReadOnlyList<CriticalPathNode> Nodes { get; init; }
    public required IReadOnlyList<RequestDependencyInfo> Edges { get; init; }

    /// <summary>The chains, most at risk first: least slack to a deadline, then earliest finish.</summary>
    public required IReadOnlyList<CriticalPathChain> Chains { get; init; }

    /// <summary>
    /// Anything the caller has to know to read the result honestly — requests skipped for want
    /// of a duration, or a cycle that made ordering impossible.
    /// </summary>
    public required IReadOnlyList<string> Diagnostics { get; init; }
}
