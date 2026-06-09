using Api.Constants;
using Api.Models;
using Api.Repositories;

namespace Api.Services;

/// <summary>
/// Validates resource assignments before creation, returning blockers and warnings.
/// </summary>
public interface IResourceAssignmentValidator
{
    Task<ValidationResult> ValidateAsync(ValidateResourceAssignmentRequest request, CancellationToken ct = default);

    /// <summary>
    /// Validates many assignment pairings, returning one result per input item
    /// (correlated by request/resource id). Lets the conflicts view evaluate the
    /// whole schedule in a single round-trip instead of N calls.
    /// </summary>
    Task<List<AssignmentValidationBatchItem>> ValidateBatchAsync(
        IReadOnlyList<ValidateResourceAssignmentRequest> requests, CancellationToken ct = default);
}

/// <summary>
/// Checks all assignment constraints (resource existence, capabilities, off-times,
/// allocation mode rules, capacity).
///
/// The post-fetch decision logic for each rule lives in a pure static
/// <c>Evaluate*</c> helper. The single-item <see cref="ValidateAsync"/> fetches per-row and
/// delegates to them; <see cref="ValidateBatchAsync"/> preloads everything in a handful of bulk
/// queries and feeds the same helpers — so the rules live in exactly one place and the batch
/// avoids the per-item N+1.
/// </summary>
public class ResourceAssignmentValidator(
    IResourceRepository resourceRepository,
    IResourceAssignmentRepository assignmentRepository,
    ICapabilityMatcher capabilityMatcher,
    IResourceCapabilityRepository capabilityRepository,
    IAvailabilityResolver availabilityResolver,
    IRequestRepository requestRepository,
    ISchedulingRepository schedulingRepository) : IResourceAssignmentValidator
{
    public async Task<ValidationResult> ValidateAsync(ValidateResourceAssignmentRequest request, CancellationToken ct = default)
    {
        var blockers = new List<ValidationIssue>();
        var warnings = new List<ValidationIssue>();

        var resource = await resourceRepository.GetByIdAsync(request.ResourceId);
        if (resource is null)
        {
            blockers.Add(NotFound(request.ResourceId));
            return Build(blockers, warnings);
        }

        if (!resource.IsActive)
            blockers.Add(Inactive(resource.Id));

        await CheckCapabilitiesAsync(request, resource, blockers);
        await CheckSiteScopedWindowAsync(request, resource, warnings, ct);
        await CheckAllocationAsync(request, resource, blockers);

        return Build(blockers, warnings);
    }

    public async Task<List<AssignmentValidationBatchItem>> ValidateBatchAsync(
        IReadOnlyList<ValidateResourceAssignmentRequest> requests, CancellationToken ct = default)
    {
        if (requests.Count == 0) return [];

        // ── Preload everything the per-item rules need, in a handful of bulk queries ──
        var resourceIds = requests.Select(r => r.ResourceId).Distinct().ToList();
        var requestIds = requests.Where(r => r.RequestId.HasValue).Select(r => r.RequestId!.Value).Distinct().ToList();
        var windowStart = requests.Min(r => r.StartUtc);
        var windowEnd = requests.Max(r => r.EndUtc);

        var resources = (await resourceRepository.GetByIdsAsync(resourceIds, ct))
            .ToDictionary(r => r.Id);
        var requestsById = (await requestRepository.GetByIdsAsync(requestIds, includeRequirements: true, ct))
            .ToDictionary(r => r.Id);
        var capabilitiesByResource = (await capabilityRepository.GetByResourcesAsync(resourceIds, ct))
            .GroupBy(c => c.ResourceId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<ResourceCapabilityInfo>)g.ToList());
        var siteByResource = await schedulingRepository.GetSiteIdsForResourcesAsync(resourceIds, ct);
        var activeByResource = (await assignmentRepository.GetActiveByResourcesAsync(resourceIds, windowStart, windowEnd, ct))
            .GroupBy(a => a.ResourceId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<ResourceAssignmentInfo>)g.ToList());

        // Off-times only apply to sited resources (matches the single path's early-return).
        // Reuse the single-resource resolver per distinct resource for exact parity.
        var blockedByResource = new Dictionary<Guid, List<BlockedPeriod>>();
        foreach (var rid in resourceIds)
            if (siteByResource.ContainsKey(rid))
                blockedByResource[rid] = await availabilityResolver.GetBlockedPeriodsAsync(rid, ct);

        // Settings per distinct site (only a few sites).
        var settingsBySite = new Dictionary<Guid, SchedulingSettingsInfo?>();
        foreach (var siteId in siteByResource.Values.Distinct())
            settingsBySite[siteId] = await schedulingRepository.GetSettingsAsync(siteId, ct);

        // ── Evaluate each item in memory ──
        var results = new List<AssignmentValidationBatchItem>(requests.Count);
        foreach (var request in requests)
        {
            var blockers = new List<ValidationIssue>();
            var warnings = new List<ValidationIssue>();

            if (!resources.TryGetValue(request.ResourceId, out var resource))
            {
                blockers.Add(NotFound(request.ResourceId));
                results.Add(Correlated(request, Build(blockers, warnings)));
                continue;
            }

            if (!resource.IsActive)
                blockers.Add(Inactive(resource.Id));

            // Capabilities (skip dry-run items with no request id, mirroring the single path).
            if (request.RequestId is { } reqId && requestsById.TryGetValue(reqId, out var requestInfo))
            {
                var caps = capabilitiesByResource.GetValueOrDefault(resource.Id, []);
                foreach (var requirement in requestInfo.Requirements ?? [])
                    if (!capabilityMatcher.Satisfies(caps, requirement))
                        blockers.Add(CapabilityMissing(resource.Id, requirement));
            }

            // Site-scoped non-working time.
            if (siteByResource.TryGetValue(resource.Id, out var siteId))
            {
                EvaluateBlockedPeriods(request, resource.Id, blockedByResource.GetValueOrDefault(resource.Id, []), warnings);
                EvaluateWeekends(request, resource.Id, settingsBySite.GetValueOrDefault(siteId), warnings);
            }

            // Allocation: compute the overlapping set / committed total in memory from the
            // resource's active assignments, then apply the shared rule.
            var active = activeByResource.GetValueOrDefault(resource.Id, []);
            var mode = request.AllocationMode ?? resource.AllocationMode;
            var overlapping = mode == AllocationModes.Exclusive
                ? active.Where(a => Overlaps(a, request) && NotExcluded(a, request)).ToList()
                : [];
            var fractionalTotal = mode == AllocationModes.Fractional
                ? active.Where(a => Overlaps(a, request)).Sum(a => a.AllocationPercent ?? 0m)
                : 0m;
            EvaluateAllocation(request, resource, overlapping, fractionalTotal, blockers);

            results.Add(Correlated(request, Build(blockers, warnings)));
        }

        return results;
    }

    // ── Single-item checks: fetch per-row, then delegate to the shared Evaluate* helpers ──

    private async Task CheckSiteScopedWindowAsync(
        ValidateResourceAssignmentRequest request, ResourceInfo resource, List<ValidationIssue> warnings, CancellationToken ct = default)
    {
        var siteId = await schedulingRepository.GetSiteIdForResourceAsync(resource.Id);
        if (siteId is null) return;

        var blocked = await availabilityResolver.GetBlockedPeriodsAsync(resource.Id, ct);
        EvaluateBlockedPeriods(request, resource.Id, blocked, warnings);

        var settings = await schedulingRepository.GetSettingsAsync(siteId.Value);
        EvaluateWeekends(request, resource.Id, settings, warnings);
    }

    private async Task CheckCapabilitiesAsync(
        ValidateResourceAssignmentRequest request, ResourceInfo resource, List<ValidationIssue> blockers, CancellationToken ct = default)
    {
        // Dry-run path: a not-yet-created request has no requirements to check.
        if (request.RequestId is null) return;

        var requestInfo = await requestRepository.GetByIdAsync(request.RequestId.Value, includeRequirements: true, ct);
        foreach (var req in requestInfo?.Requirements ?? [])
        {
            var satisfied = await capabilityMatcher.ResourceSatisfiesRequirementAsync(resource.Id, req, ct);
            if (!satisfied)
                blockers.Add(CapabilityMissing(resource.Id, req));
        }
    }

    private async Task CheckAllocationAsync(
        ValidateResourceAssignmentRequest request, ResourceInfo resource, List<ValidationIssue> blockers)
    {
        var mode = request.AllocationMode ?? resource.AllocationMode;

        // Fetch only the data the active mode needs, then apply the shared rule.
        IReadOnlyList<ResourceAssignmentInfo> overlapping = [];
        decimal fractionalTotal = 0m;
        if (mode == AllocationModes.Exclusive)
            overlapping = await assignmentRepository.GetOverlappingActiveAsync(
                resource.Id, request.StartUtc, request.EndUtc, request.ExcludeAssignmentId);
        else if (mode == AllocationModes.Fractional)
            fractionalTotal = await assignmentRepository.GetTotalAllocatedPercentAsync(
                resource.Id, request.StartUtc, request.EndUtc);

        EvaluateAllocation(request, resource, overlapping, fractionalTotal, blockers);
    }

    // ── Pure rule evaluators (no I/O) — shared by the single and batch paths ──

    /// <summary>
    /// Allocation-mode rules. Callers pre-compute the inputs so this stays pure:
    /// <paramref name="overlappingActive"/> is the already-overlapping, self-excluded set used for
    /// Exclusive overbook; <paramref name="committedFractionalTotal"/> is the committed % over the
    /// window used for Fractional capacity. Each is empty/zero for the non-applicable mode.
    /// </summary>
    private static void EvaluateAllocation(
        ValidateResourceAssignmentRequest request, ResourceInfo resource,
        IReadOnlyList<ResourceAssignmentInfo> overlappingActive, decimal committedFractionalTotal,
        List<ValidationIssue> blockers)
    {
        var mode = request.AllocationMode ?? resource.AllocationMode;

        if (mode == AllocationModes.ConcurrentCapacity)
        {
            blockers.Add(new ValidationIssue
            {
                Code = ValidationReasonCode.InvalidAllocationMode,
                Message = "ConcurrentCapacity allocation mode is not yet supported",
                ResourceId = resource.Id,
            });
            return;
        }

        if (mode == AllocationModes.Exclusive)
        {
            if (request.AllocationPercent.HasValue)
                blockers.Add(new ValidationIssue
                {
                    Code = ValidationReasonCode.InvalidAllocationPercent,
                    Message = "allocation_percent must be null for Exclusive resources",
                    ResourceId = resource.Id,
                });

            foreach (var overlap in overlappingActive)
                blockers.Add(new ValidationIssue
                {
                    Code = ValidationReasonCode.AssignmentOverbooked,
                    Message = "Resource is already assigned during this time window",
                    ResourceId = resource.Id,
                    ConflictingAssignmentId = overlap.Id,
                });
            return;
        }

        if (mode == AllocationModes.Fractional)
        {
            if (!request.AllocationPercent.HasValue || request.AllocationPercent.Value <= 0)
            {
                blockers.Add(new ValidationIssue
                {
                    Code = ValidationReasonCode.InvalidAllocationPercent,
                    Message = "allocation_percent is required and must be > 0 for Fractional resources",
                    ResourceId = resource.Id,
                });
                return;
            }

            var total = committedFractionalTotal + request.AllocationPercent.Value;
            if (total > resource.BaseAvailabilityPercent)
                blockers.Add(new ValidationIssue
                {
                    Code = ValidationReasonCode.AssignmentOverbooked,
                    Message = $"Total allocation ({total}%) exceeds available capacity ({resource.BaseAvailabilityPercent}%)",
                    ResourceId = resource.Id,
                });
        }
    }

    private static bool Overlaps(ResourceAssignmentInfo a, ValidateResourceAssignmentRequest request)
        => a.StartUtc < request.EndUtc && a.EndUtc > request.StartUtc;

    private static bool NotExcluded(ResourceAssignmentInfo a, ValidateResourceAssignmentRequest request)
        => request.ExcludeAssignmentId is null || a.Id != request.ExcludeAssignmentId;

    private static void EvaluateBlockedPeriods(
        ValidateResourceAssignmentRequest request, Guid resourceId,
        IReadOnlyList<BlockedPeriod> blocked, List<ValidationIssue> warnings)
    {
        foreach (var period in blocked.Where(p => p.StartTs < request.EndUtc && p.EndTs > request.StartUtc))
        {
            var isHoliday = period.Source == BlockedPeriodSource.AvailabilityEvent
                            && period.EventType == AvailabilityEventType.PublicHoliday;
            warnings.Add(new ValidationIssue
            {
                Code = isHoliday ? ValidationReasonCode.NonWorkingHoliday : ValidationReasonCode.OffTimeOverlap,
                Message = isHoliday ? "Assignment overlaps a public holiday" : "Resource has off-time during this period",
                ResourceId = resourceId,
                ConflictingAvailabilityId = period.Id,
                Details = period.Title,
            });
        }
    }

    private static void EvaluateWeekends(
        ValidateResourceAssignmentRequest request, Guid resourceId,
        SchedulingSettingsInfo? settings, List<ValidationIssue> warnings)
    {
        // Only warn when weekends are non-working time at the site.
        if (settings is null || settings.WeekendsEnabled) return;

        var cursor = DateOnly.FromDateTime(request.StartUtc);
        var end = DateOnly.FromDateTime(request.EndUtc);
        for (; cursor <= end; cursor = cursor.AddDays(1))
        {
            if (SchedulingEngine.IsWeekend(cursor.ToDateTime(TimeOnly.MinValue)))
            {
                warnings.Add(new ValidationIssue
                {
                    Code = ValidationReasonCode.NonWorkingWeekend,
                    Message = "Assignment overlaps non-working weekend day(s)",
                    ResourceId = resourceId,
                });
                return; // one warning per assignment is enough
            }
        }
    }

    // ── Issue factories ──────────────────────────────────────────────────────

    private static ValidationIssue CapabilityMissing(Guid resourceId, RequestRequirementInfo req) => new()
    {
        Code = ValidationReasonCode.CapabilityMissing,
        Message = "Resource does not satisfy requirement",
        ResourceId = resourceId,
        CriterionId = req.CriterionId,
        Details = req.Criterion?.Name,
    };

    private static AssignmentValidationBatchItem Correlated(
        ValidateResourceAssignmentRequest request, ValidationResult result) => new()
        {
            RequestId = request.RequestId,
            ResourceId = request.ResourceId,
            Result = result,
        };

    private static ValidationIssue NotFound(Guid resourceId) => new()
    {
        Code = ValidationReasonCode.ResourceNotFound,
        Message = "Resource not found",
        ResourceId = resourceId,
    };

    private static ValidationIssue Inactive(Guid resourceId) => new()
    {
        Code = ValidationReasonCode.ResourceInactive,
        Message = "Resource is inactive",
        ResourceId = resourceId,
    };

    private static ValidationResult Build(List<ValidationIssue> blockers, List<ValidationIssue> warnings) => new()
    {
        Severity = blockers.Count > 0
            ? ValidationSeverity.Blocker
            : warnings.Count > 0
                ? ValidationSeverity.Warning
                : ValidationSeverity.Ok,
        Blockers = blockers,
        Warnings = warnings,
    };
}
