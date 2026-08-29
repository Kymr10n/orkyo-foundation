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
/// How the builder classifies a precedence edge, which decides whether the solver sees a
/// constraint, a tightened window, or nothing at all because the successor was withheld.
///
/// The interesting cases are all about predecessors the run cannot move: already finished,
/// finished before the horizon, or genuinely unplaceable.
/// </summary>
public class SchedulingProblemBuilderDependencyTests
{
    private static readonly Guid SiteId = Guid.NewGuid();
    private static readonly DateOnly HorizonStart = new(2026, 6, 1);
    private static readonly DateOnly HorizonEnd = new(2026, 6, 30);

    private static RequestInfo Leaf(Guid id, string name, DateTime? start = null, DateTime? end = null) => new()
    {
        Id = id,
        Name = name,
        PlanningMode = PlanningMode.Leaf,
        MinimalDurationValue = 1,
        MinimalDurationUnit = DurationUnit.Days,
        Status = RequestStatus.New,
        SchedulingSettingsApply = true,
        Assignments = [],
        TargetResourceTypeKeys = [ResourceTypeKeys.Space],
        StartTs = start,
        EndTs = end,
    };

    private static ResourceInfo Space() => new()
    {
        Id = Guid.NewGuid(),
        ResourceTypeId = Guid.NewGuid(),
        ResourceTypeKey = ResourceTypeKeys.Space,
        Name = "Cell",
        AllocationMode = AllocationModes.Exclusive,
        BaseAvailabilityPercent = 100,
        IsActive = true,
        CrossSiteAllowed = true,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
    };

    private static RequestDependencyInfo Edge(Guid pred, Guid succ, int lagMinutes = 0) => new()
    {
        Id = Guid.NewGuid(),
        PredecessorRequestId = pred,
        SuccessorRequestId = succ,
        PredecessorName = "pred",
        SuccessorName = "succ",
        DependencyType = DependencyTypes.FinishToStart,
        LagMinutes = lagMinutes,
    };

    /// <param name="offHorizon">
    /// Requests the run cannot see through its site+horizon window, resolved only by id — this
    /// is where a predecessor that finished last month lives.
    /// </param>
    private static SchedulingProblemBuilder Build(
        List<RequestInfo> backlog,
        List<RequestDependencyInfo> edges,
        List<RequestInfo>? offHorizon = null)
    {
        var requests = new Mock<IRequestRepository>();
        requests.Setup(r => r.GetUnscheduledAsync(
                It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(backlog);
        requests.Setup(r => r.GetPartiallyScheduledLeavesAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        requests.Setup(r => r.GetScheduledBySiteWindowAsync(
                It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        requests.Setup(r => r.GetByIdsAsync(
                It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<Guid> ids, bool _, CancellationToken _) =>
                (offHorizon ?? []).Where(r => ids.Contains(r.Id)).ToList());

        var resources = new Mock<IResourceRepository>();
        resources.Setup(r => r.GetAllAsync(It.IsAny<ResourceListFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Space()]);
        resources.Setup(r => r.GetEveryAsync(It.IsAny<ResourceListFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Space()]);

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
            .ReturnsAsync(edges);

        return new SchedulingProblemBuilder(
            requests.Object, resources.Object, capabilities.Object,
            scheduling.Object, resolver.Object, dependencies.Object);
    }

    private static AutoSchedulePreviewRequest Preview() =>
        new(SiteId, HorizonStart, HorizonEnd, ResourceTypeKey: ResourceTypeKeys.Space);

    [Fact]
    public async Task PredecessorFinishedBeforeTheHorizon_BoundsTheSuccessorInsteadOfWithholdingIt()
    {
        // The commonest real case, and the one the first implementation got wrong: the
        // prerequisite is already done, but it finished last month so the site+horizon fetch
        // never sees it. Withholding the successor would refuse to schedule work whose
        // prerequisite is complete.
        var predId = Guid.NewGuid();
        var succId = Guid.NewGuid();
        var finishedLastMonth = new DateTime(2026, 5, 10, 0, 0, 0, DateTimeKind.Utc);

        var builder = Build(
            backlog: [Leaf(succId, "Grind")],
            edges: [Edge(predId, succId)],
            offHorizon: [Leaf(predId, "Mill", finishedLastMonth, finishedLastMonth)]);

        var problem = await builder.BuildAsync(Preview(), CancellationToken.None);

        var successor = problem.Requests.Should().ContainSingle(r => r.RequestId == succId).Subject;

        // Bounded by the predecessor's finish, not dropped.
        successor.EarliestStart.Should().Be(new DateOnly(2026, 5, 11));
        problem.Dependencies.Should().BeEmpty("the predecessor is fixed, so there is nothing for the solver to order");
    }

    [Fact]
    public async Task PredecessorWithNoEndDate_WithholdsTheSuccessor()
    {
        // Not in the run and never scheduled: there is no date to bound against, so placing the
        // successor would knowingly create a violation.
        var predId = Guid.NewGuid();
        var succId = Guid.NewGuid();

        var builder = Build(
            backlog: [Leaf(succId, "Grind")],
            edges: [Edge(predId, succId)],
            offHorizon: [Leaf(predId, "Mill")]);

        var problem = await builder.BuildAsync(Preview(), CancellationToken.None);

        problem.Requests.Should().NotContain(r => r.RequestId == succId);
    }

    [Fact]
    public async Task BothInTheRun_BecomesASolverConstraint()
    {
        var predId = Guid.NewGuid();
        var succId = Guid.NewGuid();

        var builder = Build(
            backlog: [Leaf(predId, "Mill"), Leaf(succId, "Grind")],
            edges: [Edge(predId, succId)]);

        var problem = await builder.BuildAsync(Preview(), CancellationToken.None);

        problem.Requests.Should().HaveCount(2);
        problem.Dependencies.Should().ContainSingle(e =>
            e.PredecessorRequestId == predId && e.SuccessorRequestId == succId);
    }

    [Fact]
    public async Task WithholdingTravelsDownstream()
    {
        // A blocks B blocks C. Dropping B without dropping C would leave C scheduled with
        // nothing holding it back — a violation of the very edge just deemed unenforceable.
        var blockedPredecessor = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();

        var builder = Build(
            backlog: [Leaf(b, "B"), Leaf(c, "C")],
            edges: [Edge(blockedPredecessor, b), Edge(b, c)],
            offHorizon: [Leaf(blockedPredecessor, "A")]);

        var problem = await builder.BuildAsync(Preview(), CancellationToken.None);

        problem.Requests.Should().NotContain(r => r.RequestId == b);
        problem.Requests.Should().NotContain(r => r.RequestId == c, "blocking travels along the chain");
        problem.Dependencies.Should().BeEmpty();
    }

    [Fact]
    public async Task WithheldRequestsAreReportedRatherThanDropped()
    {
        // Removing them from the solve set is right; removing them from the answer is not. The
        // caller selected them, so a run that returns fewer requests than it was given has to say
        // which ones and why.
        var predId = Guid.NewGuid();
        var succId = Guid.NewGuid();

        var builder = Build(
            backlog: [Leaf(succId, "Grind")],
            edges: [Edge(predId, succId)],
            offHorizon: [Leaf(predId, "Mill")]);

        var problem = await builder.BuildAsync(Preview(), CancellationToken.None);

        problem.Requests.Should().NotContain(r => r.RequestId == succId);
        problem.Withheld.Should().ContainSingle(w => w.RequestId == succId)
            .Which.DisplayName.Should().Be("Grind", "the name has to survive the filter to be reportable");
    }

    [Fact]
    public async Task APredecessorFinishingTooLateWithholdsInsteadOfSettingAnImpossibleWindow()
    {
        // The predecessor ends after the successor's own deadline. Folding that bound in anyway
        // leaves a window nothing can satisfy, and the feasibility analyzer then reports it as a
        // capacity problem — sending the reader to look at resource load for a dependency issue.
        var predId = Guid.NewGuid();
        var succId = Guid.NewGuid();
        var finishesLate = new DateTime(2026, 6, 25, 0, 0, 0, DateTimeKind.Utc);

        var successor = Leaf(succId, "Grind") with { LatestEndTs = new DateTime(2026, 6, 10, 0, 0, 0, DateTimeKind.Utc) };

        var builder = Build(
            backlog: [successor],
            edges: [Edge(predId, succId)],
            offHorizon: [Leaf(predId, "Mill", finishesLate, finishesLate)]);

        var problem = await builder.BuildAsync(Preview(), CancellationToken.None);

        problem.Requests.Should().NotContain(r => r.RequestId == succId);
        problem.Withheld.Should().ContainSingle(w => w.RequestId == succId);
    }

    [Fact]
    public async Task LagPushesTheBoundFurtherOut()
    {
        var predId = Guid.NewGuid();
        var succId = Guid.NewGuid();
        var finished = new DateTime(2026, 5, 10, 0, 0, 0, DateTimeKind.Utc);

        var builder = Build(
            backlog: [Leaf(succId, "Grind")],
            edges: [Edge(predId, succId, lagMinutes: 2 * 24 * 60)],
            offHorizon: [Leaf(predId, "Mill", finished, finished)]);

        var problem = await builder.BuildAsync(Preview(), CancellationToken.None);

        // Finish 10 May, +1 day finish-to-start, +2 days lag.
        problem.Requests.Single(r => r.RequestId == succId).EarliestStart
            .Should().Be(new DateOnly(2026, 5, 13));
    }
}
