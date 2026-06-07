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
/// allocation mode rules, capacity). Each check is a private method that appends
/// to the shared blockers/warnings lists so the orchestrator stays a one-screen read.
/// </summary>
public class ResourceAssignmentValidator(
    IResourceRepository resourceRepository,
    IResourceAssignmentRepository assignmentRepository,
    ICapabilityMatcher capabilityMatcher,
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
        await CheckSiteScopedWindowAsync(request, resource, warnings);
        await CheckAllocationAsync(request, resource, blockers);

        return Build(blockers, warnings);
    }

    public async Task<List<AssignmentValidationBatchItem>> ValidateBatchAsync(
        IReadOnlyList<ValidateResourceAssignmentRequest> requests, CancellationToken ct = default)
    {
        var results = new List<AssignmentValidationBatchItem>(requests.Count);
        foreach (var request in requests)
        {
            var result = await ValidateAsync(request, ct);
            results.Add(new AssignmentValidationBatchItem
            {
                RequestId = request.RequestId,
                ResourceId = request.ResourceId,
                Result = result
            });
        }
        return results;
    }

    /// <summary>
    /// Site-scoped non-working time checks: off-times (warnings), holidays (warnings),
    /// and weekends (warnings, if the site disables weekend work).
    /// All three look up `scheduling_repository.GetSiteIdForResourceAsync` so we group them
    /// into a single section that early-exits if the resource has no site.
    /// </summary>
    private async Task CheckSiteScopedWindowAsync(
        ValidateResourceAssignmentRequest request, ResourceInfo resource, List<ValidationIssue> warnings, CancellationToken ct = default)
    {
        var siteId = await schedulingRepository.GetSiteIdForResourceAsync(resource.Id);
        if (siteId is null) return;

        await CheckOffTimesAndHolidaysAsync(request, resource, siteId.Value, warnings, ct);
        await CheckWeekendsAsync(request, resource, siteId.Value, warnings);
    }

    private async Task CheckCapabilitiesAsync(
        ValidateResourceAssignmentRequest request, ResourceInfo resource, List<ValidationIssue> blockers, CancellationToken ct = default)
    {
        // Dry-run path: a not-yet-created request has no requirements to check.
        // We still run the rest of the validator so the caller gets off-time /
        // overbook / weekend feedback before saving.
        if (request.RequestId is null) return;

        var requestInfo = await requestRepository.GetByIdAsync(request.RequestId.Value, includeRequirements: true);
        var requirements = requestInfo?.Requirements ?? [];

        foreach (var req in requirements)
        {
            var satisfied = await capabilityMatcher.ResourceSatisfiesRequirementAsync(resource.Id, req);
            if (!satisfied)
            {
                blockers.Add(new ValidationIssue
                {
                    Code = ValidationReasonCode.CapabilityMissing,
                    Message = "Resource does not satisfy requirement",
                    ResourceId = resource.Id,
                    CriterionId = req.CriterionId,
                    Details = req.Criterion?.Name
                });
            }
        }
    }

    private async Task CheckOffTimesAndHolidaysAsync(
        ValidateResourceAssignmentRequest request,
        ResourceInfo resource,
        Guid siteId,
        List<ValidationIssue> warnings, CancellationToken ct = default)
    {
        var allBlocked = await availabilityResolver.GetBlockedPeriodsAsync(resource.Id, ct);
        var blocking = allBlocked.Where(p => p.StartTs < request.EndUtc && p.EndTs > request.StartUtc);

        foreach (var period in blocking)
        {
            var isHoliday = period.Source == BlockedPeriodSource.AvailabilityEvent
                            && period.EventType == AvailabilityEventType.PublicHoliday;
            warnings.Add(new ValidationIssue
            {
                Code = isHoliday ? ValidationReasonCode.NonWorkingHoliday : ValidationReasonCode.OffTimeOverlap,
                Message = isHoliday ? "Assignment overlaps a public holiday" : "Resource has off-time during this period",
                ResourceId = resource.Id,
                ConflictingAvailabilityId = period.Id,
                Details = period.Title
            });
        }
    }

    private async Task CheckWeekendsAsync(
        ValidateResourceAssignmentRequest request,
        ResourceInfo resource,
        Guid siteId,
        List<ValidationIssue> warnings, CancellationToken ct = default)
    {
        var settings = await schedulingRepository.GetSettingsAsync(siteId);
        // If weekend work is enabled at the site, a Sat/Sun assignment is unremarkable.
        // We only warn when weekends are considered non-working time.
        if (settings is null || settings.WeekendsEnabled) return;

        // Walk the assignment range day-by-day and warn once if any day falls on Sat/Sun.
        // Iterating dates (not hours) keeps the loop bounded to the assignment length.
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
                    ResourceId = resource.Id,
                });
                return; // one warning per assignment is enough; details are date-range visible elsewhere
            }
        }
    }

    private async Task CheckAllocationAsync(
        ValidateResourceAssignmentRequest request, ResourceInfo resource, List<ValidationIssue> blockers, CancellationToken ct = default)
    {
        var mode = request.AllocationMode ?? resource.AllocationMode;

        if (mode == AllocationModes.ConcurrentCapacity)
        {
            blockers.Add(new ValidationIssue
            {
                Code = ValidationReasonCode.InvalidAllocationMode,
                Message = "ConcurrentCapacity allocation mode is not yet supported",
                ResourceId = resource.Id
            });
            return;
        }

        if (mode == AllocationModes.Exclusive)
        {
            if (request.AllocationPercent.HasValue)
            {
                blockers.Add(new ValidationIssue
                {
                    Code = ValidationReasonCode.InvalidAllocationPercent,
                    Message = "allocation_percent must be null for Exclusive resources",
                    ResourceId = resource.Id
                });
            }

            var overlapping = await assignmentRepository.GetOverlappingActiveAsync(
                resource.Id, request.StartUtc, request.EndUtc, request.ExcludeAssignmentId);
            foreach (var overlap in overlapping)
            {
                blockers.Add(new ValidationIssue
                {
                    Code = ValidationReasonCode.AssignmentOverbooked,
                    Message = "Resource is already assigned during this time window",
                    ResourceId = resource.Id,
                    ConflictingAssignmentId = overlap.Id
                });
            }
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
                    ResourceId = resource.Id
                });
                return;
            }

            var existing = await assignmentRepository.GetTotalAllocatedPercentAsync(
                resource.Id, request.StartUtc, request.EndUtc);
            var total = existing + request.AllocationPercent.Value;
            if (total > resource.BaseAvailabilityPercent)
            {
                blockers.Add(new ValidationIssue
                {
                    Code = ValidationReasonCode.AssignmentOverbooked,
                    Message = $"Total allocation ({total}%) exceeds available capacity ({resource.BaseAvailabilityPercent}%)",
                    ResourceId = resource.Id
                });
            }
        }
    }

    // ── Issue factories ──────────────────────────────────────────────────────

    private static ValidationIssue NotFound(Guid resourceId) => new()
    {
        Code = ValidationReasonCode.ResourceNotFound,
        Message = "Resource not found",
        ResourceId = resourceId
    };

    private static ValidationIssue Inactive(Guid resourceId) => new()
    {
        Code = ValidationReasonCode.ResourceInactive,
        Message = "Resource is inactive",
        ResourceId = resourceId
    };

    private static ValidationResult Build(List<ValidationIssue> blockers, List<ValidationIssue> warnings) => new()
    {
        Severity = blockers.Count > 0
            ? ValidationSeverity.Blocker
            : warnings.Count > 0
                ? ValidationSeverity.Warning
                : ValidationSeverity.Ok,
        Blockers = blockers,
        Warnings = warnings
    };
}
