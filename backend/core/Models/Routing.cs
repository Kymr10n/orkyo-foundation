namespace Api.Models;

/// <summary>
/// A part's sequence of operations, kept once and instantiated per work order. Each step
/// names an operation — a request template, which carries the target resource types and the
/// criterion requirements — and how long this part takes on it.
/// </summary>
public sealed record RoutingInfo
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required IReadOnlyList<RoutingStepInfo> Steps { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}

public sealed record RoutingStepInfo
{
    public required Guid Id { get; init; }
    /// <summary>1-based position in the routing; steps run in this order.</summary>
    public required int StepNo { get; init; }
    public required Guid OperationTemplateId { get; init; }
    /// <summary>The operation template's name, so a routing lists as text without a second read.</summary>
    public required string OperationName { get; init; }
    public required int SetupMinutes { get; init; }
    public required int RunMinutesPerUnit { get; init; }
    /// <summary>Minimum gap between this step's finish and the next step's start.</summary>
    public required int LagMinutesAfter { get; init; }
}

public sealed record RoutingStepRequest
{
    public required int StepNo { get; init; }
    public required Guid OperationTemplateId { get; init; }
    public int SetupMinutes { get; init; }
    public int RunMinutesPerUnit { get; init; }
    public int LagMinutesAfter { get; init; }
}

public sealed record CreateRoutingRequest
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required IReadOnlyList<RoutingStepRequest> Steps { get; init; }
}

public sealed record UpdateRoutingRequest
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    /// <summary>The whole step list; it replaces what was there.</summary>
    public required IReadOnlyList<RoutingStepRequest> Steps { get; init; }
}

/// <summary>
/// Makes a work order from a routing: a container request for the job with one leaf per step
/// and the finish-to-start chain between them, all in one transaction. Each leaf lasts
/// <c>setup + run × quantity</c> minutes and inherits the operation's target types and
/// requirements; the job window, when given, is copied onto every leaf.
/// </summary>
public sealed record InstantiateRoutingRequest
{
    public required string Name { get; init; }
    public Guid? SiteId { get; init; }
    public required int Quantity { get; init; }
    public DateTime? EarliestStartTs { get; init; }
    public DateTime? LatestEndTs { get; init; }
    /// <summary>An existing group to put the job under; null makes it a top-level request.</summary>
    public Guid? ParentRequestId { get; init; }
}

public sealed record InstantiateRoutingResponse(
    RequestInfo Parent,
    IReadOnlyList<Guid> ChildIds);
