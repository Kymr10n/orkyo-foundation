using Api.Constants;
using Api.Models;
using Api.Repositories;

namespace Api.Services;

/// <summary>
/// Authoritative, tenant-wide conflicts registry for the Conflicts page and the Requests-page
/// badges. Computes every scheduled request's conflicts server-side by evaluating its whole
/// assignment set — whatever resource types it targets — through the shared, bulk-optimized
/// <see cref="IResourceAssignmentValidator.ValidateBatchAsync"/> (overbook / capacity / off-time /
/// weekend / site), checking capability at the request level (a requirement is satisfied iff ANY
/// assigned resource satisfies it, so each capability lands on the resources whose type can carry
/// it), adding the cheap request-intrinsic checks (below-min-duration, before-earliest-start,
/// after-latest-end), and checking precedence against the request-dependency graph. Computed on
/// demand (no DB materialization).
/// </summary>
public interface IConflictService
{
    /// <summary>
    /// One entry per request that has at least one conflict. Tenant-wide; all-time by default, or
    /// scoped to scheduled bars overlapping [<paramref name="from"/>,<paramref name="to"/>] when a
    /// window is supplied (the utilization grid passes its visible window; the Conflicts page omits
    /// it for the authoritative all-time view).
    /// </summary>
    Task<List<RequestConflictInfo>> GetAllAsync(DateTime? from = null, DateTime? to = null, CancellationToken ct = default);
}

public class ConflictService(
    IRequestRepository requestRepository,
    IResourceAssignmentValidator validator,
    ICapabilityMatcher capabilityMatcher,
    IResourceCapabilityRepository capabilityRepository,
    IRequestDependencyRepository dependencyRepository) : IConflictService
{
    public async Task<List<RequestConflictInfo>> GetAllAsync(DateTime? from = null, DateTime? to = null, CancellationToken ct = default)
    {
        var requests = from.HasValue && to.HasValue
            ? await requestRepository.GetScheduledAsync(from.Value, to.Value, ct)
            : await requestRepository.GetScheduledAsync(ct);
        if (requests.Count == 0) return [];

        // Validate every non-cancelled assignment (room + people + tools) of each request in one
        // bulk round-trip. Carry an assignmentId → requestId map so overbook conflicts can name the
        // peer request — for any resource type, not just the room.
        var requestByAssignmentId = new Dictionary<Guid, Guid>();
        var items = new List<ValidateResourceAssignmentRequest>();
        foreach (var r in requests)
            foreach (var a in r.Assignments.Where(a => a.AssignmentStatus != AssignmentStatuses.Cancelled))
            {
                requestByAssignmentId[a.Id] = r.Id;
                items.Add(new ValidateResourceAssignmentRequest
                {
                    RequestId = r.Id,
                    ResourceId = a.ResourceId,
                    StartUtc = a.StartUtc,
                    EndUtc = a.EndUtc,
                    AllocationPercent = a.AllocationPercent,
                    ExcludeAssignmentId = a.Id,
                });
            }

        // Multiple items per request now → group results rather than keying by request id.
        var validationByRequest = (await validator.ValidateBatchAsync(items, ct))
            .Where(v => v.RequestId.HasValue)
            .GroupBy(v => v.RequestId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(v => v.Result).ToList());

        var capsByResource = await LoadCapabilitiesAsync(requests, ct);

        // Precedence edges pointing at anything in this batch — one read, never per request.
        var edgesBySuccessor = (await dependencyRepository.GetBySuccessorsAsync(
                requests.Select(r => r.Id).ToList(), ct))
            .GroupBy(e => e.SuccessorRequestId)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Predecessor placements are needed even when the predecessor itself is outside the
        // window being examined, so resolve any that the batch does not already carry.
        var byId = requests.ToDictionary(r => r.Id);
        var missingPredecessors = edgesBySuccessor.Values
            .SelectMany(list => list.Select(e => e.PredecessorRequestId))
            .Where(id => !byId.ContainsKey(id))
            .Distinct()
            .ToList();
        if (missingPredecessors.Count > 0)
            foreach (var p in await requestRepository.GetByIdsAsync(missingPredecessors, includeRequirements: false, ct))
                byId[p.Id] = p;

        var result = new List<RequestConflictInfo>();
        foreach (var request in requests)
        {
            var conflicts = new List<ConflictInfo>();

            if (validationByRequest.TryGetValue(request.Id, out var results))
                foreach (var v in results)
                    foreach (var issue in v.Blockers.Concat(v.Warnings))
                    {
                        var mapped = MapIssue(request.Id, issue, requestByAssignmentId);
                        if (mapped is not null) conflicts.Add(mapped);
                    }

            conflicts.AddRange(CapabilityConflicts(request, capsByResource));
            conflicts.AddRange(IntrinsicConflicts(request));
            conflicts.AddRange(DependencyConflicts(request, edgesBySuccessor, byId));

            if (conflicts.Count > 0)
                result.Add(new RequestConflictInfo { RequestId = request.Id, Conflicts = conflicts });
        }

        return result;
    }

    /// <summary>
    /// Request-level capability: a requirement is satisfied iff at least one assigned resource has a
    /// matching capability. Because only applicable resource types ever hold a given capability, each
    /// requirement is effectively matched against the assigned resources whose type can carry it —
    /// without falsely flagging the others for "missing" it. One conflict per unmet requirement.
    /// </summary>
    private IEnumerable<ConflictInfo> CapabilityConflicts(
        RequestInfo request, IReadOnlyDictionary<Guid, IReadOnlyList<ResourceCapabilityInfo>> capsByResource)
    {
        foreach (var requirement in request.Requirements ?? [])
        {
            var satisfied = request.Assignments
                .Where(a => a.AssignmentStatus != AssignmentStatuses.Cancelled)
                .Any(a => capabilityMatcher.Satisfies(capsByResource.GetValueOrDefault(a.ResourceId, []), requirement));
            if (!satisfied)
                yield return new ConflictInfo
                {
                    Id = $"{request.Id}-{requirement.CriterionId}-capability",
                    Kind = ConflictKinds.ConnectorMismatch,
                    Severity = ConflictSeverities.Error,
                    Message = $"No assigned resource provides '{requirement.Criterion?.Name ?? "a required capability"}'",
                    CriterionId = requirement.CriterionId,
                };
        }
    }

    private async Task<IReadOnlyDictionary<Guid, IReadOnlyList<ResourceCapabilityInfo>>> LoadCapabilitiesAsync(
        IReadOnlyList<RequestInfo> requests, CancellationToken ct)
    {
        var resourceIds = requests
            .SelectMany(r => r.Assignments)
            .Where(a => a.AssignmentStatus != AssignmentStatuses.Cancelled)
            .Select(a => a.ResourceId)
            .Distinct()
            .ToList();
        if (resourceIds.Count == 0)
            return new Dictionary<Guid, IReadOnlyList<ResourceCapabilityInfo>>();

        return (await capabilityRepository.GetByResourcesAsync(resourceIds, ct))
            .GroupBy(c => c.ResourceId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<ResourceCapabilityInfo>)g.ToList());
    }

    /// <summary>Maps a validator issue to the FE conflict shape; returns null for issues that
    /// aren't schedule conflicts (e.g. allocation-config errors).</summary>
    private static ConflictInfo? MapIssue(
        Guid requestId, ValidationIssue issue, IReadOnlyDictionary<Guid, Guid> requestByAssignmentId)
    {
        switch (issue.Code)
        {
            // Capability is evaluated at the request level (CapabilityConflicts), not per-resource —
            // a room legitimately lacks the person-skills its job's people supply. Drop the
            // per-resource verdict here so it isn't double-counted or falsely raised.
            case ValidationReasonCode.CapabilityMissing:
            case ValidationReasonCode.ResourceTypeMismatch:
                return null;

            case ValidationReasonCode.AssignmentOverbooked:
                // Exclusive overbook carries the conflicting assignment id → it's an overlap with a
                // peer request. Fractional capacity overbook has no peer → capacity_exceeded.
                if (issue.ConflictingAssignmentId is { } caId)
                {
                    requestByAssignmentId.TryGetValue(caId, out var peer);
                    return new ConflictInfo
                    {
                        Id = $"{requestId}-overlap-{caId}",
                        Kind = ConflictKinds.Overlap,
                        Severity = ConflictSeverities.Error,
                        Message = issue.Message,
                        PeerRequestId = peer == Guid.Empty ? null : peer,
                        ResourceId = issue.ResourceId,
                    };
                }
                return new ConflictInfo
                {
                    Id = $"{requestId}-{issue.ResourceId}-capacity-exceeded",
                    Kind = ConflictKinds.CapacityExceeded,
                    Severity = ConflictSeverities.Error,
                    Message = issue.Message,
                    ResourceId = issue.ResourceId,
                };

            case ValidationReasonCode.OffTimeOverlap:
            case ValidationReasonCode.NonWorkingHoliday:
            case ValidationReasonCode.NonWorkingWeekend:
                // The blocked period belongs in the id. One assignment can overlap several
                // — two closures, a holiday and a shutdown — and the validator raises one
                // issue per period, all with the same code and resource. Without the
                // period, those conflicts shared an id and React rendered only one of
                // them. The overlap branch above already keys on its peer for exactly
                // this reason.
                return new ConflictInfo
                {
                    Id = $"{requestId}-{issue.ResourceId}-{issue.Code}-{issue.ConflictingAvailabilityId}-offtime",
                    Kind = ConflictKinds.StartsInOffTime,
                    Severity = ConflictSeverities.Warning,
                    Message = issue.Message,
                    ResourceId = issue.ResourceId,
                };

            case ValidationReasonCode.SiteCrossNotAllowed:
            case ValidationReasonCode.SiteMismatchPerson:
                return new ConflictInfo
                {
                    Id = $"{requestId}-{issue.ResourceId}-site",
                    Kind = ConflictKinds.SiteMismatch,
                    // A mismatch on a resource allowed to travel is advisory; on one that is
                    // not allowed to travel it is an error.
                    Severity = issue.Code == ValidationReasonCode.SiteMismatchPerson ? "warning" : "error",
                    Message = issue.Message,
                    ResourceId = issue.ResourceId,
                };

            // Config errors (invalid allocation mode/percent) and resource not-found/inactive are
            // not schedule conflicts surfaced on the grid/page.
            default:
                return null;
        }
    }

    /// <summary>
    /// Precedence violations on a scheduled request: it starts before the predecessor it waits
    /// for has finished (error), or that predecessor is not scheduled at all (warning — the plan
    /// is incomplete rather than wrong). Nothing is reported while the successor itself is
    /// unscheduled: a backlog item breaks no promise.
    /// </summary>
    private static IEnumerable<ConflictInfo> DependencyConflicts(
        RequestInfo request,
        IReadOnlyDictionary<Guid, List<RequestDependencyInfo>> edgesBySuccessor,
        IReadOnlyDictionary<Guid, RequestInfo> byId)
    {
        if (request.StartTs is not { } start) yield break;
        if (!edgesBySuccessor.TryGetValue(request.Id, out var edges)) yield break;

        foreach (var edge in edges)
        {
            var name = edge.PredecessorName;

            if (!byId.TryGetValue(edge.PredecessorRequestId, out var predecessor)
                || predecessor.EndTs is not { } predecessorEnd)
            {
                yield return new ConflictInfo
                {
                    Id = $"{request.Id}-{edge.PredecessorRequestId}-dependency-unscheduled",
                    Kind = ConflictKinds.DependencyViolation,
                    Severity = ConflictSeverities.Warning,
                    Message = $"Its predecessor '{name}' is not scheduled",
                    PeerRequestId = edge.PredecessorRequestId,
                };
                continue;
            }

            // Whole days, matching the critical path. The scheduler and the CPM pass both work
            // in day buckets, so a successor starting the same calendar day its predecessor ends
            // is a violation to them; comparing raw timestamps here would report it clean and
            // leave the Conflicts page contradicting the critical-path view.
            // Inclusive last day (end_ts is half-open): the raw date of a midnight end would
            // flag a successor correctly placed the very next day as a violation.
            var earliest = SchedulingEngine.InclusiveLastDay(predecessorEnd)
                .AddDays(1 + (int)Math.Ceiling(edge.LagMinutes / (double)(24 * 60)));
            if (DateOnly.FromDateTime(start) < earliest)
            {
                yield return new ConflictInfo
                {
                    Id = $"{request.Id}-{edge.PredecessorRequestId}-dependency-violation",
                    Kind = ConflictKinds.DependencyViolation,
                    Severity = ConflictSeverities.Error,
                    Message = edge.LagMinutes > 0
                        ? $"Starts before its predecessor '{name}' finishes plus the required gap"
                        : $"Starts before its predecessor '{name}' finishes",
                    PeerRequestId = edge.PredecessorRequestId,
                };
            }
        }
    }

    /// <summary>Request-intrinsic checks computed directly from the request's own fields.</summary>
    private static IEnumerable<ConflictInfo> IntrinsicConflicts(RequestInfo request)
    {
        if (request.StartTs is not { } start || request.EndTs is not { } end)
            yield break;

        var minMinutes = SchedulingEngine.DurationToMinutes(request.MinimalDurationValue, request.MinimalDurationUnit);
        if ((end - start).TotalMinutes < minMinutes)
            yield return new ConflictInfo
            {
                Id = $"{request.Id}-below-min-duration",
                Kind = ConflictKinds.BelowMinDuration,
                Severity = ConflictSeverities.Error,
                Message = "Duration is below the required minimum",
            };

        if (request.EarliestStartTs is { } earliest && start < earliest)
            yield return new ConflictInfo
            {
                Id = $"{request.Id}-before-earliest-start",
                Kind = ConflictKinds.BeforeEarliestStart,
                Severity = ConflictSeverities.Error,
                Message = "Starts before the earliest allowed start",
            };

        if (request.LatestEndTs is { } latest && end > latest)
            yield return new ConflictInfo
            {
                Id = $"{request.Id}-after-latest-end",
                Kind = ConflictKinds.AfterLatestEnd,
                Severity = ConflictSeverities.Error,
                Message = "Ends after the latest allowed end",
            };
    }
}
