namespace Api.Models;

/// <summary>
/// One parent's children and the dependencies among them — everything the planner draws, in a
/// single read. Assembled rather than stored: it is a projection over requests and edges that
/// exist independently of any planner.
/// </summary>
public record RequestPlan
{
    public required Guid ParentId { get; init; }
    public required string ParentName { get; init; }
    public required PlanningMode ParentPlanningMode { get; init; }

    /// <summary>The parent's direct children, in their sort order.</summary>
    public required IReadOnlyList<RequestPlanChild> Children { get; init; }

    /// <summary>
    /// Edges with BOTH ends among the children. Edges that leave the group are deliberately not
    /// listed — they have no second node to draw — and are reported per child as counts instead,
    /// so an editor can say a task waits on something outside without inventing a node for it.
    /// </summary>
    public required IReadOnlyList<RequestDependencyInfo> Edges { get; init; }
}

/// <summary>A child of the planned parent, with everything the planner shows on its node.</summary>
public record RequestPlanChild
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required PlanningMode PlanningMode { get; init; }

    /// <summary>Schedule-derived status, as everywhere else in the read model.</summary>
    public required RequestStatus Status { get; init; }
    public DateTime? StartTs { get; init; }
    public DateTime? EndTs { get; init; }
    public required int SortOrder { get; init; }
    public string? Icon { get; init; }

    public required PredecessorLogic PredecessorLogic { get; init; }
    public int? PredecessorLogicK { get; init; }

    /// <summary>
    /// Whether this request's join condition is satisfied right now — the same evaluation the
    /// execution gate performs. Sent so the planner can show a task as not-yet-startable instead
    /// of letting the user try and meet a 409.
    /// </summary>
    public required bool CanStart { get; init; }

    /// <summary>Edges to and from requests outside this group, which the planner cannot draw.</summary>
    public required int ExternalPredecessorCount { get; init; }
    public required int ExternalSuccessorCount { get; init; }
}
