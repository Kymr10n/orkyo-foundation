using Api.Models;
using Api.Repositories;
using Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Api.Tests.Services;

/// <summary>
/// Unit tests for SchedulingService.ApplySchedulingToUpdateAsync — specifically that an
/// explicitly-provided schedule window is preserved (so a window shorter than the minimal
/// duration persists and surfaces a `below_min_duration` conflict, matching the edit dialog's
/// promise), while the start-only convenience still computes the end from the duration.
/// </summary>
public class SchedulingServiceTests
{
    private readonly Mock<ISchedulingRepository> _schedulingRepo = new();
    private readonly Mock<IRequestRepository> _requestRepo = new();
    private readonly Mock<IResourceRepository> _resourceRepo = new();
    private readonly Mock<IAvailabilityResolver> _resolver = new();
    private readonly SchedulingService _service;

    private static readonly DateTime Start = new(2026, 6, 8, 9, 0, 0, DateTimeKind.Utc); // a Monday

    public SchedulingServiceTests()
    {
        // Working-hours resolution follows the first resource that cannot travel, so the
        // default fixture is an immovable one (a space).
        _resourceRepo.Setup(r => r.GetByIdsAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<Guid> ids, CancellationToken _) =>
                ids.Select(id => new ResourceInfo
                {
                    Id = id,
                    ResourceTypeId = Guid.NewGuid(),
                    ResourceTypeKey = ResourceTypeKeys.Space,
                    Name = "Fixture",
                    AllocationMode = AllocationModes.Exclusive,
                    BaseAvailabilityPercent = 100,
                    IsActive = true,
                    CrossSiteAllowed = false,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                }).ToList());

        // Placement resolution asks which types are placeable; these tests schedule onto spaces.
        var typeRepo = new Mock<IResourceTypeRepository>();
        typeRepo.Setup(r => r.GetPlaceableKeysAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(["space"]);

        _service = new SchedulingService(
            _schedulingRepo.Object, _requestRepo.Object, typeRepo.Object,
            _resourceRepo.Object, _resolver.Object,
            NullLogger<SchedulingService>.Instance);
    }

    private static RequestInfo ExistingRequest(bool applyScheduling = true) => new()
    {
        Id = Guid.NewGuid(),
        Name = "Test",
        PlanningMode = PlanningMode.Leaf,
        Status = RequestStatus.New,
        SchedulingSettingsApply = applyScheduling,
        Requirements = new List<RequestRequirementInfo>(),
        Assignments = new List<ResourceAssignmentInfo>(),
        TargetResourceTypeKeys = [ResourceTypeKeys.Space],
        MinimalDurationValue = 9,
        MinimalDurationUnit = DurationUnit.Hours,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
    };

    [Fact]
    public async Task ApplySchedulingToUpdate_PreservesExplicitWindow_EvenWhenShorterThanMinimalDuration()
    {
        var requestId = Guid.NewGuid();
        _requestRepo.Setup(r => r.GetByIdAsync(requestId, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ExistingRequest());

        // Window = 8h, minimal duration = 9h, scheduling settings on, a resource assigned.
        var request = new UpdateRequestRequest
        {
            ResourceIds = [Guid.NewGuid()],
            StartTs = Start,
            EndTs = Start.AddHours(8),
            MinimalDurationValue = 9,
            MinimalDurationUnit = DurationUnit.Hours,
            SchedulingSettingsApply = true,
        };

        var result = await _service.ApplySchedulingToUpdateAsync(requestId, request);

        // The explicit window is preserved (NOT extended to start + minimal duration).
        Assert.Equal(Start, result.StartTs);
        Assert.Equal(Start.AddHours(8), result.EndTs);
        // Early return: scheduling computation is never invoked when an end is provided.
        _schedulingRepo.Verify(s => s.GetSiteIdForResourceAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ApplySchedulingToUpdate_StartOnly_ComputesEndFromDuration()
    {
        var requestId = Guid.NewGuid();
        var resourceId = Guid.NewGuid();
        var siteId = Guid.NewGuid();
        _requestRepo.Setup(r => r.GetByIdAsync(requestId, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ExistingRequest());
        _schedulingRepo.Setup(s => s.GetSiteIdForResourceAsync(resourceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(siteId);
        // Working hours disabled → schedule is plain start + duration (no snapping).
        _schedulingRepo.Setup(s => s.GetSettingsAsync(siteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SchedulingSettingsInfo.Default(siteId));
        _resolver.Setup(r => r.GetBlockedPeriodsForResourcesAsync(
                It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, List<BlockedPeriod>>());

        // No end provided → the service computes it from start + minimal duration (2h here).
        var request = new UpdateRequestRequest
        {
            ResourceIds = [resourceId],
            StartTs = Start,
            EndTs = null,
            MinimalDurationValue = 2,
            MinimalDurationUnit = DurationUnit.Hours,
            SchedulingSettingsApply = true,
        };

        var result = await _service.ApplySchedulingToUpdateAsync(requestId, request);

        Assert.Equal(Start, result.StartTs);
        Assert.Equal(Start.AddHours(2), result.EndTs);
    }

    [Fact]
    public async Task ApplySchedulingToCreate_PreservesAnExplicitWindow()
    {
        // The reviewer typed Sep 2 08:00-12:00 and it saved as Sep 4 06:00-10:00: create used to
        // recompute even when the caller supplied an end, while update has always preserved it.
        // An explicit window persists and surfaces conflicts instead of being silently moved.
        var request = new CreateRequestRequest
        {
            Name = "Bracket run",
            ResourceIds = [Guid.NewGuid()],
            StartTs = Start,
            EndTs = Start.AddHours(4),
            MinimalDurationValue = 2,
            MinimalDurationUnit = DurationUnit.Hours,
            SchedulingSettingsApply = true,
        };

        var result = await _service.ApplySchedulingToCreateAsync(request);

        Assert.Equal(Start, result.StartTs);
        Assert.Equal(Start.AddHours(4), result.EndTs);
        // Early return: scheduling computation is never invoked when an end is provided.
        _schedulingRepo.Verify(s => s.GetSiteIdForResourceAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ApplySchedulingToCreate_WalksPastAnAbsenceOnASecondResource()
    {
        // Working hours follow the immovable resource, but the slot has to be free for every
        // resource on the request. Only the site-bearing one used to be checked, so a job landed
        // on a day its operator was on leave and conflicted the moment it was saved.
        var machineId = Guid.NewGuid();
        var operatorId = Guid.NewGuid();
        var siteId = Guid.NewGuid();

        _schedulingRepo.Setup(s => s.GetSiteIdForResourceAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(siteId);
        _schedulingRepo.Setup(s => s.GetSettingsAsync(siteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SchedulingSettingsInfo.Default(siteId));
        _resolver.Setup(r => r.GetBlockedPeriodsForResourcesAsync(
                It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, List<BlockedPeriod>>
            {
                [machineId] = [],
                [operatorId] =
                [
                    new BlockedPeriod
                    {
                        Id = Guid.NewGuid(),
                        Title = "Vacation",
                        Source = BlockedPeriodSource.ResourceAbsence,
                        StartTs = Start.AddHours(-1),
                        EndTs = Start.AddHours(4),
                    },
                ],
            });

        var request = new CreateRequestRequest
        {
            Name = "Bracket run",
            ResourceIds = [machineId, operatorId],
            StartTs = Start,
            MinimalDurationValue = 2,
            MinimalDurationUnit = DurationUnit.Hours,
            SchedulingSettingsApply = true,
        };

        var result = await _service.ApplySchedulingToCreateAsync(request);

        // Pushed to the end of the operator's absence rather than left on top of it.
        Assert.Equal(Start.AddHours(4), result.StartTs);
        Assert.Equal(Start.AddHours(6), result.EndTs);
    }
}
