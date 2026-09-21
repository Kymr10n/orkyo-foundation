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
/// Fixed occupancies are read back from stored schedules, whose <c>end_ts</c> is half-open, and
/// put on the working-time axis as half-open minute ranges. An applied placement must occupy
/// exactly the minutes it holds — a day-bucketed reading used to phantom-occupy one extra day
/// of the resource on every subsequent solve — and a booking that lies entirely in
/// non-working time must survive as a point rather than vanish.
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

    private static SchedulingProblemBuilder Build(
        List<RequestInfo> scheduled,
        SchedulingSettingsInfo? settings = null,
        Dictionary<Guid, List<BlockedPeriod>>? blocked = null)
    {
        var scheduleReads = new Mock<IRequestScheduleReadRepository>();
        scheduleReads.Setup(r => r.GetUnscheduledAsync(
                It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        scheduleReads.Setup(r => r.GetPartiallyScheduledLeavesAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        scheduleReads.Setup(r => r.GetScheduledBySiteWindowAsync(
                It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(scheduled);

        // The booked resource is in the run's pool: only bookings on pool resources occupy it.
        var resources = new Mock<IResourceRepository>();
        resources.Setup(r => r.GetEveryAsync(It.IsAny<ResourceListFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ResourceInfo
            {
                Id = ResourceId,
                ResourceTypeId = Guid.NewGuid(),
                ResourceTypeKey = ResourceTypeKeys.Space,
                Name = "Room",
                AllocationMode = AllocationModes.Exclusive,
                BaseAvailabilityPercent = 100,
                IsActive = true,
                CrossSiteAllowed = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            }]);

        var capabilities = new Mock<IResourceCapabilityRepository>();
        capabilities.Setup(c => c.GetByResourcesAsync(
                It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var scheduling = new Mock<ISchedulingRepository>();
        scheduling.Setup(s => s.GetSettingsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(settings);

        var resolver = new Mock<IAvailabilityResolver>();
        resolver.Setup(r => r.GetBlockedPeriodsForResourcesAsync(
                It.IsAny<Guid>(), It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(blocked ?? []);

        var dependencies = new Mock<IRequestDependencyRepository>();
        dependencies.Setup(d => d.GetBySuccessorsAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        return new SchedulingProblemBuilder(
            Mock.Of<IRequestRepository>(), scheduleReads.Object, resources.Object, capabilities.Object,
            scheduling.Object, resolver.Object, dependencies.Object,
            TimeProvider.System);
    }

    private static AutoSchedulePreviewRequest Preview() => new(
        SiteId, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31),
        ResourceTypeKeys: [ResourceTypeKeys.Space]);

    private static readonly SchedulingSettingsInfo Weekdays = SchedulingSettingsInfo.Default(SiteId) with
    {
        WorkingHoursEnabled = true,
        WorkingDayStart = new TimeOnly(8, 0),
        WorkingDayEnd = new TimeOnly(17, 0),
        WeekendsEnabled = false,
    };

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
        occ.Start.Should().Be(1 * 1440, "2 March is the second horizon day");
        occ.End.Should().Be(2 * 1440);
    }

    [Fact]
    public async Task AManualWindow_OccupiesItsExactMinutes()
    {
        // A drag-scheduled 09:00–17:00 window occupies those hours and nothing more.
        var builder = Build([Scheduled(
            new DateTime(2026, 3, 2, 9, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 3, 2, 17, 0, 0, DateTimeKind.Utc))]);

        var problem = await builder.BuildAsync(Preview(), CancellationToken.None);

        var occ = problem.FixedAssignments.Single();
        occ.Start.Should().Be(1440 + 9 * 60);
        occ.End.Should().Be(1440 + 17 * 60);
    }

    [Fact]
    public async Task UnderWorkingHours_ABookingOutsideThemCollapsesToAPointAndIsKept()
    {
        // Saturday 7 March 2026, 09:00–13:00: no working minutes, but the resource is taken.
        // A point at the Friday/Monday boundary is what stops a job from spanning the weekend.
        var builder = Build([Scheduled(
            new DateTime(2026, 3, 7, 9, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 3, 7, 13, 0, 0, DateTimeKind.Utc))], settings: Weekdays);

        var problem = await builder.BuildAsync(Preview(), CancellationToken.None);

        var occ = problem.FixedAssignments.Should().ContainSingle().Subject;
        occ.End.Should().Be(occ.Start);
        occ.Start.Should().Be(5 * 9 * 60, "Mon 2 March to Fri 6 March is five working days");
    }

    [Fact]
    public async Task UnderWorkingHours_ABlockedPeriodOutsideThemIsDropped()
    {
        // Saturday maintenance costs no working capacity; keeping it as a point would forbid a
        // job from running Friday into Monday for no reason the calendar supports.
        var saturday = new DateTime(2026, 3, 7, 9, 0, 0, DateTimeKind.Utc);
        var tuesday = new DateTime(2026, 3, 3, 10, 0, 0, DateTimeKind.Utc);
        var builder = Build([], settings: Weekdays, blocked: new()
        {
            [ResourceId] =
            [
                new BlockedPeriod { Id = Guid.NewGuid(), Source = BlockedPeriodSource.AvailabilityEvent, Title = "Maintenance", StartTs = saturday, EndTs = saturday.AddHours(4) },
                new BlockedPeriod { Id = Guid.NewGuid(), Source = BlockedPeriodSource.AvailabilityEvent, Title = "Service", StartTs = tuesday, EndTs = tuesday.AddHours(2) },
            ],
        });

        var problem = await builder.BuildAsync(Preview(), CancellationToken.None);

        // Only the Tuesday one survives, as an occupancy of two working hours.
        var occ = problem.FixedAssignments.Should().ContainSingle().Subject;
        occ.RequestId.Should().Be(Guid.Empty);
        occ.ResourceId.Should().Be(ResourceId);
        (occ.End - occ.Start).Should().Be(120);
        occ.Start.Should().Be(9 * 60 + 2 * 60, "Tuesday 10:00 is one working day plus two hours in");
    }
}
