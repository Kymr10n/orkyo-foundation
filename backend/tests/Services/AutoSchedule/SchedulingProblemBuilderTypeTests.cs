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
/// One run fills every type in its set. These cover how the builder turns the run's type set
/// into a pool, which requests join the run and with which types open, how a criterion's type
/// scope partitions the requirements, and how an already-chosen window is pinned.
/// </summary>
public class SchedulingProblemBuilderTypeTests
{
    private static readonly Guid SiteId = Guid.NewGuid();
    private static readonly DateOnly HorizonStart = new(2026, 6, 1);
    private static readonly DateOnly HorizonEnd = new(2026, 6, 30);

    private static RequestInfo Leaf(string[] targets, ResourceAssignmentInfo[]? assignments = null,
        DateTime? start = null, DateTime? end = null, List<RequestRequirementInfo>? requirements = null) => new()
        {
            Id = Guid.NewGuid(),
            Name = "Leaf",
            PlanningMode = PlanningMode.Leaf,
            MinimalDurationValue = 3,
            MinimalDurationUnit = DurationUnit.Hours,
            Status = RequestStatus.New,
            SchedulingSettingsApply = true,
            Assignments = assignments ?? [],
            TargetResourceTypeKeys = targets,
            StartTs = start,
            EndTs = end,
            Requirements = requirements,
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

    private static ResourceAssignmentInfo Assignment(string typeKey, Guid resourceId, DateTime start, DateTime end) => new()
    {
        Id = Guid.NewGuid(),
        RequestId = Guid.NewGuid(),
        ResourceId = resourceId,
        ResourceTypeKey = typeKey,
        StartUtc = start,
        EndUtc = end,
        AssignmentStatus = "Active",
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
    };

    /// <summary>A requirement as the repository loads it: the criterion's scope rides along.</summary>
    private static RequestRequirementInfo Requirement(Guid criterionId, params string[] typeKeys) => new()
    {
        Id = Guid.NewGuid(),
        RequestId = Guid.NewGuid(),
        CriterionId = criterionId,
        Value = System.Text.Json.JsonDocument.Parse("true").RootElement,
        Criterion = new CriterionBasicInfo
        {
            Id = criterionId,
            Name = "crit",
            DataType = CriterionDataType.Boolean,
            ResourceTypeKeys = typeKeys,
        },
    };

    /// <summary>Wires the builder with the given backlog and candidate pool; captures the filters.</summary>
    private static (SchedulingProblemBuilder Builder, List<ResourceListFilter> Filters) Build(
        List<RequestInfo> backlog, List<ResourceInfo> candidates,
        SchedulingSettingsInfo? settings = null)
    {
        var filters = new List<ResourceListFilter>();

        var scheduleReads = new Mock<IRequestScheduleReadRepository>();
        scheduleReads.Setup(r => r.GetUnscheduledAsync(
                It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(backlog.Where(r => r.StartTs is null).ToList());
        scheduleReads.Setup(r => r.GetPartiallyScheduledLeavesAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(backlog.Where(r => r.StartTs is not null).ToList());
        scheduleReads.Setup(r => r.GetScheduledBySiteWindowAsync(
                It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var resources = new Mock<IResourceRepository>();
        resources.Setup(r => r.GetEveryAsync(It.IsAny<ResourceListFilter>(), It.IsAny<CancellationToken>()))
            .Callback((ResourceListFilter f, CancellationToken _) => filters.Add(f))
            .ReturnsAsync((ResourceListFilter f, CancellationToken _) =>
                candidates.Where(c => c.ResourceTypeKey == f.ResourceTypeKey).ToList());

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

        var dependencies = new Mock<IRequestDependencyRepository>();
        dependencies.Setup(d => d.GetBySuccessorsAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        return (new SchedulingProblemBuilder(
            Mock.Of<IRequestRepository>(), scheduleReads.Object, resources.Object, capabilities.Object,
            scheduling.Object, resolver.Object, dependencies.Object,
            TimeProvider.System), filters);
    }

    private static AutoSchedulePreviewRequest Preview(params string[] typeKeys) => new(
        SiteId, HorizonStart, HorizonEnd,
        ResourceTypeKeys: typeKeys.Length == 0 ? null : typeKeys);

    [Fact]
    public async Task PoolIsReadOncePerTypeAndTyped()
    {
        var (builder, filters) = Build(
            [Leaf([ResourceTypeKeys.Space, "tool"])],
            [Resource("tool", "Drill"), Resource(ResourceTypeKeys.Space, "Room")]);

        var problem = await builder.BuildAsync(Preview("tool", ResourceTypeKeys.Space), CancellationToken.None);

        // One read per type, in key order, site-scoped, active only.
        filters.Select(f => f.ResourceTypeKey).Should().Equal(ResourceTypeKeys.Space, "tool");
        filters.Should().OnlyContain(f => f.SiteId == SiteId && f.IsActive == true);
        problem.Resources.Select(r => r.ResourceTypeKey).Should().BeEquivalentTo([ResourceTypeKeys.Space, "tool"]);
    }

    [Fact]
    public async Task OmittingTheTypes_Throws_ResolutionBelongsToTheService()
    {
        // AutoScheduleService resolves the set before building, so an empty set reaching this
        // far is a programming error and must say so rather than quietly solving for nothing.
        var (builder, _) = Build([], []);

        await Assert.ThrowsAsync<ArgumentException>(
            () => builder.BuildAsync(Preview(), CancellationToken.None));
    }

    /// <summary>
    /// SchedulingValidators rejects end &lt;= start when the settings are written, so a site
    /// that reaches the builder in this state has corrupt stored data. Inventing a working
    /// day would silently schedule against a length nobody configured.
    /// </summary>
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
    public async Task OnlyRequestsWantingARunTypeAreScheduled_WithExactlyThoseTypesOpen()
    {
        var wantsTool = Leaf([ResourceTypeKeys.Space, "tool"]);
        var wantsSpaceOnly = Leaf([ResourceTypeKeys.Space]);
        var (builder, _) = Build([wantsTool, wantsSpaceOnly], [Resource("tool", "Drill")]);

        var problem = await builder.BuildAsync(Preview("tool"), CancellationToken.None);

        var node = problem.Requests.Should().ContainSingle().Subject;
        node.RequestId.Should().Be(wantsTool.Id);
        // The room is not this run's business; only the tool is open.
        node.OpenTypeKeys.Should().BeEquivalentTo(["tool"]);
    }

    [Fact]
    public async Task ARequestNeedingTwoRunTypes_HasBothOpen()
    {
        var both = Leaf([ResourceTypeKeys.Space, "tool"]);
        var (builder, _) = Build([both], [Resource("tool", "Drill"), Resource(ResourceTypeKeys.Space, "Room")]);

        var problem = await builder.BuildAsync(Preview("tool", ResourceTypeKeys.Space), CancellationToken.None);

        problem.Requests.Single().OpenTypeKeys.Should().BeEquivalentTo([ResourceTypeKeys.Space, "tool"]);
    }

    [Fact]
    public async Task RequestsThatAlreadyHaveThatTypeAreLeftAlone()
    {
        // Without this the solver would offer a second drill to a request already holding one.
        var alreadyHasTool = Leaf(
            [ResourceTypeKeys.Space, "tool"],
            [Assignment("tool", Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow.AddDays(1))]);
        var (builder, _) = Build([alreadyHasTool], [Resource("tool", "Drill")]);

        var problem = await builder.BuildAsync(Preview("tool"), CancellationToken.None);

        problem.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task AWindowAlreadyChosen_IsPinned()
    {
        // The room is booked 3 June 09:00–12:00 and the tool is still open: the run fills the
        // tool at exactly that window rather than moving the room.
        var room = Guid.NewGuid();
        var start = new DateTime(2026, 6, 3, 9, 0, 0, DateTimeKind.Utc);
        var end = start.AddHours(3);
        var partly = Leaf(
            [ResourceTypeKeys.Space, "tool"],
            [Assignment(ResourceTypeKeys.Space, room, start, end)],
            start, end);
        var (builder, _) = Build([partly], [Resource("tool", "Drill")]);

        var problem = await builder.BuildAsync(Preview("tool"), CancellationToken.None);

        var node = problem.Requests.Single();
        var offset = 2 * 1440 + 9 * 60;
        node.EarliestStart.Should().Be(offset);
        node.LatestEnd.Should().Be(offset + 180);
        node.DurationMinutes.Should().Be(180);
    }

    [Fact]
    public async Task AWindowOutsideTheHorizon_KeepsTheRequestOutOfTheRun()
    {
        // Pinned onto this horizon's axis, a window from last month would be dragged into it.
        var room = Guid.NewGuid();
        var start = new DateTime(2026, 5, 3, 9, 0, 0, DateTimeKind.Utc);
        var partly = Leaf(
            [ResourceTypeKeys.Space, "tool"],
            [Assignment(ResourceTypeKeys.Space, room, start, start.AddHours(3))],
            start, start.AddHours(3));
        var (builder, _) = Build([partly], [Resource("tool", "Drill")]);

        var problem = await builder.BuildAsync(Preview("tool"), CancellationToken.None);

        problem.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task CriterionTypeScopes_TravelWithTheProblem()
    {
        // Each required criterion carries the types it applies to, read off the loaded
        // requirements — so the analyzer can demand the mill's tolerance of the mill, not the van.
        var millOnly = Guid.NewGuid();
        var unscoped = Guid.NewGuid();
        var request = Leaf(["mill", "van"], requirements:
            [Requirement(millOnly, "mill"), Requirement(unscoped) with { Criterion = null }]);
        var (builder, _) = Build([request], [Resource("mill", "Mill"), Resource("van", "Van")]);

        var problem = await builder.BuildAsync(Preview("mill", "van"), CancellationToken.None);

        problem.CriterionTypeScopes.Should().ContainKey(millOnly)
            .WhoseValue.Should().BeEquivalentTo(["mill"]);
        // No criterion joined → no scope recorded; the analyzer then demands it of every type.
        problem.CriterionTypeScopes.Should().NotContainKey(unscoped);
    }

    [Fact]
    public async Task RequestIds_NarrowTheRunToTheNamedRequests()
    {
        var wanted = Leaf(["tool"]);
        var other = Leaf(["tool"]);
        var (builder, _) = Build([wanted, other], [Resource("tool", "Drill")]);

        var problem = await builder.BuildAsync(
            new AutoSchedulePreviewRequest(SiteId, HorizonStart, HorizonEnd, RequestIds: [wanted.Id], ResourceTypeKeys: ["tool"]),
            CancellationToken.None);

        problem.Requests.Select(r => r.RequestId).Should().Equal(wanted.Id);
    }
}
