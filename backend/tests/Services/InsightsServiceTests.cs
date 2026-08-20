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
/// Unit tests for the parts of <see cref="InsightsService"/> that aggregate the live conflict service
/// and compute time-based utilization (no analytics view → no DB needed; the view-backed request paths
/// are covered by the endpoint integration tests). Verifies conflict-kind→category mapping, site
/// filtering, bucketing, and — crucially — that utilization is time-based occupancy, not the grid's
/// "occupied at all → 100%" per-slot view.
/// </summary>
public class InsightsServiceTests
{
    private readonly Mock<IOrgDbConnectionFactory> _db = new();
    private readonly Mock<IConflictService> _conflicts = new();
    private readonly Mock<IRequestRepository> _requests = new();
    private readonly Mock<IConflictTimelineProvider> _timeline = new();
    private readonly Mock<IResourceRepository> _resources = new();
    private readonly Mock<IResourceAssignmentRepository> _assignments = new();
    private readonly Mock<IAvailabilityResolver> _availability = new();
    private readonly Mock<IResourceTypeService> _resourceTypes = new();
    private readonly InsightsService _service;

    public InsightsServiceTests()
    {
        var org = new OrgContext { OrgId = Guid.NewGuid(), OrgSlug = "test", DbConnectionString = "unused" };
        _service = new InsightsService(
            org, _db.Object, _timeline.Object,
            _resources.Object, _resourceTypes.Object, _assignments.Object, _availability.Object);

        // The overview now fans out over the tenant's active types instead of a fixed triple.
        _resourceTypes.Setup(t => t.GetAllAsync(true, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                TypeInfo(ResourceTypeKeys.Space, "Space"),
                TypeInfo(ResourceTypeKeys.Person, "Person"),
                TypeInfo(ResourceTypeKeys.Tool, "Tool"),
            ]);

        // Sensible empties — individual tests override.
        _timeline.Setup(t => t.GetAsync(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _conflicts.Setup(c => c.GetAllAsync(It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _requests.Setup(r => r.GetScheduledLiteAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _resources.Setup(r => r.GetAllAsync(It.IsAny<ResourceListFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _resources.Setup(r => r.GetEveryAsync(It.IsAny<ResourceListFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _assignments.Setup(a => a.GetByResourceAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _availability.Setup(a => a.GetBlockedPeriodsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        // Bulk preloading paths (utilization series): empty by default; individual tests override.
        _assignments.Setup(a => a.GetActiveByResourcesAsync(
                It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _availability.Setup(a => a.GetBlockedPeriodsForResourcesAsync(
                It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<Guid> ids, CancellationToken _) =>
                ids.ToDictionary(id => id, _ => new List<BlockedPeriod>()));
    }

    private static ResourceTypeInfo TypeInfo(string key, string displayName) => new()
    {
        Id = Guid.NewGuid(),
        Key = key,
        HasGeometry = key == ResourceTypeKeys.Space,
        HasDirectoryProfile = key == ResourceTypeKeys.Person,
        SingleGroupMembership = key == ResourceTypeKeys.Space,
        DisplayName = displayName,
        DisplayNamePlural = displayName + "s",
        IsSystem = false,
        IsActive = true,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
    };

    private static readonly DateTime Jan = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Feb = new(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Mar = new(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
    private const int JanMinutes = 31 * 24 * 60; // 44640

    private static ScheduledRequestLite Scheduled(Guid id, DateTime start, Guid? siteId) => new(id, start, siteId);

    private static ConflictInfo Conflict(string kind) => new()
    {
        Id = $"{Guid.NewGuid()}-{kind}",
        Kind = kind,
        Severity = "error",
        Message = kind,
    };

    private static ResourceInfo SpaceResource(Guid id, int availability = 100) => new()
    {
        Id = id,
        ResourceTypeId = Guid.NewGuid(),
        ResourceTypeKey = ResourceTypeKeys.Space,
        Name = "Room",
        AllocationMode = AllocationModes.Exclusive,
        BaseAvailabilityPercent = availability,
        IsActive = true,
    };

    private static ResourceAssignmentInfo Assignment(Guid resourceId, DateTime start, DateTime end, decimal? allocPct = null) => new()
    {
        Id = Guid.NewGuid(),
        RequestId = Guid.NewGuid(),
        ResourceId = resourceId,
        ResourceTypeKey = ResourceTypeKeys.Space,
        StartUtc = start,
        EndUtc = end,
        AssignmentStatus = AssignmentStatuses.Planned,
        AllocationPercent = allocPct,
        CreatedAt = start,
        UpdatedAt = start,
    };

    private static BlockedPeriod Block(DateTime start, DateTime end) => new()
    {
        Id = Guid.NewGuid(),
        StartTs = start,
        EndTs = end,
        Title = "Shutdown",
        Source = BlockedPeriodSource.AvailabilityEvent,
    };

    private void SetupResource(ResourceInfo resource, params ResourceAssignmentInfo[] assignments)
    {
        _resources.Setup(r => r.GetAllAsync(It.IsAny<ResourceListFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([resource]);
        _resources.Setup(r => r.GetEveryAsync(It.IsAny<ResourceListFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([resource]);
        _assignments.Setup(a => a.GetByResourceAsync(resource.Id, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([.. assignments]);
        _assignments.Setup(a => a.GetActiveByResourcesAsync(
                It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([.. assignments]);
    }

    /// <summary>Stubs the shared conflict timeline. Site filtering is the provider's job now, so
    /// these tests hand over the points it would have returned and assert what Insights does with
    /// them — mapping kinds to categories and placing each in the right bucket.</summary>
    private void Timeline(params ConflictPoint[] points) =>
        _timeline.Setup(t => t.GetAsync(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([.. points]);

    // ── Conflict mapping + bucketing ──────────────────────────────────────────

    [Fact]
    public async Task ConflictTrend_MapsLiveKindsIntoStableCategoriesInTheRightBucket()
    {
        var febStart = new DateTime(2026, 2, 10, 9, 0, 0, DateTimeKind.Utc);
        Timeline(
            new ConflictPoint(febStart, "overlap"),
            new ConflictPoint(febStart, "capacity_exceeded"),
            new ConflictPoint(febStart, "connector_mismatch"),
            new ConflictPoint(febStart, "starts_in_off_time"),
            new ConflictPoint(febStart, "site_mismatch"),
            new ConflictPoint(febStart, "below_min_duration"));

        var result = await _service.GetConflictTrendAsync(
            new InsightsFilter { From = Jan, To = Mar, Bucket = "month" });

        Assert.Equal(2, result.Series.Count); // Jan, Feb
        Assert.Equal(0, result.Series[0].Total);
        var feb = result.Series[1];
        Assert.Equal(6, feb.Total);
        Assert.Equal(2, feb.Overbooking);                 // overlap + capacity_exceeded
        Assert.Equal(1, feb.CriteriaMismatch);            // connector_mismatch
        Assert.Equal(2, feb.ResourceUnavailable);         // starts_in_off_time + site_mismatch
        Assert.Equal(1, feb.ScheduleOutsideAvailability); // below_min_duration
        Assert.Equal(0, feb.MissingResource);             // no live kind maps here — honest 0
    }

    [Fact]
    public async Task UtilizationTrend_PartialBooking_IsTimeBased_NotPinnedAt100()
    {
        // The regression: an Exclusive room booked for just 4 hours in a month must read ~0.5%,
        // NOT 100% (which the grid's per-slot occupied-flag would have produced).
        var room = Guid.NewGuid();
        SetupResource(SpaceResource(room),
            Assignment(room, new DateTime(2026, 1, 10, 9, 0, 0, DateTimeKind.Utc),
                             new DateTime(2026, 1, 10, 13, 0, 0, DateTimeKind.Utc)));

        var result = await _service.GetUtilizationTrendAsync(
            new InsightsFilter { From = Jan, To = Mar, Bucket = "month", ResourceType = "space" });

        var jan = result.Series[0];
        Assert.Equal(JanMinutes, jan.TotalCapacityMinutes);
        Assert.Equal(240, jan.UsedCapacityMinutes);                 // 4 hours
        Assert.NotNull(jan.UtilizationPercent);
        Assert.InRange(jan.UtilizationPercent!.Value, 0.01m, 5m);   // ≈ 0.54%, emphatically not 100
        Assert.Equal(0m, result.Series[1].UtilizationPercent);      // Feb: capacity but no usage → 0%
    }

    [Fact]
    public async Task UtilizationTrend_Overbooking_CapsAtFullCapacity()
    {
        // Two assignments each spanning all of January on one Exclusive room. Raw overlap is 2× the
        // month, but a room can't be more than 100% occupied — overbooking is a conflict, not >100%.
        var room = Guid.NewGuid();
        SetupResource(SpaceResource(room),
            Assignment(room, Jan, Feb),
            Assignment(room, Jan, Feb));

        var result = await _service.GetUtilizationTrendAsync(
            new InsightsFilter { From = Jan, To = Mar, Bucket = "month", ResourceType = "space" });

        var jan = result.Series[0];
        Assert.Equal(JanMinutes, jan.TotalCapacityMinutes);
        Assert.Equal(JanMinutes, jan.UsedCapacityMinutes); // capped at capacity
        Assert.Equal(100m, jan.UtilizationPercent);
    }

    [Fact]
    public async Task UtilizationTrend_BlockedTimeReducesCapacity()
    {
        // First half of January is blocked (shutdown) → capacity is the open minutes only.
        var room = Guid.NewGuid();
        _resources.Setup(r => r.GetAllAsync(It.IsAny<ResourceListFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([SpaceResource(room)]);
        _resources.Setup(r => r.GetEveryAsync(It.IsAny<ResourceListFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([SpaceResource(room)]);
        _availability.Setup(a => a.GetBlockedPeriodsForResourcesAsync(
                It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<Guid> ids, CancellationToken _) => ids.ToDictionary(
                id => id,
                id => id == room
                    ? new List<BlockedPeriod> { Block(Jan, new DateTime(2026, 1, 16, 0, 0, 0, DateTimeKind.Utc)) } // 15 days
                    : []));

        var result = await _service.GetUtilizationTrendAsync(
            new InsightsFilter { From = Jan, To = Mar, Bucket = "month", ResourceType = "space" });

        Assert.Equal(JanMinutes - 15 * 24 * 60, result.Series[0].TotalCapacityMinutes); // 44640 - 21600
    }

    [Fact]
    public async Task UtilizationTrend_NullPercentWhenNoCapacityConfigured()
    {
        // No resources of this type → no capacity → honest null, not a fake 0%.
        var result = await _service.GetUtilizationTrendAsync(
            new InsightsFilter { From = Jan, To = Mar, Bucket = "month", ResourceType = "tool" });

        Assert.All(result.Series, s =>
        {
            Assert.Equal(0, s.TotalCapacityMinutes);
            Assert.Null(s.UtilizationPercent);
        });
    }

    [Fact]
    public async Task UtilizationTrend_CountsConflictsPerBucket()
    {
        Timeline(new ConflictPoint(new DateTime(2026, 1, 20, 9, 0, 0, DateTimeKind.Utc), "overlap"));

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
