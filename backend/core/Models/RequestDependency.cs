namespace Api.Models;

/// <summary>
/// The dependency kinds the edge table accepts. Only finish-to-start exists today; the
/// column and this class are the seam through which start-to-start and the rest arrive
/// without a migration on the data.
/// </summary>
public static class DependencyTypes
{
    /// <summary>The successor cannot start until the predecessor has finished (plus any lag).</summary>
    public const string FinishToStart = "finish_to_start";
}

/// <summary>
/// A precedence edge: <see cref="SuccessorRequestId"/> cannot start until
/// <see cref="PredecessorRequestId"/> has finished, plus <see cref="LagMinutes"/>.
///
/// Independent of the request tree. Containment (parent/child) says what a request is part
/// of; this says what has to happen first, and the two routinely disagree — a leaf in one
/// group commonly blocks a leaf in another.
/// </summary>
public record RequestDependencyInfo
{
    public required Guid Id { get; init; }
    public required Guid PredecessorRequestId { get; init; }
    public required Guid SuccessorRequestId { get; init; }

    /// <summary>Display name of the predecessor, so a list of edges needs no second read.</summary>
    public required string PredecessorName { get; init; }
    public required string SuccessorName { get; init; }

    /// <summary>One of <see cref="DependencyTypes"/>.</summary>
    public required string DependencyType { get; init; }

    /// <summary>
    /// Minimum gap after the predecessor finishes, in minutes — the same unit the scheduling
    /// arithmetic uses everywhere else. Both readers ceiling it to whole days, so a lag can only
    /// ever push a successor later, never earlier.
    ///
    /// The two readers round differently on purpose. The scheduler converts against the working
    /// day, so with 8-hour hours a 720-minute lag is two days; the critical path converts against
    /// the calendar, so the same lag is one. A network spanning sites cannot borrow one site's
    /// working hours, and the scheduler cannot ignore them.
    /// </summary>
    public required int LagMinutes { get; init; }

    public DateTime CreatedAt { get; init; }
}

/// <summary>Payload for adding a predecessor to a request.</summary>
public record CreateDependencyRequest
{
    public required Guid PredecessorRequestId { get; init; }
    public int LagMinutes { get; init; }
}

/// <summary>
/// The edges touching one request, split by direction so the UI does not have to.
/// </summary>
public record RequestDependencies
{
    public required IReadOnlyList<RequestDependencyInfo> Predecessors { get; init; }
    public required IReadOnlyList<RequestDependencyInfo> Successors { get; init; }
}
