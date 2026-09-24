using Api.Constants;
using Api.Models;
using Api.Repositories;
using Api.Services;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace Api.Tests.Services;

public class ResourceStatusServiceTests
{
    private static readonly DateTime Now = new(2026, 9, 24, 10, 0, 0, DateTimeKind.Utc);

    private readonly Mock<IResourceRepository> _resources = new();
    private readonly Mock<IResourceAssignmentService> _assignments = new();
    private readonly Mock<IResourceAbsenceRepository> _absences = new();
    private readonly Mock<IRequestRepository> _requests = new();
    private readonly Mock<IConflictService> _conflicts = new();
    private readonly Mock<IUtilizationService> _utilization = new();
    private readonly ResourceStatusService _service;

    private readonly ResourceInfo _drill = new()
    {
        Id = Guid.NewGuid(),
        ResourceTypeId = Guid.NewGuid(),
        ResourceTypeKey = "machine",
        Name = "Drill",
        AllocationMode = "Exclusive",
        BaseAvailabilityPercent = 100,
        IsActive = true,
    };

    private List<ResourceAssignmentInfo> _booked = [];

    public ResourceStatusServiceTests()
    {
        _resources.Setup(r => r.GetByIdAsync(_drill.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_drill);
        _assignments.Setup(a => a.GetByResourceAsync(_drill.Id, Now, Now.AddDays(30), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _booked);
        _absences.Setup(a => a.GetByResourceAsync(_drill.Id, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _requests.Setup(r => r.GetByIdsAsync(It.IsAny<IReadOnlyList<Guid>>(), false, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<Guid> ids, bool _, CancellationToken _) =>
                ids.Select(id => Request(id, $"Job {id.ToString()[..4]}")).ToList());
        _conflicts.Setup(c => c.CountResourceConflictsAsync(It.IsAny<IReadOnlyList<ResourceAssignmentInfo>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        _service = new ResourceStatusService(
            _resources.Object, _assignments.Object, _absences.Object, _requests.Object,
            _conflicts.Object, _utilization.Object, new FakeTimeProvider(Now));
    }

    private static RequestInfo Request(Guid id, string name) => new()
    {
        Id = id,
        Name = name,
        PlanningMode = PlanningMode.Leaf,
        Status = RequestStatus.New,
        SchedulingSettingsApply = false,
        Assignments = [],
        TargetResourceTypeKeys = [],
        MinimalDurationValue = 1,
        MinimalDurationUnit = DurationUnit.Hours,
    };

    private ResourceAssignmentInfo Booking(DateTime start, DateTime end) => new()
    {
        Id = Guid.NewGuid(),
        RequestId = Guid.NewGuid(),
        ResourceId = _drill.Id,
        ResourceTypeKey = "machine",
        StartUtc = start,
        EndUtc = end,
        AssignmentStatus = AssignmentStatuses.Planned,
    };

    private ResourceAbsenceInfo Absence(DateTime start, DateTime end, bool enabled = true) => new()
    {
        Id = Guid.NewGuid(),
        ResourceId = _drill.Id,
        AbsenceType = AbsenceType.Maintenance,
        Title = "Service",
        StartTs = start,
        EndTs = end,
        IsRecurring = false,
        Enabled = enabled,
    };

    [Fact]
    public async Task Get_UnknownResource_ReturnsNull()
    {
        Assert.Null(await _service.GetAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Get_IdleResource_HasNoBookingsAndNoRequestLookup()
    {
        var status = await _service.GetAsync(_drill.Id);

        Assert.Equal("Drill", status!.Name);
        Assert.Equal(Now, status.AsOfUtc);
        Assert.Null(status.Current);
        Assert.Null(status.Next);
        Assert.Null(status.ActiveAbsence);
        Assert.Null(status.UtilizationPercent);
        _requests.Verify(r => r.GetByIdsAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Get_PicksTheRunningAndTheEarliestUpcomingBooking()
    {
        var running = Booking(Now.AddHours(-1), Now.AddHours(1));
        var later = Booking(Now.AddDays(3), Now.AddDays(4));
        var sooner = Booking(Now.AddDays(1), Now.AddDays(2));
        _booked = [running, later, sooner];

        var status = await _service.GetAsync(_drill.Id);

        Assert.Equal(running.Id, status!.Current!.AssignmentId);
        Assert.Equal($"Job {running.RequestId.ToString()[..4]}", status.Current.RequestName);
        Assert.Equal(sooner.Id, status.Next!.AssignmentId);
    }

    [Fact]
    public async Task Get_BookingEndingNow_IsNotCurrent()
    {
        _booked = [Booking(Now.AddHours(-2), Now)];

        Assert.Null((await _service.GetAsync(_drill.Id))!.Current);
    }

    [Fact]
    public async Task Get_ReportsOnlyAnEnabledAbsenceCoveringNow()
    {
        var active = Absence(Now.AddDays(-1), Now.AddDays(1));
        _absences.Setup(a => a.GetByResourceAsync(_drill.Id, It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            Absence(Now.AddDays(-1), Now.AddDays(1), enabled: false),
            Absence(Now.AddDays(1), Now.AddDays(2)),
            active,
        ]);

        var status = await _service.GetAsync(_drill.Id);

        Assert.Equal(active.Id, status!.ActiveAbsence!.Id);
        Assert.Equal(AbsenceType.Maintenance, status.ActiveAbsence.AbsenceType);
    }

    [Fact]
    public async Task Get_CountsConflictsOfTheLoadedBookings()
    {
        _booked = [Booking(Now.AddDays(1), Now.AddDays(2))];
        _conflicts.Setup(c => c.CountResourceConflictsAsync(_booked, It.IsAny<CancellationToken>())).ReturnsAsync(2);

        Assert.Equal(2, (await _service.GetAsync(_drill.Id))!.ConflictCount);
    }

    [Fact]
    public async Task Get_AveragesDailyUtilizationOverTheLast30Days()
    {
        _utilization.Setup(u => u.GetResourceUtilizationAsync(_drill.Id, Now.AddDays(-30), Now, "day", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UtilizationResponse
            {
                From = Now.AddDays(-30),
                To = Now,
                Granularity = "day",
                Buckets = [Bucket(100m), Bucket(0m), Bucket(25m)],
            });

        var status = await _service.GetAsync(_drill.Id);

        Assert.Equal(41.7m, status!.UtilizationPercent);
        Assert.Equal(30, status.UtilizationDays);
    }

    private static UtilizationBucket Bucket(decimal allocated) => new()
    {
        Start = Now,
        End = Now,
        AllocatedPercent = allocated,
        EffectiveAvailabilityPercent = 100m,
        IsExclusiveOccupied = false,
    };
}
