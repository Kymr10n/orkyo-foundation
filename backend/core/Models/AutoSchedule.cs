using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Api.Helpers;

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
    /// outside the solve set, or its finish leaves the successor no room in its window. Scheduling
    /// it anyway would knowingly produce a dependency violation.
    /// </summary>
    PredecessorUnscheduled = 8
}

// ── Request / Response DTOs ────────────────────────────────────────

/// <param name="ResourceTypeKeys">
/// Which resource types the run fills. A request is placed with one resource of every type it
/// targets in this set, all at the same time. NULL means every active type a request can
/// target — people are attached on the request's own tab, many per request, and are never a
/// slot the solver fills.
/// </param>
public sealed record AutoSchedulePreviewRequest(
    Guid SiteId,
    DateOnly HorizonStart,
    DateOnly HorizonEnd,
    IReadOnlyCollection<Guid>? RequestIds = null,
    bool RespectSchedulingSettings = true,
    IReadOnlyCollection<string>? ResourceTypeKeys = null);

public sealed record AutoScheduleApplyRequest(
    Guid SiteId,
    DateOnly HorizonStart,
    DateOnly HorizonEnd,
    IReadOnlyCollection<Guid>? RequestIds = null,
    bool RespectSchedulingSettings = true,
    string? PreviewFingerprint = null,
    IReadOnlyCollection<string>? ResourceTypeKeys = null);

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

/// <summary>
/// One proposed placement: one resource per type the request needed, all occupied together.
/// <see cref="Start"/> and <see cref="End"/> are the half-open UTC window the apply would
/// write; <see cref="DurationMinutes"/> is the working time inside it.
/// </summary>
public sealed record ProposedAssignmentDto(
    Guid RequestId,
    string RequestName,
    IReadOnlyList<ProposedResourceDto> Resources,
    DateTime Start,
    DateTime End,
    int DurationMinutes);

public sealed record ProposedResourceDto(
    string TypeKey,
    Guid ResourceId,
    string ResourceName);

public sealed record UnscheduledRequestDto(
    Guid RequestId,
    string RequestName,
    IReadOnlyList<SchedulingReasonCode> ReasonCodes);

// ── Internal domain types (solver input/output) ────────────────────

/// <summary>
/// Canonical scheduling problem — solver-agnostic input. Every offset, duration and lag in it
/// is in working minutes on <see cref="Axis"/>; only the service converts back to timestamps.
/// </summary>
public sealed record SchedulingProblem(
    Guid SiteId,
    DateOnly HorizonStart,
    DateOnly HorizonEnd,
    Services.AutoSchedule.WorkingTimeAxis Axis,
    IReadOnlyList<RequestNode> Requests,
    IReadOnlyList<ResourceNode> Resources,
    IReadOnlyList<FixedOccupancy> FixedAssignments,
    IReadOnlyList<DependencyEdge>? Dependencies = null,
    IReadOnlyList<WithheldRequestNode>? Withheld = null,
    /// <summary>The join condition of every request that has incoming edges. Part of the
    /// preview's identity: changing a condition changes what a valid plan is, so it belongs in
    /// the fingerprint alongside the edges.</summary>
    IReadOnlyDictionary<Guid, JoinCondition>? JoinConditions = null,
    /// <summary>Which resource types each required criterion applies to, so a criterion scoped
    /// to mills is not demanded of the van the same request also needs. Absent or empty means
    /// every type.</summary>
    IReadOnlyDictionary<Guid, IReadOnlySet<string>>? CriterionTypeScopes = null);

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
/// window instead, and one whose predecessor is absent rejects the successor outright.
/// Lag is in working minutes on the axis, like every other quantity the solver sees: it
/// can only ever delay the successor, never let it start before the gap has elapsed.
/// </summary>
public sealed record DependencyEdge(
    Guid PredecessorRequestId,
    Guid SuccessorRequestId,
    int LagMinutes);

/// <summary>
/// A request to place. <see cref="EarliestStart"/> and <see cref="LatestEnd"/> are offsets on
/// the axis (null = the horizon edge); <see cref="DurationMinutes"/> is working time.
/// <see cref="OpenTypeKeys"/> are the resource types this run must fill for it — every one of
/// them, at the same start, or none.
/// </summary>
public sealed record RequestNode(
    Guid RequestId,
    string DisplayName,
    int? EarliestStart,
    int? LatestEnd,
    int DurationMinutes,
    int Priority,
    IReadOnlySet<Guid> RequiredCriterionIds,
    IReadOnlySet<string> OpenTypeKeys);

public sealed record ResourceNode(
    Guid ResourceId,
    string DisplayName,
    string ResourceTypeKey,
    IReadOnlySet<Guid> CriterionIds);

/// <summary>
/// Time a resource is already taken, as a half-open <c>[Start, End)</c> on the axis. A range
/// that lies entirely in non-working time collapses to a point (<c>Start == End</c>); it is
/// kept, because a placement may touch that point but must not span it — the booking still
/// covers the calendar weekend the axis compressed away. <see cref="RequestId"/> is
/// <see cref="Guid.Empty"/> for a resource's own blocked period rather than a request.
/// </summary>
public sealed record FixedOccupancy(
    Guid RequestId,
    Guid ResourceId,
    int Start,
    int End);

/// <summary>A half-open range of start offsets, <c>[From, To)</c>.</summary>
public readonly record struct StartWindow(int From, int To)
{
    public int Length => To - From;
}

/// <summary>
/// A feasible request→resource candidate for one of the request's open types, with the windows
/// its start may fall in: the request's own window minus everything the resource is already
/// taken for. A request reaches the solvers only with candidates for every open type.
/// </summary>
public sealed record SchedulingCandidate(
    Guid RequestId,
    Guid ResourceId,
    string ResourceTypeKey,
    int EarliestStart,
    int LatestEnd,
    int DurationMinutes,
    int Priority,
    IReadOnlyList<StartWindow> FeasibleStartWindows);

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
    /// <param name="joinConditions">
    /// The join condition of each request with incoming edges, for the same reason: switching a
    /// request from "any predecessor" to "all" between preview and apply invalidates a plan that
    /// the edges alone still describe perfectly.
    /// </param>
    public string ComputeFingerprint(
        IEnumerable<string> resourceTypeKeys,
        IEnumerable<DependencyEdge> edges,
        IReadOnlyDictionary<Guid, JoinCondition>? joinConditions = null)
    {
        // The type set is part of the identity, not just the assignments: an empty solution
        // hashes the same for every set, so without it a preview that proposed nothing would
        // match an apply for any set.
        var sb = new StringBuilder(string.Join(',', resourceTypeKeys.Order())).Append('#');
        foreach (var e in edges.OrderBy(e => e.PredecessorRequestId).ThenBy(e => e.SuccessorRequestId))
        {
            sb.Append(e.PredecessorRequestId).Append('>')
              .Append(e.SuccessorRequestId).Append('+')
              .Append(e.LagMinutes).Append(';');
        }
        sb.Append('#');
        foreach (var (requestId, condition) in (joinConditions ?? new Dictionary<Guid, JoinCondition>())
                     .OrderBy(kv => kv.Key))
        {
            // The DB string, not the enum member name: hashing "KOfN" would tie every in-flight
            // preview's validity to a C# identifier that a rename could change.
            sb.Append(requestId).Append(':')
              .Append(EnumMapper.ToDbValue(condition.Logic)).Append(':')
              .Append(condition.K).Append(';');
        }
        sb.Append('#');
        foreach (var a in Assignments.OrderBy(a => a.RequestId))
        {
            sb.Append(a.RequestId).Append('|');
            foreach (var r in a.Resources.OrderBy(r => r.TypeKey, StringComparer.Ordinal))
                sb.Append(r.TypeKey).Append(':').Append(r.ResourceId).Append(',');
            sb.Append('|')
              .Append(a.Start).Append('|')
              .Append(a.End).Append(';');
        }
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexStringLower(hash);
    }
}

/// <summary>
/// A placement on the axis: half-open <c>[Start, End)</c>, <c>End == Start + DurationMinutes</c>,
/// on one resource per type the request had open.
/// </summary>
public sealed record ScheduledPlacement(
    Guid RequestId,
    IReadOnlyList<PlacedResource> Resources,
    int Start,
    int End,
    int DurationMinutes,
    int Priority);

public sealed record PlacedResource(string TypeKey, Guid ResourceId);

public sealed record UnscheduledPlacement(
    Guid RequestId,
    IReadOnlyList<SchedulingReasonCode> ReasonCodes);
