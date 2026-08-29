using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace Api.Models;

/// <summary>
/// Which solver engine was used.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SolverKind
{
    Greedy = 0,
    OrToolsCpSat = 1
}

/// <summary>
/// Status returned by the solver.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SolverStatus
{
    Optimal = 0,
    Feasible = 1,
    Infeasible = 2,
    Unknown = 3
}

/// <summary>
/// Reason code explaining why a request could not be scheduled.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SchedulingReasonCode
{
    NoCompatibleResource = 1,
    InsufficientCapacity = 3,
    BlockedByFixedAssignments = 4,
    InvalidDuration = 5,
    InternalSolverLimit = 7,

    /// <summary>
    /// The request waits for a predecessor that this run cannot place: it is unscheduled and
    /// outside the solve set, or its finish leaves the successor no feasible day. Scheduling
    /// it anyway would knowingly produce a dependency violation.
    /// </summary>
    PredecessorUnscheduled = 8
}

// ── Request / Response DTOs ────────────────────────────────────────

/// <param name="ResourceTypeKey">
/// Which resource type to schedule. One run fills one type's slot, because the solver's model —
/// no overlap per node, at most one node per request — has nothing to say about matching a room
/// to a van. NULL means spaces, which is what every run meant before types were selectable.
/// </param>
public sealed record AutoSchedulePreviewRequest(
    Guid SiteId,
    DateOnly HorizonStart,
    DateOnly HorizonEnd,
    IReadOnlyCollection<Guid>? RequestIds = null,
    bool RespectSchedulingSettings = true,
    string? ResourceTypeKey = null);

public sealed record AutoScheduleApplyRequest(
    Guid SiteId,
    DateOnly HorizonStart,
    DateOnly HorizonEnd,
    IReadOnlyCollection<Guid>? RequestIds = null,
    bool RespectSchedulingSettings = true,
    string? PreviewFingerprint = null,
    string? ResourceTypeKey = null);

public sealed record AutoSchedulePreviewResponse(
    SolverKind SolverUsed,
    SolverStatus Status,
    AutoScheduleScore Score,
    IReadOnlyList<ProposedAssignmentDto> Assignments,
    IReadOnlyList<UnscheduledRequestDto> Unscheduled,
    IReadOnlyList<string> Diagnostics,
    string Fingerprint);

public sealed record AutoScheduleApplyResponse(
    int CreatedAssignments,
    int UnscheduledCount);

public sealed record AutoScheduleScore(
    int ScheduledCount,
    int UnscheduledCount,
    int PriorityScore);

public sealed record ProposedAssignmentDto(
    Guid RequestId,
    string RequestName,
    Guid ResourceId,
    string ResourceName,
    DateOnly Start,
    DateOnly End,
    int DurationDays);

public sealed record UnscheduledRequestDto(
    Guid RequestId,
    string RequestName,
    IReadOnlyList<SchedulingReasonCode> ReasonCodes);

// ── Internal domain types (solver input/output) ────────────────────

/// <summary>
/// Canonical scheduling problem — solver-agnostic input.
/// </summary>
public sealed record SchedulingProblem(
    Guid SiteId,
    DateOnly HorizonStart,
    DateOnly HorizonEnd,
    IReadOnlyList<RequestNode> Requests,
    IReadOnlyList<ResourceNode> Resources,
    IReadOnlyList<FixedOccupancy> FixedAssignments,
    SchedulingSettingsInfo? Settings,
    Dictionary<Guid, List<BlockedPeriod>>? BlockedPeriodsByResource,
    IReadOnlyList<DependencyEdge>? Dependencies = null,
    IReadOnlyList<WithheldRequestNode>? Withheld = null);

/// <summary>
/// A request kept out of the solve set because a dependency makes it unplaceable in this run:
/// its predecessor is neither scheduled nor part of the run, or that predecessor's finish leaves
/// no room inside the request's own window.
///
/// Carried separately because it never reaches a solver — the name travels with it so the caller
/// can say which request and why, rather than silently returning fewer than it was asked for.
/// </summary>
public sealed record WithheldRequestNode(Guid RequestId, string DisplayName);

/// <summary>
/// A precedence edge the solver must honour: the successor may not start until the
/// predecessor has finished, plus the lag. Both endpoints are in this run's solve set —
/// an edge whose predecessor is already placed is folded into the successor's feasible
/// days instead, and one whose predecessor is absent rejects the successor outright.
/// Lag is in whole days here, ceilinged from minutes exactly as durations are.
/// </summary>
public sealed record DependencyEdge(
    Guid PredecessorRequestId,
    Guid SuccessorRequestId,
    int LagDays);

public sealed record RequestNode(
    Guid RequestId,
    string DisplayName,
    DateOnly? EarliestStart,
    DateOnly? LatestEnd,
    int DurationDays,
    int Priority,
    bool RespectSchedulingSettings,
    IReadOnlySet<Guid> RequiredCriterionIds);

public sealed record ResourceNode(
    Guid ResourceId,
    string DisplayName,
    IReadOnlySet<Guid> CriterionIds);

public sealed record FixedOccupancy(
    Guid RequestId,
    Guid ResourceId,
    DateOnly Start,
    DateOnly End);

/// <summary>
/// A feasible request→resource candidate with enumerated start days.
/// </summary>
public sealed record SchedulingCandidate(
    Guid RequestId,
    Guid ResourceId,
    DateOnly EarliestStart,
    DateOnly LatestEnd,
    int DurationDays,
    int Priority,
    IReadOnlyList<DateOnly> FeasibleStartDays);

public sealed record CandidateRejection(
    Guid RequestId,
    Guid? ResourceId,
    SchedulingReasonCode ReasonCode,
    string? Message = null);

/// <summary>
/// Result of feasibility analysis — candidates that survive preprocessing.
/// </summary>
public sealed record AnalyzedSchedulingProblem(
    SchedulingProblem Problem,
    IReadOnlyList<SchedulingCandidate> Candidates,
    IReadOnlyList<CandidateRejection> Rejections,
    IReadOnlyList<string> Diagnostics);

/// <summary>
/// Solver output.
/// </summary>
public sealed record SchedulingSolution(
    SolverKind SolverUsed,
    SolverStatus Status,
    IReadOnlyList<ScheduledPlacement> Assignments,
    IReadOnlyList<UnscheduledPlacement> Unscheduled,
    IReadOnlyList<string> Diagnostics)
{
    public AutoScheduleScore ToScore()
        => new(
            ScheduledCount: Assignments.Count,
            UnscheduledCount: Unscheduled.Count,
            PriorityScore: Assignments.Sum(x => x.Priority));

    /// <summary>
    /// Computes a SHA-256 fingerprint over sorted assignments so that two identical
    /// solutions produce the same fingerprint regardless of solver non-determinism in ordering.
    /// Used for stale-preview detection on apply.
    /// </summary>
    /// <param name="edges">
    /// The precedence edges the preview was computed under. They are part of the identity
    /// because they change what a valid plan is: without them, adding a dependency between
    /// preview and apply would leave the fingerprint matching, and the apply would commit a
    /// plan that violates the edge the user just drew.
    /// </param>
    public string ComputeFingerprint(string resourceTypeKey, IEnumerable<DependencyEdge> edges)
    {
        // The type is part of the identity, not just the assignments: an empty solution hashes
        // the same for every type, so without it a preview that proposed nothing would match
        // an apply for any type.
        var sb = new StringBuilder(resourceTypeKey).Append('#');
        foreach (var e in edges.OrderBy(e => e.PredecessorRequestId).ThenBy(e => e.SuccessorRequestId))
        {
            sb.Append(e.PredecessorRequestId).Append('>')
              .Append(e.SuccessorRequestId).Append('+')
              .Append(e.LagDays).Append(';');
        }
        sb.Append('#');
        foreach (var a in Assignments.OrderBy(a => a.RequestId).ThenBy(a => a.ResourceId))
        {
            sb.Append(a.RequestId).Append('|')
              .Append(a.ResourceId).Append('|')
              .Append(a.Start).Append('|')
              .Append(a.End).Append(';');
        }
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexStringLower(hash);
    }
}

public sealed record ScheduledPlacement(
    Guid RequestId,
    Guid ResourceId,
    DateOnly Start,
    DateOnly End,
    int DurationDays,
    int Priority);

public sealed record UnscheduledPlacement(
    Guid RequestId,
    IReadOnlyList<SchedulingReasonCode> ReasonCodes);
