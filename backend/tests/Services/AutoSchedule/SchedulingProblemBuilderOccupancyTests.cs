using Api.Constants;
using Api.Models;
using Api.Repositories;
using Api.Services;
using Api.Services.AutoSchedule;
using AwesomeAssertions;
using Moq;
using Xunit;

namespace Orkyo.Foundation.Tests.Services.AutoSchedule;

/// <summary>
/// Fixed occupancies are read back from stored schedules, whose <c>end_ts</c> is half-open —
/// a one-day placement applied on 03-02 is stored as <c>[03-02 00:00, 03-03 00:00)</c>. The
/// analyzer's overlap check treats an occupancy's End as INCLUSIVE, so the conversion must land
/// on the last occupied day, not the raw end date. Getting this wrong made every applied
/// placement phantom-occupy one extra day of the resource on every subsequent solve.
/// </summary>
public class SchedulingProblemBuilderOccupancyTests
{
    private static readonly Guid SiteId = Guid.NewGuid();
    private static readonly Guid ResourceId = Guid.NewGuid();

    private static RequestInfo Scheduled(DateTime startTs, DateTime endTs)
    {
        var id = Guid.NewGuid();
        return new RequestInfo
        {
            Id = id,
            Name = "Applied placement",
            PlanningMode = PlanningMode.Leaf,
            MinimalDurationValue = 1,
            MinimalDurationUnit = DurationUnit.Days,
            Status = RequestStatus.New,
            SchedulingSettingsApply = true,
            StartTs = startTs,
            EndTs = endTs,
            TargetResourceTypeKeys = [ResourceTypeKeys.Space],
            Assignments =
            [
                new ResourceAssignmentInfo
                {
                    Id = Guid.NewGuid(),
                    RequestId = id,
                    ResourceId = ResourceId,
                    ResourceTypeKey = ResourceTypeKeys.Space,
                    StartUtc = startTs,
                    EndUtc = endTs,
                    AssignmentStatus = "Planned",
                },
            ],
        };
    }

    private static SchedulingProblemBuilder Build(List<RequestInfo> scheduled)
    {
        var requests = new Mock<IRequestRepository>();
        requests.Setup(r => r.GetUnscheduledAsync(
                It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        requests.Setup(r => r.GetPartiallyScheduledLeavesAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        requests.Setup(r => r.GetScheduledBySiteWindowAsync(
                It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(scheduled);

        var resources = new Mock<IResourceRepository>();
        resources.Setup(r => r.GetAllAsync(It.IsAny<ResourceListFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        resources.Setup(r => r.GetEveryAsync(It.IsAny<ResourceListFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var capabilities = new Mock<IResourceCapabilityRepository>();
        capabilities.Setup(c => c.GetByResourcesAsync(
                It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var scheduling = new Mock<ISchedulingRepository>();
        scheduling.Setup(s => s.GetSettingsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SchedulingSettingsInfo?)null);

        var resolver = new Mock<IAvailabilityResolver>();
        resolver.Setup(r => r.GetBlockedPeriodsForResourcesAsync(
                It.IsAny<Guid>(), It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var dependencies = new Mock<IRequestDependencyRepository>();
        dependencies.Setup(d => d.GetBySuccessorsAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        return new SchedulingProblemBuilder(
            requests.Object, resources.Object, capabilities.Object,
            scheduling.Object, resolver.Object, dependencies.Object);
    }

    private static AutoSchedulePreviewRequest Preview() => new(
        SiteId, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31),
        ResourceTypeKey: ResourceTypeKeys.Space);

    [Fact]
    public async Task AnAppliedOneDayPlacement_OccupiesExactlyItsOwnDay()
    {
        // The regression the review found: a one-day apply stores [03-02, 03-03) and the next
        // solve read it back as busy on 03-02 AND 03-03 — sterilising the adjacent day.
        var builder = Build([Scheduled(
            new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 3, 3, 0, 0, 0, DateTimeKind.Utc))]);

        var problem = await builder.BuildAsync(Preview(), CancellationToken.None);

        var occ = problem.FixedAssignments.Should().ContainSingle().Subject;
        occ.Start.Should().Be(new DateOnly(2026, 3, 2));
        occ.End.Should().Be(new DateOnly(2026, 3, 2));
    }

    [Fact]
    public async Task AManualMidDayWindow_StillOccupiesItsEndDate()
    {
        // A drag-scheduled 09:00–17:00 window genuinely occupies its end day; the half-open
        // exclusion applies to exactly-midnight ends only.
        var builder = Build([Scheduled(
            new DateTime(2026, 3, 2, 9, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 3, 4, 17, 0, 0, DateTimeKind.Utc))]);

        var problem = await builder.BuildAsync(Preview(), CancellationToken.None);

        problem.FixedAssignments.Single().End.Should().Be(new DateOnly(2026, 3, 4));
    }
}
