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
    InternalSolverLimit = 7
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
    Dictionary<Guid, List<BlockedPeriod>>? BlockedPeriodsByResource);

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
    public string ComputeFingerprint(string resourceTypeKey)
    {
        // The type is part of the identity, not just the assignments: an empty solution hashes
        // the same for every type, so without it a preview that proposed nothing would match
        // an apply for any type.
        var sb = new StringBuilder(resourceTypeKey).Append('#');
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
