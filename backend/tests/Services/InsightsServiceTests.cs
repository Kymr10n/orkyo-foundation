using Api.Constants;
using Api.Models;
using Api.Models.Insights;
using Api.Repositories;
using Api.Services;
using Api.Services.Insights;
using Moq;
using Xunit;

namespace Api.Tests.Services;

/// <summary>
/// Unit tests for the parts of <see cref="InsightsService"/> that aggregate the live conflict and
/// utilization services (no analytics view → no DB needed; the view-backed request paths are covered
/// by the endpoint integration tests). Verifies conflict-kind→category mapping, site filtering,
/// bucketing, and the capacity/used-minute utilization math.
/// </summary>
public class InsightsServiceTests
{
    private readonly Mock<IOrgDbConnectionFactory> _db = new();
    private readonly Mock<IConflictService> _conflicts = new();
    private readonly Mock<IUtilizationService> _utilization = new();
    private readonly Mock<IRequestRepository> _requests = new();
    private readonly InsightsService _service;

    public InsightsServiceTests()
    {
        var org = new OrgContext { OrgId = Guid.NewGuid(), OrgSlug = "test", DbConnectionString = "unused" };
        _service = new InsightsService(org, _db.Object, _conflicts.Object, _utilization.Object, _requests.Object);

        // Sensible empties — individual tests override.
        _conflicts.Setup(c => c.GetAllAsync(It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _requests.Setup(r => r.GetScheduledAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _utilization.Setup(u => u.GetUtilizationByResourceAsync(
                It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<string>(),
                It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
    }

    private static readonly DateTime Jan = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Mar = new(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);

    private static RequestInfo Scheduled(Guid id, DateTime start, Guid? siteId) => new()
    {
        Id = id,
        Name = "R",
        PlanningMode = PlanningMode.Leaf,
        Status = RequestStatus.Planned,
        SchedulingSettingsApply = false,
        Assignments = [],
        SiteId = siteId,
        StartTs = start,
        EndTs = start.AddHours(1),
        MinimalDurationValue = 60,
        MinimalDurationUnit = DurationUnit.Minutes,
        CreatedAt = start,
        UpdatedAt = start,
    };

    private static ConflictInfo Conflict(string kind) => new()
    {
        Id = $"{Guid.NewGuid()}-{kind}",
        Kind = kind,
        Severity = "error",
        Message = kind,
    };

    private static UtilizationBucket Bucket(decimal availPercent, decimal allocPercent) => new()
    {
        Start = Jan,
        End = Jan, // unused by the service (it uses its own bucket spans)
        AllocatedPercent = allocPercent,
        EffectiveAvailabilityPercent = availPercent,
        IsExclusiveOccupied = false,
    };

    // ── Conflict mapping + bucketing ──────────────────────────────────────────

    [Fact]
    public async Task ConflictTrend_MapsLiveKindsIntoStableCategoriesInTheRightBucket()
    {
        var reqId = Guid.NewGuid();
        var febStart = new DateTime(2026, 2, 10, 9, 0, 0, DateTimeKind.Utc);
        _conflicts.Setup(c => c.GetAllAsync(It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new RequestConflictInfo
            {
                RequestId = reqId,
                Conflicts = [Conflict("overlap"), Conflict("capacity_exceeded"), Conflict("connector_mismatch"),
                             Conflict("starts_in_off_time"), Conflict("site_mismatch"), Conflict("below_min_duration")],
            }]);
        _requests.Setup(r => r.GetScheduledAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Scheduled(reqId, febStart, siteId: null)]);

        var result = await _service.GetConflictTrendAsync(
            new InsightsFilter { From = Jan, To = Mar, Bucket = "month" });

        Assert.Equal(2, result.Series.Count); // Jan, Feb
        var jan = result.Series[0];
        Assert.Equal(0, jan.Total);
        var feb = result.Series[1];
        Assert.Equal(6, feb.Total);
        Assert.Equal(2, feb.Overbooking);                 // overlap + capacity_exceeded
        Assert.Equal(1, feb.CriteriaMismatch);            // connector_mismatch
        Assert.Equal(2, feb.ResourceUnavailable);         // starts_in_off_time + site_mismatch
        Assert.Equal(1, feb.ScheduleOutsideAvailability); // below_min_duration
        Assert.Equal(0, feb.MissingResource);             // no live kind maps here — honest 0
    }

    [Fact]
    public async Task ConflictTrend_ExcludesConflictsFromOtherSites()
    {
        var reqId = Guid.NewGuid();
        var siteA = Guid.NewGuid();
        _conflicts.Setup(c => c.GetAllAsync(It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new RequestConflictInfo { RequestId = reqId, Conflicts = [Conflict("overlap")] }]);
        _requests.Setup(r => r.GetScheduledAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Scheduled(reqId, new DateTime(2026, 1, 10, 9, 0, 0, DateTimeKind.Utc), siteId: siteA)]);

        // Filter to a different site → the siteA conflict is excluded.
        var result = await _service.GetConflictTrendAsync(
            new InsightsFilter { From = Jan, To = Mar, Bucket = "month", SiteId = Guid.NewGuid() });

        Assert.All(result.Series, s => Assert.Equal(0, s.Total));
    }

    // ── Utilization minutes math ──────────────────────────────────────────────

    [Fact]
    public async Task UtilizationTrend_AggregatesCapacityAndUsedMinutesPerBucket()
    {
        // One resource: Jan @ 100% avail / 50% alloc, Feb @ 100% avail / 25% alloc.
        _utilization.Setup(u => u.GetUtilizationByResourceAsync(
                "space", It.IsAny<DateTime>(), It.IsAny<DateTime>(), "month", It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ResourceUtilizationResponse
            {
                ResourceId = Guid.NewGuid(),
                Buckets = [Bucket(100m, 50m), Bucket(100m, 25m)],
            }]);

        var result = await _service.GetUtilizationTrendAsync(
            new InsightsFilter { From = Jan, To = Mar, Bucket = "month", ResourceType = "space" });

        Assert.Equal(2, result.Series.Count);
        var jan = result.Series[0]; // 31 days = 44640 min
        Assert.Equal(44640, jan.TotalCapacityMinutes);
        Assert.Equal(22320, jan.UsedCapacityMinutes);
        Assert.Equal(22320, jan.AvailableCapacityMinutes);
        Assert.Equal(50m, jan.UtilizationPercent);
        var feb = result.Series[1]; // 28 days = 40320 min
        Assert.Equal(40320, feb.TotalCapacityMinutes);
        Assert.Equal(10080, feb.UsedCapacityMinutes);
        Assert.Equal(25m, feb.UtilizationPercent);
    }

    [Fact]
    public async Task UtilizationTrend_NullPercentWhenNoCapacityConfigured()
    {
        _utilization.Setup(u => u.GetUtilizationByResourceAsync(
                "tool", It.IsAny<DateTime>(), It.IsAny<DateTime>(), "month", It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ResourceUtilizationResponse
            {
                ResourceId = Guid.NewGuid(),
                Buckets = [Bucket(0m, 0m), Bucket(0m, 0m)],
            }]);

        var result = await _service.GetUtilizationTrendAsync(
            new InsightsFilter { From = Jan, To = Mar, Bucket = "month", ResourceType = "tool" });

        Assert.All(result.Series, s =>
        {
            Assert.Equal(0, s.TotalCapacityMinutes);
            Assert.Null(s.UtilizationPercent); // honest null, not a fake 0%
        });
    }

    [Fact]
    public async Task UtilizationTrend_CountsConflictsPerBucket()
    {
        var reqId = Guid.NewGuid();
        _utilization.Setup(u => u.GetUtilizationByResourceAsync(
                "space", It.IsAny<DateTime>(), It.IsAny<DateTime>(), "month", It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ResourceUtilizationResponse
            {
                ResourceId = Guid.NewGuid(),
                Buckets = [Bucket(100m, 50m), Bucket(100m, 50m)],
            }]);
        _conflicts.Setup(c => c.GetAllAsync(It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new RequestConflictInfo { RequestId = reqId, Conflicts = [Conflict("overlap")] }]);
        _requests.Setup(r => r.GetScheduledAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Scheduled(reqId, new DateTime(2026, 1, 20, 9, 0, 0, DateTimeKind.Utc), siteId: null)]);

        var result = await _service.GetUtilizationTrendAsync(
            new InsightsFilter { From = Jan, To = Mar, Bucket = "month", ResourceType = "space" });

        Assert.Equal(1, result.Series[0].ConflictCount); // Jan
        Assert.Equal(0, result.Series[1].ConflictCount); // Feb
    }

    [Fact]
    public async Task Metadata_ReportsLiveSourceMode()
    {
        var result = await _service.GetConflictTrendAsync(
            new InsightsFilter { From = Jan, To = Mar, Bucket = "month" });

        Assert.Equal("live", result.Metadata.SourceMode);
    }
}
