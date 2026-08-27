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
/// One auto-schedule run fills one resource type's slot. Before requests could name a type the
/// candidate pool came from ISpaceRepository, so the solver could only ever propose rooms; these
/// cover the selection that replaced it.
/// </summary>
public class SchedulingProblemBuilderTypeTests
{
    private static readonly Guid SiteId = Guid.NewGuid();

    private static RequestInfo Leaf(string[] targets, ResourceAssignmentInfo[]? assignments = null) => new()
    {
        Id = Guid.NewGuid(),
        Name = "Leaf",
        PlanningMode = PlanningMode.Leaf,
        MinimalDurationValue = 1,
        MinimalDurationUnit = DurationUnit.Days,
        Status = RequestStatus.New,
        SchedulingSettingsApply = true,
        Assignments = assignments ?? [],
        TargetResourceTypeKeys = targets,
    };

    private static ResourceInfo Resource(string typeKey, string name) => new()
    {
        Id = Guid.NewGuid(),
        ResourceTypeId = Guid.NewGuid(),
        ResourceTypeKey = typeKey,
        Name = name,
        AllocationMode = AllocationModes.Exclusive,
        BaseAvailabilityPercent = 100,
        IsActive = true,
        CrossSiteAllowed = true,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
    };

    /// <summary>Wires the builder with the given backlog and candidate pool; captures the filter.</summary>
    private static (SchedulingProblemBuilder Builder, List<ResourceListFilter> Filters) Build(
        List<RequestInfo> backlog, List<ResourceInfo> candidates,
        SchedulingSettingsInfo? settings = null)
    {
        var filters = new List<ResourceListFilter>();

        var requests = new Mock<IRequestRepository>();
        requests.Setup(r => r.GetUnscheduledAsync(
                It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(backlog);
        requests.Setup(r => r.GetPartiallyScheduledLeavesAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        requests.Setup(r => r.GetScheduledBySiteWindowAsync(
                It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var resources = new Mock<IResourceRepository>();
        resources.Setup(r => r.GetAllAsync(It.IsAny<ResourceListFilter>(), It.IsAny<CancellationToken>()))
            .Callback((ResourceListFilter f, CancellationToken _) => filters.Add(f))
            .ReturnsAsync(candidates);
        resources.Setup(r => r.GetEveryAsync(It.IsAny<ResourceListFilter>(), It.IsAny<CancellationToken>()))
            .Callback((ResourceListFilter f, CancellationToken _) => filters.Add(f))
            .ReturnsAsync(candidates);

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
            .ReturnsAsync([]);

        return (new SchedulingProblemBuilder(
            requests.Object, resources.Object, capabilities.Object,
            scheduling.Object, resolver.Object), filters);
    }

    private static AutoSchedulePreviewRequest Preview(string? typeKey) => new(
        SiteId,
        DateOnly.FromDateTime(DateTime.UtcNow),
        DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
        ResourceTypeKey: typeKey);

    [Fact]
    public async Task PoolIsScopedToTheRequestedType()
    {
        var (builder, filters) = Build([Leaf([ResourceTypeKeys.Space])], [Resource("tool", "Drill")]);

        await builder.BuildAsync(Preview("tool"), CancellationToken.None);

        filters.Should().ContainSingle();
        filters[0].ResourceTypeKey.Should().Be("tool");
        filters[0].SiteId.Should().Be(SiteId);
        // Deactivated resources must not be offered as candidates.
        filters[0].IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task OmittingTheType_Throws_ResolutionBelongsToTheService()
    {
        // The builder used to guess `space` on its own; the guess and the fingerprint's separate
        // guess agreed only by luck. AutoScheduleService now resolves the type from the tenant's
        // placeable types before building, so a null reaching this far is a programming error and
        // must say so rather than quietly solving for the wrong pool.
        var (builder, _) = Build([], []);

        await Assert.ThrowsAsync<ArgumentException>(
            () => builder.BuildAsync(Preview(null), CancellationToken.None));
    }

    /// <summary>
    /// SchedulingValidators rejects end &lt;= start when the settings are written, so a site
    /// that reaches the builder in this state has corrupt stored data. Inventing a working
    /// day would silently schedule against a length nobody configured.
    /// </summary>
    /// <remarks>
    /// Both cases matter because TimeOnly subtraction is elapsed time and wraps at midnight:
    /// 09:00 - 17:00 is 16 hours, not -8. A guard that subtracted first would catch only the
    /// equal case and let the reversed one through as a plausible day length.
    /// </remarks>
    [Theory]
    [InlineData(17, 9)]  // end before start — wraps to a positive span
    [InlineData(9, 9)]   // end equal to start — a zero-length day
    public async Task WorkingHoursThatDoNotEndAfterTheyStart_Throws(int startHour, int endHour)
    {
        var settings = SchedulingSettingsInfo.Default(SiteId) with
        {
            WorkingHoursEnabled = true,
            WorkingDayStart = new TimeOnly(startHour, 0),
            WorkingDayEnd = new TimeOnly(endHour, 0),
        };
        var (builder, _) = Build(
            [Leaf([ResourceTypeKeys.Space])], [Resource(ResourceTypeKeys.Space, "Room")], settings);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => builder.BuildAsync(Preview(ResourceTypeKeys.Space), CancellationToken.None));

        thrown.Message.Should().Contain("Working hours are enabled");
    }

    [Fact]
    public async Task OnlyRequestsWantingTheTypeAreScheduled()
    {
        var wantsTool = Leaf([ResourceTypeKeys.Space, "tool"]);
        var wantsSpaceOnly = Leaf([ResourceTypeKeys.Space]);
        var (builder, _) = Build([wantsTool, wantsSpaceOnly], [Resource("tool", "Drill")]);

        var problem = await builder.BuildAsync(Preview("tool"), CancellationToken.None);

        problem.Requests.Should().ContainSingle()
            .Which.RequestId.Should().Be(wantsTool.Id);
    }

    [Fact]
    public async Task RequestsThatAlreadyHaveThatTypeAreLeftAlone()
    {
        // Without this the solver would offer a second drill to a request already holding one.
        var alreadyHasTool = Leaf(
            [ResourceTypeKeys.Space, "tool"],
            [new ResourceAssignmentInfo
            {
                Id = Guid.NewGuid(),
                RequestId = Guid.NewGuid(),
                ResourceId = Guid.NewGuid(),
                ResourceTypeKey = "tool",
                StartUtc = DateTime.UtcNow,
                EndUtc = DateTime.UtcNow.AddDays(1),
                AssignmentStatus = "Active",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            }]);
        var (builder, _) = Build([alreadyHasTool], [Resource("tool", "Drill")]);

        var problem = await builder.BuildAsync(Preview("tool"), CancellationToken.None);

        problem.Requests.Should().BeEmpty();
    }
}
