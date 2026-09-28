using Api.Constants;
using Api.Models;
using Api.Repositories;

namespace Api.Services;

public interface IUtilizationService
{
    Task<UtilizationResponse?> GetResourceUtilizationAsync(
        Guid resourceId, DateTime from, DateTime to, string granularity, CancellationToken ct = default);

    Task<UtilizationResponse?> GetGroupUtilizationAsync(
        Guid groupId, DateTime from, DateTime to, string granularity, CancellationToken ct = default);

    Task<UtilizationResponse> GetTenantUtilizationAsync(
        string? resourceTypeKey, DateTime from, DateTime to, string granularity, CancellationToken ct = default);

    Task<List<ResourceUtilizationResponse>> GetUtilizationByResourceAsync(
        string? resourceTypeKey, DateTime from, DateTime to, string granularity, Guid? siteId = null, CancellationToken ct = default);
}

public class UtilizationService(
    IResourceRepository resourceRepository,
    IResourceAssignmentRepository assignmentRepository,
    IResourceGroupMemberRepository groupMemberRepository,
    IAvailabilityResolver resolver) : IUtilizationService
{
    public async Task<UtilizationResponse?> GetResourceUtilizationAsync(
        Guid resourceId, DateTime from, DateTime to, string granularity, CancellationToken ct = default)
    {
        var resource = await resourceRepository.GetByIdAsync(resourceId, ct);
        if (resource is null) return null;

        var buckets = await ComputeResourceBucketsAsync(resource, from, to, granularity, ct);
        return new UtilizationResponse
        {
            From = from,
            To = to,
            Granularity = granularity,
            Buckets = buckets,
        };
    }

    /// <summary>
    /// Compute one resource's buckets given the resource itself (no extra
    /// lookup). Shared by the single-resource, tenant-aggregate and bulk
    /// by-resource paths so the per-bucket math lives in exactly one place.
    /// </summary>
    private async Task<List<UtilizationBucket>> ComputeResourceBucketsAsync(
        ResourceInfo resource, DateTime from, DateTime to, string granularity, CancellationToken ct)
    {
        var assignments = await assignmentRepository.GetByResourceAsync(resource.Id, from, to, ct);
        var blockedPeriods = await resolver.GetBlockedPeriodsAsync(resource.Id, ct);
        var settings = (await resolver.GetSchedulingSettingsForResourcesAsync([resource.Id], ct))
            .GetValueOrDefault(resource.Id);
        return ComputeBuckets(resource, assignments, blockedPeriods, settings, from, to, granularity);
    }

    /// <summary>
    /// Bulk equivalent of <see cref="ComputeResourceBucketsAsync"/> for many resources: preloads all
    /// assignments and blocked periods in two queries (was two DB round-trips <em>per resource</em>),
    /// then runs the identical per-resource bucket math. Returns buckets keyed by resource id.
    /// </summary>
    private async Task<Dictionary<Guid, List<UtilizationBucket>>> ComputeBucketsForResourcesAsync(
        IReadOnlyList<ResourceInfo> resources, DateTime from, DateTime to, string granularity, CancellationToken ct)
    {
        if (resources.Count == 0) return [];

        var resourceIds = resources.Select(r => r.Id).ToList();
        var assignmentsByResource = (await assignmentRepository.GetActiveByResourcesAsync(resourceIds, from, to, ct))
            .GroupBy(a => a.ResourceId)
            .ToDictionary(g => g.Key, g => g.ToList());
        var blockedByResource = await resolver.GetBlockedPeriodsForResourcesAsync(resourceIds, ct);
        var settingsByResource = await resolver.GetSchedulingSettingsForResourcesAsync(resourceIds, ct);

        return resources.ToDictionary(
            r => r.Id,
            r => ComputeBuckets(
                r,
                assignmentsByResource.GetValueOrDefault(r.Id, []),
                blockedByResource.GetValueOrDefault(r.Id, []),
                settingsByResource.GetValueOrDefault(r.Id),
                from, to, granularity));
    }

    public async Task<UtilizationResponse?> GetGroupUtilizationAsync(
        Guid groupId, DateTime from, DateTime to, string granularity, CancellationToken ct = default)
    {
        var membersResponse = await groupMemberRepository.GetMembersAsync(groupId, ct);
        var activeMembers = membersResponse.Members.Where(m => m.IsActive).ToList();

        if (activeMembers.Count == 0)
            return Averaged([], from, to, granularity);

        // Compute per-member then average. Bulk-load the member resources + their assignments/blocked
        // periods (was N+1: a full per-resource compute — two DB round-trips — per member).
        var memberResources = await resourceRepository.GetByIdsAsync(activeMembers.Select(m => m.Id).ToList(), ct);
        var memberBucketsById = await ComputeBucketsForResourcesAsync(memberResources, from, to, granularity, ct);
        return Averaged(memberResources.Select(r => memberBucketsById[r.Id]).ToList(), from, to, granularity);
    }

    public async Task<UtilizationResponse> GetTenantUtilizationAsync(
        string? resourceTypeKey, DateTime from, DateTime to, string granularity, CancellationToken ct = default)
    {
        var filter = new ResourceListFilter { IsActive = true, ResourceTypeKey = resourceTypeKey };
        // Every resource: a utilization figure over the first 1000 of 1200 is a wrong number,
        // not a short list, and the response carries nothing that would say it was cut.
        var resources = await resourceRepository.GetEveryAsync(filter, ct);

        var bucketsById = await ComputeBucketsForResourcesAsync(resources, from, to, granularity, ct);
        return Averaged(resources.Select(r => bucketsById[r.Id]).ToList(), from, to, granularity);
    }

    /// <summary>
    /// Averages per-resource buckets slot by slot (a resource with fewer buckets counts 0 for the
    /// missing slots); a slot is exclusively occupied if any resource's is. No resources: every slot 0.
    /// </summary>
    private static UtilizationResponse Averaged(
        IReadOnlyList<List<UtilizationBucket>> perResource, DateTime from, DateTime to, string granularity)
    {
        decimal Average(Func<List<UtilizationBucket>, double> slotValue) =>
            perResource.Count == 0 ? 0 : (decimal)perResource.Average(slotValue);

        return new UtilizationResponse
        {
            From = from,
            To = to,
            Granularity = granularity,
            Buckets = BuildBucketShells(from, to, granularity).Select((shell, i) => new UtilizationBucket
            {
                Start = shell.Start,
                End = shell.End,
                AllocatedPercent = Average(b => i < b.Count ? (double)b[i].AllocatedPercent : 0),
                EffectiveAvailabilityPercent = Average(b => i < b.Count ? (double)b[i].EffectiveAvailabilityPercent : 0),
                IsExclusiveOccupied = perResource.Any(b => i < b.Count && b[i].IsExclusiveOccupied),
            }).ToList(),
        };
    }

    public async Task<List<ResourceUtilizationResponse>> GetUtilizationByResourceAsync(
        string? resourceTypeKey, DateTime from, DateTime to, string granularity, Guid? siteId = null, CancellationToken ct = default)
    {
        // siteId filters which resources are included (rows); buckets stay whole-person —
        // ComputeResourceBucketsAsync still uses each resource's full assignment set across all sites.
        var filter = new ResourceListFilter
        {
            IsActive = true,
            ResourceTypeKey = resourceTypeKey,
            SiteId = siteId,
            SiteWindowFrom = siteId.HasValue ? from : null,
            SiteWindowTo = siteId.HasValue ? to : null,
        };
        var resources = await resourceRepository.GetEveryAsync(filter, ct);

        var bucketsById = await ComputeBucketsForResourcesAsync(resources, from, to, granularity, ct);
        return resources.Select(resource => new ResourceUtilizationResponse
        {
            ResourceId = resource.Id,
            Buckets = bucketsById[resource.Id],
        }).ToList();
    }

    // ── Core computation ────────────────────────────────────────────────────

    private static List<UtilizationBucket> ComputeBuckets(
        ResourceInfo resource,
        List<ResourceAssignmentInfo> assignments,
        List<BlockedPeriod> blockedPeriods,
        SchedulingSettingsInfo? settings,
        DateTime from, DateTime to, string granularity)
    {
        var shells = BuildBucketShells(from, to, granularity);
        return shells.Select(shell => ComputeBucket(resource, assignments, blockedPeriods, settings, shell.Start, shell.End)).ToList();
    }

    private static UtilizationBucket ComputeBucket(
        ResourceInfo resource,
        List<ResourceAssignmentInfo> assignments,
        List<BlockedPeriod> blockedPeriods,
        SchedulingSettingsInfo? settings,
        DateTime bucketStart, DateTime bucketEnd)
    {
        // Working minutes, not wall-clock: the fractional weight below is "share of the
        // bookable time in this bucket", and a bucket that is mostly night or weekend has
        // far less bookable time than its span. Null/24-7 settings return the raw span.
        var bucketSpan = SchedulingEngine.WorkingMinutesInWindow(bucketStart, bucketEnd, settings);

        // Blocked time is SUBTRACTED, not treated as an on/off switch, as WorkingMinutesInWindow's
        // contract asks of its callers.
        //
        // The previous `blockedPeriods.Any(overlap)` was indistinguishable from this at day
        // granularity, where a blocked day covers its whole bucket. At week and month
        // granularity it was not: one public holiday, one maintenance day or one day of
        // somebody's leave zeroed an entire week, while AllocatedPercent below was still
        // computed normally — so a bucket reported "Off" while carrying real booked time.
        var blockedMinutes = SchedulingEngine.BlockedWorkingMinutes(blockedPeriods, settings, bucketStart, bucketEnd);
        var openMinutes = Math.Max(0d, bucketSpan - blockedMinutes);
        // No bookable minutes at all (a weekend or overnight bucket, once working hours are on)
        // still reads zero: deriveBucketStatus maps a zero here to "non-working", and the grid's
        // averages skip those buckets.
        var effectiveAvailability = bucketSpan <= 0
            ? 0m
            : Math.Round(resource.BaseAvailabilityPercent * (decimal)(openMinutes / bucketSpan), 2);

        // Overlapping active assignments within this bucket
        var overlapping = assignments.Where(a =>
            a.AssignmentStatus != AssignmentStatuses.Cancelled &&
            a.StartUtc < bucketEnd && a.EndUtc > bucketStart).ToList();

        if (resource.AllocationMode == AllocationModes.Exclusive)
        {
            var occupied = overlapping.Count > 0;
            return new UtilizationBucket
            {
                Start = bucketStart,
                End = bucketEnd,
                AllocatedPercent = occupied ? 100m : 0m,
                EffectiveAvailabilityPercent = effectiveAvailability,
                IsExclusiveOccupied = occupied,
            };
        }

        // Fractional: time-weighted sum of overlapping allocation percentages.
        // Treat a null allocation_percent as 100 — a fully-allocated assignment
        // that was created without an explicit percent should not silently vanish.
        var totalAllocated = 0m;
        foreach (var a in overlapping)
        {
            var allocPct = a.AllocationPercent ?? 100m;
            var overlapStart = a.StartUtc > bucketStart ? a.StartUtc : bucketStart;
            var overlapEnd = a.EndUtc < bucketEnd ? a.EndUtc : bucketEnd;
            // Masked on both sides of the ratio: a window that waits through a night must
            // not count those minutes as booked when they were never bookable.
            var overlapMinutes = SchedulingEngine.WorkingMinutesInWindow(overlapStart, overlapEnd, settings);
            // A bucket with no bookable time at all (a weekend day) allocates nothing rather
            // than defaulting the weight to 1 — that default exists for zero-length shells.
            var weight = bucketSpan > 0 ? (decimal)(overlapMinutes / bucketSpan) : 0m;
            totalAllocated += allocPct * weight;
        }

        return new UtilizationBucket
        {
            Start = bucketStart,
            End = bucketEnd,
            AllocatedPercent = Math.Round(totalAllocated, 2),
            EffectiveAvailabilityPercent = effectiveAvailability,
            IsExclusiveOccupied = false,
        };
    }

    private static List<(DateTime Start, DateTime End)> BuildBucketShells(
        DateTime from, DateTime to, string granularity)
    {
        var buckets = new List<(DateTime, DateTime)>();
        var current = from;

        while (current < to)
        {
            var next = granularity.ToLowerInvariant() switch
            {
                "minute" => current.AddMinutes(15),
                "hour" => current.AddHours(1),
                "week" => current.AddDays(7),
                "month" => current.AddMonths(1),
                "quarter" => current.AddMonths(3),
                "year" => current.AddYears(1),
                _ => current.AddDays(1), // day
            };
            if (next > to) next = to;
            buckets.Add((current, next));
            current = next;
        }

        return buckets;
    }
}
