using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace Api.Models;

/// <summary>
/// Mode for auto-scheduling.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AutoScheduleMode
{
    /// <summary>Fill gaps only — existing scheduled items remain fixed.</summary>
    FillGapsOnly = 0
}

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
    Unknown = 3,
    Error = 4
}

/// <summary>
/// Reason code explaining why a request could not be scheduled.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SchedulingReasonCode
{
    None = 0,
    NoCompatibleSpace = 1,
    DateWindowTooTight = 2,
    InsufficientCapacity = 3,
    BlockedByFixedAssignments = 4,
    InvalidDuration = 5,
    MissingRequiredData = 6,
    InternalSolverLimit = 7
}

// ── Request / Response DTOs ────────────────────────────────────────

public sealed record AutoSchedulePreviewRequest(
    Guid SiteId,
    DateOnly HorizonStart,
    DateOnly HorizonEnd,
    AutoScheduleMode Mode = AutoScheduleMode.FillGapsOnly,
    IReadOnlyCollection<Guid>? RequestIds = null,
    bool RespectSchedulingSettings = true);

public sealed record AutoScheduleApplyRequest(
    Guid SiteId,
    DateOnly HorizonStart,
    DateOnly HorizonEnd,
    AutoScheduleMode Mode = AutoScheduleMode.FillGapsOnly,
    IReadOnlyCollection<Guid>? RequestIds = null,
    bool RespectSchedulingSettings = true,
    string? PreviewFingerprint = null);

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
    Guid SpaceId,
    string SpaceName,
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
    IReadOnlyList<SpaceNode> Spaces,
    IReadOnlyList<FixedOccupancy> FixedAssignments,
    SchedulingSettingsInfo? Settings,
    List<OffTimeInfo>? OffTimes,
    AutoScheduleMode Mode);

public sealed record RequestNode(
    Guid RequestId,
    string DisplayName,
    DateOnly? EarliestStart,
    DateOnly? LatestEnd,
    int DurationDays,
    int Priority,
    bool RespectSchedulingSettings,
    IReadOnlySet<Guid> RequiredCriterionIds);

public sealed record SpaceNode(
    Guid SpaceId,
    string DisplayName,
    IReadOnlySet<Guid> CriterionIds);

public sealed record FixedOccupancy(
    Guid RequestId,
    Guid SpaceId,
    DateOnly Start,
    DateOnly End);

/// <summary>
/// A feasible request→space candidate with enumerated start days.
/// </summary>
public sealed record SchedulingCandidate(
    Guid RequestId,
    Guid SpaceId,
    DateOnly EarliestStart,
    DateOnly LatestEnd,
    int DurationDays,
    int Priority,
    IReadOnlyList<DateOnly> FeasibleStartDays);

public sealed record CandidateRejection(
    Guid RequestId,
    Guid? SpaceId,
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
    public string ComputeFingerprint()
    {
        var sb = new StringBuilder();
        foreach (var a in Assignments.OrderBy(a => a.RequestId).ThenBy(a => a.SpaceId))
        {
            sb.Append(a.RequestId).Append('|')
              .Append(a.SpaceId).Append('|')
              .Append(a.Start).Append('|')
              .Append(a.End).Append(';');
        }
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexStringLower(hash);
    }
}

public sealed record ScheduledPlacement(
    Guid RequestId,
    Guid SpaceId,
    DateOnly Start,
    DateOnly End,
    int DurationDays,
    int Priority);

public sealed record UnscheduledPlacement(
    Guid RequestId,
    IReadOnlyList<SchedulingReasonCode> ReasonCodes);
