using Api.Constants;
using Api.Models;
using Api.Repositories;

namespace Api.Services;

/// <summary>
/// Authoritative, tenant-wide conflicts registry for the Conflicts page and the Requests-page
/// badges. Computes every scheduled request's conflicts server-side by reusing the shared,
/// bulk-optimized <see cref="IResourceAssignmentValidator.ValidateBatchAsync"/> (capability /
/// overbook / capacity / off-time / weekend) and adding the cheap request-intrinsic checks
/// (below-min-duration, before-earliest-start, after-latest-end). Computed on demand — see the
/// caching note in the plan (no DB materialization).
/// </summary>
public interface IConflictService
{
    /// <summary>One entry per request that has at least one conflict, tenant-wide / all-time.</summary>
    Task<List<RequestConflictInfo>> GetAllAsync(CancellationToken ct = default);
}

public class ConflictService(
    IRequestRepository requestRepository,
    IResourceAssignmentValidator validator) : IConflictService
{
    public async Task<List<RequestConflictInfo>> GetAllAsync(CancellationToken ct = default)
    {
        var requests = await requestRepository.GetScheduledAsync(ct);
        if (requests.Count == 0) return [];

        // Validate each request's space assignment in one bulk round-trip. Carry a
        // spaceAssignmentId → requestId map so overbook conflicts can name the peer request.
        var requestByAssignmentId = new Dictionary<Guid, Guid>();
        var items = new List<ValidateResourceAssignmentRequest>(requests.Count);
        foreach (var r in requests)
        {
            var space = r.Assignments.FirstOrDefault(a => a.ResourceTypeKey == ResourceTypeKeys.Space);
            if (space is null) continue;
            requestByAssignmentId[space.Id] = r.Id;
            items.Add(new ValidateResourceAssignmentRequest
            {
                RequestId = r.Id,
                ResourceId = space.ResourceId,
                StartUtc = space.StartUtc,
                EndUtc = space.EndUtc,
                AllocationPercent = space.AllocationPercent,
                ExcludeAssignmentId = space.Id,
            });
        }

        var validation = (await validator.ValidateBatchAsync(items, ct))
            .Where(v => v.RequestId.HasValue)
            .ToDictionary(v => v.RequestId!.Value, v => v.Result);

        var result = new List<RequestConflictInfo>();
        foreach (var request in requests)
        {
            var conflicts = new List<ConflictInfo>();

            if (validation.TryGetValue(request.Id, out var v))
            {
                foreach (var issue in v.Blockers.Concat(v.Warnings))
                {
                    var mapped = MapIssue(request.Id, issue, requestByAssignmentId);
                    if (mapped is not null) conflicts.Add(mapped);
                }
            }

            conflicts.AddRange(IntrinsicConflicts(request));

            if (conflicts.Count > 0)
                result.Add(new RequestConflictInfo { RequestId = request.Id, Conflicts = conflicts });
        }

        return result;
    }

    /// <summary>Maps a validator issue to the FE conflict shape; returns null for issues that
    /// aren't schedule conflicts (e.g. allocation-config errors).</summary>
    private static ConflictInfo? MapIssue(
        Guid requestId, ValidationIssue issue, IReadOnlyDictionary<Guid, Guid> requestByAssignmentId)
    {
        switch (issue.Code)
        {
            case ValidationReasonCode.CapabilityMissing:
            case ValidationReasonCode.ResourceTypeMismatch:
                return new ConflictInfo
                {
                    Id = $"{requestId}-{issue.CriterionId?.ToString() ?? "cap"}-capability",
                    Kind = "connector_mismatch",
                    Severity = "error",
                    Message = issue.Message,
                };

            case ValidationReasonCode.AssignmentOverbooked:
                // Exclusive overbook carries the conflicting assignment id → it's an overlap with a
                // peer request. Fractional capacity overbook has no peer → capacity_exceeded.
                if (issue.ConflictingAssignmentId is { } caId)
                {
                    requestByAssignmentId.TryGetValue(caId, out var peer);
                    return new ConflictInfo
                    {
                        Id = $"{requestId}-overlap-{caId}",
                        Kind = "overlap",
                        Severity = "error",
                        Message = issue.Message,
                        PeerRequestId = peer == Guid.Empty ? null : peer,
                    };
                }
                return new ConflictInfo
                {
                    Id = $"{requestId}-capacity-exceeded",
                    Kind = "capacity_exceeded",
                    Severity = "error",
                    Message = issue.Message,
                };

            case ValidationReasonCode.OffTimeOverlap:
            case ValidationReasonCode.NonWorkingHoliday:
            case ValidationReasonCode.NonWorkingWeekend:
                return new ConflictInfo
                {
                    Id = $"{requestId}-{issue.Code}-offtime",
                    Kind = "starts_in_off_time",
                    Severity = "warning",
                    Message = issue.Message,
                };

            case ValidationReasonCode.SiteMismatchSpace:
            case ValidationReasonCode.SiteCrossNotAllowed:
            case ValidationReasonCode.SiteMismatchPerson:
                return new ConflictInfo
                {
                    Id = $"{requestId}-{issue.ResourceId}-site",
                    Kind = "site_mismatch",
                    // Cross-site-allowed person mismatch is advisory; space/not-allowed are errors.
                    Severity = issue.Code == ValidationReasonCode.SiteMismatchPerson ? "warning" : "error",
                    Message = issue.Message,
                };

            // Config errors (invalid allocation mode/percent) and resource not-found/inactive are
            // not schedule conflicts surfaced on the grid/page.
            default:
                return null;
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
                Kind = "below_min_duration",
                Severity = "error",
                Message = "Duration is below the required minimum",
            };

        if (request.EarliestStartTs is { } earliest && start < earliest)
            yield return new ConflictInfo
            {
                Id = $"{request.Id}-before-earliest-start",
                Kind = "before_earliest_start",
                Severity = "error",
                Message = "Starts before the earliest allowed start",
            };

        if (request.LatestEndTs is { } latest && end > latest)
            yield return new ConflictInfo
            {
                Id = $"{request.Id}-after-latest-end",
                Kind = "after_latest_end",
                Severity = "error",
                Message = "Ends after the latest allowed end",
            };
    }
}
