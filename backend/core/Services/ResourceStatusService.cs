using Api.Models;
using Api.Repositories;

namespace Api.Services;

/// <summary>
/// Composes one resource's current state from the services that already own each part:
/// bookings, absences, conflicts and utilization. Adds no rule of its own.
/// </summary>
public interface IResourceStatusService
{
    /// <summary>The status of a resource, or null when it does not exist.</summary>
    Task<ResourceStatusInfo?> GetAsync(Guid resourceId, CancellationToken ct = default);
}

public class ResourceStatusService(
    IResourceRepository resourceRepository,
    IResourceAssignmentService assignmentService,
    IResourceAbsenceRepository absenceRepository,
    IRequestRepository requestRepository,
    IConflictService conflictService,
    IUtilizationService utilizationService,
    TimeProvider time) : IResourceStatusService
{
    public const int LookAheadDays = 30;
    public const int UtilizationDays = 30;

    public async Task<ResourceStatusInfo?> GetAsync(Guid resourceId, CancellationToken ct = default)
    {
        var resource = await resourceRepository.GetByIdAsync(resourceId, ct);
        if (resource is null) return null;

        var now = time.GetUtcNow().UtcDateTime;
        var horizon = now.AddDays(LookAheadDays);

        // Overlap query: a booking in progress (start <= now < end) is inside this window too.
        var assignments = await assignmentService.GetByResourceAsync(resourceId, now, horizon, ct);
        var current = assignments.FirstOrDefault(a => a.StartUtc <= now && a.EndUtc > now);
        var next = assignments.Where(a => a.StartUtc > now).MinBy(a => a.StartUtc);

        var names = await RequestNamesAsync([current, next], ct);

        // Same semantics as the scheduler (AvailabilityResolver): enabled, start–end as stored.
        var absence = (await absenceRepository.GetByResourceAsync(resourceId, ct))
            .FirstOrDefault(a => a.Enabled && a.StartTs <= now && a.EndTs > now);

        var utilization = await utilizationService.GetResourceUtilizationAsync(
            resourceId, now.AddDays(-UtilizationDays), now, "day", ct);

        return new ResourceStatusInfo
        {
            ResourceId = resource.Id,
            Name = resource.Name,
            ResourceTypeKey = resource.ResourceTypeKey,
            IsActive = resource.IsActive,
            AsOfUtc = now,
            Current = ToBooking(current, names),
            Next = ToBooking(next, names),
            ActiveAbsence = absence is null ? null : new ResourceStatusAbsence
            {
                Id = absence.Id,
                Title = absence.Title,
                AbsenceType = absence.AbsenceType,
                EndTs = absence.EndTs,
            },
            ConflictCount = await conflictService.CountResourceConflictsAsync(assignments, ct),
            LookAheadDays = LookAheadDays,
            UtilizationPercent = utilization is { Buckets.Count: > 0 }
                ? Math.Round(utilization.Buckets.Average(b => b.AllocatedPercent), 1)
                : null,
            UtilizationDays = UtilizationDays,
        };
    }

    private async Task<Dictionary<Guid, string>> RequestNamesAsync(
        IEnumerable<ResourceAssignmentInfo?> bookings, CancellationToken ct)
    {
        var ids = bookings.OfType<ResourceAssignmentInfo>().Select(a => a.RequestId).Distinct().ToList();
        if (ids.Count == 0) return [];
        return (await requestRepository.GetByIdsAsync(ids, includeRequirements: false, ct))
            .ToDictionary(r => r.Id, r => r.Name);
    }

    private static ResourceStatusBooking? ToBooking(ResourceAssignmentInfo? a, Dictionary<Guid, string> names)
        => a is null ? null : new ResourceStatusBooking
        {
            AssignmentId = a.Id,
            RequestId = a.RequestId,
            RequestName = names.GetValueOrDefault(a.RequestId, ""),
            StartUtc = a.StartUtc,
            EndUtc = a.EndUtc,
        };
}
