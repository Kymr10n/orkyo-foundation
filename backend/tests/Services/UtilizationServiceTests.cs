using Api.Constants;
using Api.Models;
using Api.Repositories;
using Api.Services;
using Moq;
using Xunit;

namespace Orkyo.Foundation.Tests.Services;

public class UtilizationServiceTests
{
    private static readonly Guid ResourceId = Guid.NewGuid();
    private static readonly Guid GroupId = Guid.NewGuid();

    private static ResourceInfo MakeResource(string mode, int availPct = 100) => new()
    {
        Id = ResourceId,
        ResourceTypeId = Guid.NewGuid(),
        ResourceTypeKey = "tool",
        Name = "Test Resource",
        AllocationMode = mode,
        BaseAvailabilityPercent = availPct,
        IsActive = true,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
    };

    private static ResourceAssignmentInfo MakeAssignment(
        Guid resourceId, DateTime start, DateTime end, decimal? pct = null) => new()
        {
            Id = Guid.NewGuid(),
            RequestId = Guid.NewGuid(),
            ResourceId = resourceId,
            ResourceTypeKey = ResourceTypeKeys.Space,
            StartUtc = start,
            EndUtc = end,
            AllocationPercent = pct,
            AssignmentStatus = AssignmentStatuses.Planned,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

    private static BlockedPeriod MakeBlockedPeriod(DateTime start, DateTime end) => new()
    {
        Id = Guid.NewGuid(),
        Title = "Off",
        StartTs = start,
        EndTs = end,
        Source = BlockedPeriodSource.ResourceAbsence,
        AbsenceType = AbsenceType.Custom
    };

    private static IUtilizationService BuildService(
        ResourceInfo resource,
        List<ResourceAssignmentInfo>? assignments = null,
        List<BlockedPeriod>? blockedPeriods = null,
        ResourceGroupMembersResponse? groupMembers = null)
    {
        var resourceRepo = new Mock<IResourceRepository>();
        resourceRepo.Setup(r => r.GetByIdAsync(ResourceId)).ReturnsAsync(resource);
        resourceRepo.Setup(r => r.GetAllAsync(It.IsAny<ResourceListFilter>()))
            .ReturnsAsync([resource]);

        var assignmentRepo = new Mock<IResourceAssignmentRepository>();
        assignmentRepo.Setup(r => r.GetByResourceAsync(ResourceId, It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(assignments ?? []);

        var groupRepo = new Mock<IResourceGroupMemberRepository>();
        groupRepo.Setup(r => r.GetMembersAsync(GroupId))
            .ReturnsAsync(groupMembers ?? new ResourceGroupMembersResponse { GroupId = GroupId, Members = [resource] });

        var resolver = new Mock<IAvailabilityResolver>();
        resolver.Setup(r => r.GetBlockedPeriodsAsync(ResourceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(blockedPeriods ?? []);

        return new UtilizationService(resourceRepo.Object, assignmentRepo.Object, groupRepo.Object, resolver.Object);
    }

    // ── Exclusive resource tests ───────────────────────────────────────────

    [Fact]
    public async Task Exclusive_NoAssignment_AllBucketsUnoccupied()
    {
        var resource = MakeResource(AllocationModes.Exclusive);
        var service = BuildService(resource);
        var from = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = from.AddDays(3);

        var result = await service.GetResourceUtilizationAsync(ResourceId, from, to, "day");

        Assert.NotNull(result);
        Assert.Equal(3, result.Buckets.Count);
        Assert.All(result.Buckets, b =>
        {
            Assert.False(b.IsExclusiveOccupied);
            Assert.Equal(0m, b.AllocatedPercent);
            Assert.Equal(100m, b.EffectiveAvailabilityPercent);
        });
    }

    [Fact]
    public async Task Exclusive_AssignmentCoversDay_BucketOccupied()
    {
        var resource = MakeResource(AllocationModes.Exclusive);
        var from = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = from.AddDays(3);
        var assignments = new List<ResourceAssignmentInfo>
        {
            MakeAssignment(ResourceId, from.AddDays(1), from.AddDays(2)),
        };
        var service = BuildService(resource, assignments);

        var result = await service.GetResourceUtilizationAsync(ResourceId, from, to, "day");

        Assert.False(result!.Buckets[0].IsExclusiveOccupied); // day 1: free
        Assert.True(result.Buckets[1].IsExclusiveOccupied);   // day 2: occupied
        Assert.False(result.Buckets[2].IsExclusiveOccupied);  // day 3: free
    }

    // ── Fractional resource tests ─────────────────────────────────────────

    [Fact]
    public async Task Fractional_50PctAssignment_Bucket50Pct()
    {
        var resource = MakeResource(AllocationModes.Fractional);
        var from = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = from.AddDays(1);
        var assignments = new List<ResourceAssignmentInfo>
        {
            MakeAssignment(ResourceId, from, to, pct: 50m),
        };
        var service = BuildService(resource, assignments);

        var result = await service.GetResourceUtilizationAsync(ResourceId, from, to, "day");

        Assert.Single(result!.Buckets);
        Assert.Equal(50m, result.Buckets[0].AllocatedPercent);
        Assert.False(result.Buckets[0].IsExclusiveOccupied);
    }

    [Fact]
    public async Task Fractional_PartialOverlap_TimeWeighted()
    {
        var resource = MakeResource(AllocationModes.Fractional);
        var from = new DateTime(2026, 7, 10, 0, 0, 0, DateTimeKind.Utc);
        var to = from.AddDays(1); // one 24h bucket
        // Assignment covers only the second half of the day
        var assignments = new List<ResourceAssignmentInfo>
        {
            MakeAssignment(ResourceId, from.AddHours(12), to, pct: 100m),
        };
        var service = BuildService(resource, assignments);

        var result = await service.GetResourceUtilizationAsync(ResourceId, from, to, "day");

        Assert.Single(result!.Buckets);
        // 100% * 0.5 overlap = 50%
        Assert.Equal(50m, result.Buckets[0].AllocatedPercent);
    }

    [Fact]
    public async Task Fractional_NullAllocationPercent_TreatedAsFullAllocation()
    {
        // An assignment created without an explicit allocationPercent (e.g. via
        // auto-schedule or a legacy code path) must not silently vanish from the
        // utilization calculation. Null is treated as 100%.
        var resource = MakeResource(AllocationModes.Fractional);
        var from = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = from.AddDays(1);
        var assignments = new List<ResourceAssignmentInfo>
        {
            MakeAssignment(ResourceId, from, to, pct: null), // no explicit percent
        };
        var service = BuildService(resource, assignments);

        var result = await service.GetResourceUtilizationAsync(ResourceId, from, to, "day");

        Assert.Single(result!.Buckets);
        Assert.Equal(100m, result.Buckets[0].AllocatedPercent);
    }

    [Theory]
    [InlineData("hour", 24, 60)]
    [InlineData("minute", 96, 15)]
    public async Task ResourceUtilization_FineGranularity_BuildsExpectedBuckets(
        string granularity,
        int expectedCount,
        int expectedMinutes)
    {
        var resource = MakeResource(AllocationModes.Fractional);
        var service = BuildService(resource);
        var from = new DateTime(2026, 7, 10, 0, 0, 0, DateTimeKind.Utc);
        var to = from.AddDays(1);

        var result = await service.GetResourceUtilizationAsync(ResourceId, from, to, granularity);

        Assert.NotNull(result);
        Assert.Equal(expectedCount, result.Buckets.Count);
        Assert.Equal(from, result.Buckets[0].Start);
        Assert.Equal(from.AddMinutes(expectedMinutes), result.Buckets[0].End);
        Assert.Equal(to, result.Buckets[^1].End);
    }

    // ── Off-time tests ────────────────────────────────────────────────────

    [Fact]
    public async Task OffTime_BlocksBucket_EffectiveAvailabilityZero()
    {
        var resource = MakeResource(AllocationModes.Fractional);
        var from = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = from.AddDays(2);
        var blocked = new List<BlockedPeriod> { MakeBlockedPeriod(from, from.AddDays(1)) };

        var resourceRepo = new Mock<IResourceRepository>();
        resourceRepo.Setup(r => r.GetByIdAsync(ResourceId)).ReturnsAsync(resource);
        var assignmentRepo = new Mock<IResourceAssignmentRepository>();
        assignmentRepo.Setup(r => r.GetByResourceAsync(ResourceId, It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync([]);
        var groupRepo = new Mock<IResourceGroupMemberRepository>();
        var resolver = new Mock<IAvailabilityResolver>();
        resolver.Setup(r => r.GetBlockedPeriodsAsync(ResourceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(blocked);

        var service = new UtilizationService(resourceRepo.Object, assignmentRepo.Object, groupRepo.Object, resolver.Object);
        var result = await service.GetResourceUtilizationAsync(ResourceId, from, to, "day");

        Assert.Equal(0m, result!.Buckets[0].EffectiveAvailabilityPercent); // off-time day
        Assert.Equal(100m, result.Buckets[1].EffectiveAvailabilityPercent); // normal day
    }

    // ── Group utilization tests ──────────────────────────────────────────

    [Fact]
    public async Task Group_NoMembers_EmptyBuckets()
    {
        var resource = MakeResource(AllocationModes.Fractional);
        var emptyGroup = new ResourceGroupMembersResponse { GroupId = GroupId, Members = [] };
        var service = BuildService(resource, groupMembers: emptyGroup);
        var from = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = from.AddDays(2);

        var result = await service.GetGroupUtilizationAsync(GroupId, from, to, "day");

        Assert.NotNull(result);
        Assert.All(result.Buckets, b => Assert.Equal(0m, b.AllocatedPercent));
    }

    [Fact]
    public async Task Group_TwoMembersOneOccupied_AveragedAt50Pct()
    {
        var resource1 = MakeResource(AllocationModes.Exclusive) with { Id = Guid.NewGuid() };
        var resource2 = MakeResource(AllocationModes.Exclusive) with { Id = Guid.NewGuid() };
        var from = new DateTime(2026, 9, 10, 0, 0, 0, DateTimeKind.Utc);
        var to = from.AddDays(1);

        var resourceRepo = new Mock<IResourceRepository>();
        resourceRepo.Setup(r => r.GetByIdAsync(resource1.Id)).ReturnsAsync(resource1);
        resourceRepo.Setup(r => r.GetByIdAsync(resource2.Id)).ReturnsAsync(resource2);
        resourceRepo.Setup(r => r.GetAllAsync(It.IsAny<ResourceListFilter>()))
            .ReturnsAsync([resource1, resource2]);

        var assignmentRepo = new Mock<IResourceAssignmentRepository>();
        // resource1 occupied, resource2 free
        assignmentRepo.Setup(r => r.GetByResourceAsync(resource1.Id, It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync([MakeAssignment(resource1.Id, from, to)]);
        assignmentRepo.Setup(r => r.GetByResourceAsync(resource2.Id, It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync([]);

        var groupRepo = new Mock<IResourceGroupMemberRepository>();
        groupRepo.Setup(r => r.GetMembersAsync(GroupId))
            .ReturnsAsync(new ResourceGroupMembersResponse { GroupId = GroupId, Members = [resource1, resource2] });

        var resolver = new Mock<IAvailabilityResolver>();
        resolver.Setup(r => r.GetBlockedPeriodsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var service = new UtilizationService(resourceRepo.Object, assignmentRepo.Object, groupRepo.Object, resolver.Object);
        var result = await service.GetGroupUtilizationAsync(GroupId, from, to, "day");

        Assert.Single(result!.Buckets);
        Assert.Equal(50m, result.Buckets[0].AllocatedPercent); // (100+0)/2
    }
}
