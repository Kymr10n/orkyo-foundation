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
    private readonly Mock<IAvailabilityResolver> _resolver = new();
    private readonly SchedulingService _service;

    private static readonly DateTime Start = new(2026, 6, 8, 9, 0, 0, DateTimeKind.Utc); // a Monday

    public SchedulingServiceTests()
    {
        _service = new SchedulingService(
            _schedulingRepo.Object, _requestRepo.Object, _resolver.Object, NullLogger<SchedulingService>.Instance);
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
            ResourceId = Guid.NewGuid(),
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
        _resolver.Setup(r => r.GetBlockedPeriodsAsync(resourceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<BlockedPeriod>());

        // No end provided → the service computes it from start + minimal duration (2h here).
        var request = new UpdateRequestRequest
        {
            ResourceId = resourceId,
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
}
