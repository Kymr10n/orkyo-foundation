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
        string? resourceTypeKey, DateTime from, DateTime to, string granularity, CancellationToken ct = default);
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
        var resource = await resourceRepository.GetByIdAsync(resourceId);
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
        return ComputeBuckets(resource, assignments, blockedPeriods, from, to, granularity);
    }

    public async Task<UtilizationResponse?> GetGroupUtilizationAsync(
        Guid groupId, DateTime from, DateTime to, string granularity, CancellationToken ct = default)
    {
        var membersResponse = await groupMemberRepository.GetMembersAsync(groupId);
        var activeMembers = membersResponse.Members.Where(m => m.IsActive).ToList();

        if (activeMembers.Count == 0)
            return new UtilizationResponse
            {
                From = from,
                To = to,
                Granularity = granularity,
                Buckets = BuildBucketShells(from, to, granularity)
                    .Select(b => new UtilizationBucket
                    {
                        Start = b.Start,
                        End = b.End,
                        AllocatedPercent = 0,
                        EffectiveAvailabilityPercent = 0,
                        IsExclusiveOccupied = false,
                    }).ToList(),
            };

        // Compute per-member then average
        var memberBuckets = new List<List<UtilizationBucket>>();
        foreach (var member in activeMembers)
        {
            var memberUtil = await GetResourceUtilizationAsync(member.Id, from, to, granularity);
            if (memberUtil is not null)
                memberBuckets.Add(memberUtil.Buckets);
        }

        var shells = BuildBucketShells(from, to, granularity);
        var averaged = shells.Select((shell, i) =>
        {
            var count = memberBuckets.Count;
            if (count == 0)
                return new UtilizationBucket
                {
                    Start = shell.Start,
                    End = shell.End,
                    AllocatedPercent = 0,
                    EffectiveAvailabilityPercent = 0,
                    IsExclusiveOccupied = false,
                };

            var allocPct = memberBuckets.Average(mb => i < mb.Count ? (double)mb[i].AllocatedPercent : 0);
            var availPct = memberBuckets.Average(mb => i < mb.Count ? (double)mb[i].EffectiveAvailabilityPercent : 0);
            var occupied = memberBuckets.Any(mb => i < mb.Count && mb[i].IsExclusiveOccupied);
            return new UtilizationBucket
            {
                Start = shell.Start,
                End = shell.End,
                AllocatedPercent = (decimal)allocPct,
                EffectiveAvailabilityPercent = (decimal)availPct,
                IsExclusiveOccupied = occupied,
            };
        }).ToList();

        return new UtilizationResponse { From = from, To = to, Granularity = granularity, Buckets = averaged };
    }

    public async Task<UtilizationResponse> GetTenantUtilizationAsync(
        string? resourceTypeKey, DateTime from, DateTime to, string granularity, CancellationToken ct = default)
    {
        var filter = new ResourceListFilter { IsActive = true, ResourceTypeKey = resourceTypeKey };
        var resources = await resourceRepository.GetAllAsync(filter);

        if (resources.Count == 0)
            return new UtilizationResponse
            {
                From = from,
                To = to,
                Granularity = granularity,
                Buckets = BuildBucketShells(from, to, granularity)
                    .Select(b => new UtilizationBucket
                    {
                        Start = b.Start,
                        End = b.End,
                        AllocatedPercent = 0,
                        EffectiveAvailabilityPercent = 0,
                        IsExclusiveOccupied = false,
                    }).ToList(),
            };

        var allBuckets = new List<List<UtilizationBucket>>();
        foreach (var resource in resources)
            allBuckets.Add(await ComputeResourceBucketsAsync(resource, from, to, granularity, ct));

        var shells = BuildBucketShells(from, to, granularity);
        var averaged = shells.Select((shell, i) =>
        {
            var count = allBuckets.Count;
            if (count == 0)
                return new UtilizationBucket
                {
                    Start = shell.Start,
                    End = shell.End,
                    AllocatedPercent = 0,
                    EffectiveAvailabilityPercent = 0,
                    IsExclusiveOccupied = false,
                };
            return new UtilizationBucket
            {
                Start = shell.Start,
                End = shell.End,
                AllocatedPercent = (decimal)allBuckets.Average(b => i < b.Count ? (double)b[i].AllocatedPercent : 0),
                EffectiveAvailabilityPercent = (decimal)allBuckets.Average(b => i < b.Count ? (double)b[i].EffectiveAvailabilityPercent : 0),
                IsExclusiveOccupied = allBuckets.Any(b => i < b.Count && b[i].IsExclusiveOccupied),
            };
        }).ToList();

        return new UtilizationResponse { From = from, To = to, Granularity = granularity, Buckets = averaged };
    }

    public async Task<List<ResourceUtilizationResponse>> GetUtilizationByResourceAsync(
        string? resourceTypeKey, DateTime from, DateTime to, string granularity, CancellationToken ct = default)
    {
        var filter = new ResourceListFilter { IsActive = true, ResourceTypeKey = resourceTypeKey };
        var resources = await resourceRepository.GetAllAsync(filter);

        var result = new List<ResourceUtilizationResponse>(resources.Count);
        foreach (var resource in resources)
            result.Add(new ResourceUtilizationResponse
            {
                ResourceId = resource.Id,
                Buckets = await ComputeResourceBucketsAsync(resource, from, to, granularity, ct),
            });

        return result;
    }

    // ── Core computation ────────────────────────────────────────────────────

    private static List<UtilizationBucket> ComputeBuckets(
        ResourceInfo resource,
        List<ResourceAssignmentInfo> assignments,
        List<BlockedPeriod> blockedPeriods,
        DateTime from, DateTime to, string granularity)
    {
        var shells = BuildBucketShells(from, to, granularity);
        return shells.Select(shell => ComputeBucket(resource, assignments, blockedPeriods, shell.Start, shell.End)).ToList();
    }

    private static UtilizationBucket ComputeBucket(
        ResourceInfo resource,
        List<ResourceAssignmentInfo> assignments,
        List<BlockedPeriod> blockedPeriods,
        DateTime bucketStart, DateTime bucketEnd)
    {
        var bucketSpan = (bucketEnd - bucketStart).TotalMinutes;

        var isBlocked = blockedPeriods.Any(p => p.StartTs < bucketEnd && p.EndTs > bucketStart);
        var effectiveAvailability = isBlocked ? 0m : resource.BaseAvailabilityPercent;

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
            var overlapMinutes = (overlapEnd - overlapStart).TotalMinutes;
            var weight = bucketSpan > 0 ? (decimal)(overlapMinutes / bucketSpan) : 1m;
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
                _ => current.AddDays(1), // day
            };
            if (next > to) next = to;
            buckets.Add((current, next));
            current = next;
        }

        return buckets;
    }
}
