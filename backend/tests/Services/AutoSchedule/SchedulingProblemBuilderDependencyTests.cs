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

    private static RequestInfo Leaf(Guid id, string name, DateTime? start = null, DateTime? end = null,
        PredecessorLogic logic = PredecessorLogic.All, int? k = null,
        RequestStatus status = RequestStatus.New) => new()
        {
            Id = id,
            Name = name,
            PlanningMode = PlanningMode.Leaf,
            MinimalDurationValue = 1,
            MinimalDurationUnit = DurationUnit.Days,
            Status = status,
            SchedulingSettingsApply = true,
            Assignments = [],
            TargetResourceTypeKeys = [ResourceTypeKeys.Space],
            StartTs = start,
            EndTs = end,
            PredecessorLogic = logic,
            PredecessorLogicK = k,
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
            // Half-open end, as stored: the last worked day is 10 May, so end_ts is 11 May 00:00.
            offHorizon: [Leaf(predId, "Mill", finishedLastMonth, finishedLastMonth.AddDays(1))]);

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
            offHorizon: [Leaf(predId, "Mill", finished, finished.AddDays(1))]);

        var problem = await builder.BuildAsync(Preview(), CancellationToken.None);

        // Finish 10 May, +1 day finish-to-start, +2 days lag.
        problem.Requests.Single(r => r.RequestId == succId).EarliestStart
            .Should().Be(new DateOnly(2026, 5, 13));
    }

    // ── Join conditions ───────────────────────────────────────────────────────
    // The triage runs per successor, because a condition is a property of the whole incoming
    // set. What matters is whether the successor reaches the solver, with what window, and
    // under how many constraints.

    private static readonly DateTime Early = new(2026, 5, 10, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Late = new(2026, 5, 20, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task AnyJoin_TakesTheEarliestPlacedPredecessor()
    {
        Guid early = Guid.NewGuid(), late = Guid.NewGuid(), succ = Guid.NewGuid();

        var builder = Build(
            backlog: [Leaf(succ, "Assemble", logic: PredecessorLogic.Any)],
            edges: [Edge(early, succ), Edge(late, succ)],
            offHorizon:
            [
                Leaf(early, "Supplier A", Early, Early.AddDays(1)),
                Leaf(late, "Supplier B", Late, Late.AddDays(1)),
            ]);

        var problem = await builder.BuildAsync(Preview(), CancellationToken.None);

        // Either delivery unblocks it, so the earlier one governs: 10 May + 1.
        problem.Requests.Single(r => r.RequestId == succ).EarliestStart
            .Should().Be(new DateOnly(2026, 5, 11));
    }

    [Fact]
    public async Task KOfNJoin_TakesTheKthEarliestPlacedPredecessor()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid(), c = Guid.NewGuid(), succ = Guid.NewGuid();
        var middle = new DateTime(2026, 5, 15, 0, 0, 0, DateTimeKind.Utc);

        var builder = Build(
            backlog: [Leaf(succ, "Assemble", logic: PredecessorLogic.KOfN, k: 2)],
            edges: [Edge(a, succ), Edge(b, succ), Edge(c, succ)],
            offHorizon:
            [
                Leaf(a, "A", Early, Early.AddDays(1)),
                Leaf(b, "B", middle, middle.AddDays(1)),
                Leaf(c, "C", Late, Late.AddDays(1)),
            ]);

        var problem = await builder.BuildAsync(Preview(), CancellationToken.None);

        // Two of three: free once the second finishes — 15 May + 1, not 20 May + 1.
        problem.Requests.Single(r => r.RequestId == succ).EarliestStart
            .Should().Be(new DateOnly(2026, 5, 16));
    }

    [Fact]
    public async Task AnyJoin_IsNotBlockedByAnUnplaceablePredecessorWhenAnotherIsPlaced()
    {
        Guid placed = Guid.NewGuid(), unplaceable = Guid.NewGuid(), succ = Guid.NewGuid();

        var builder = Build(
            backlog: [Leaf(succ, "Assemble", logic: PredecessorLogic.Any)],
            edges: [Edge(placed, succ), Edge(unplaceable, succ)],
            // Only the placed one resolves; the other has no end date and is not in this run.
            offHorizon: [Leaf(placed, "Delivered", Early, Early.AddDays(1))]);

        var problem = await builder.BuildAsync(Preview(), CancellationToken.None);

        // Under "all" this successor would be withheld. Under "any" the delivered predecessor
        // already satisfies it, so it schedules.
        problem.Withheld.Should().BeNullOrEmpty();
        problem.Requests.Should().ContainSingle(r => r.RequestId == succ);
    }

    [Fact]
    public async Task AnyJoin_IsWithheldWhenNoPredecessorCanSatisfyIt()
    {
        Guid one = Guid.NewGuid(), two = Guid.NewGuid(), succ = Guid.NewGuid();

        var builder = Build(
            backlog: [Leaf(succ, "Assemble", logic: PredecessorLogic.Any)],
            edges: [Edge(one, succ), Edge(two, succ)],
            offHorizon: []);

        var problem = await builder.BuildAsync(Preview(), CancellationToken.None);

        // "Any" of nothing placeable is still nothing.
        problem.Withheld.Should().ContainSingle(w => w.RequestId == succ);
    }

    [Fact]
    public async Task AllJoin_DoesNotWaitForACancelledPredecessor()
    {
        Guid cancelled = Guid.NewGuid(), live = Guid.NewGuid(), succ = Guid.NewGuid();

        var builder = Build(
            backlog: [Leaf(succ, "Assemble")],
            edges: [Edge(cancelled, succ), Edge(live, succ)],
            offHorizon:
            [
                Leaf(cancelled, "Scrapped", Late, Late.AddDays(1), status: RequestStatus.Cancelled),
                Leaf(live, "Delivered", Early, Early.AddDays(1)),
            ]);

        var problem = await builder.BuildAsync(Preview(), CancellationToken.None);

        // The cancelled predecessor's 20 May finish is ignored; the live one governs.
        problem.Requests.Single(r => r.RequestId == succ).EarliestStart
            .Should().Be(new DateOnly(2026, 5, 11));
    }

    [Fact]
    public async Task EveryPredecessorCancelled_LeavesTheSuccessorUnconstrained()
    {
        Guid cancelled = Guid.NewGuid(), succ = Guid.NewGuid();

        var builder = Build(
            backlog: [Leaf(succ, "Assemble")],
            edges: [Edge(cancelled, succ)],
            offHorizon: [Leaf(cancelled, "Scrapped", Late, Late.AddDays(1), status: RequestStatus.Cancelled)]);

        var problem = await builder.BuildAsync(Preview(), CancellationToken.None);

        // Nothing left to wait for — and crucially not withheld, which is what would happen if
        // an abandoned predecessor still counted towards the condition.
        problem.Withheld.Should().BeNullOrEmpty();
        problem.Requests.Single(r => r.RequestId == succ).EarliestStart.Should().BeNull();
    }

    [Fact]
    public async Task AnyJoin_PrefersACoScheduledPredecessorOverADistantPlacedOne()
    {
        // The regression: the satisfied-by-placed branch used to be entered on a COUNT test, so a
        // single placed predecessor finishing long after the horizon captured the successor and
        // its co-scheduled alternative was discarded — leaving the request withheld. Choosing
        // "any" produced a refusal that "all" would not have.
        Guid distant = Guid.NewGuid(), coScheduled = Guid.NewGuid(), succ = Guid.NewGuid();
        var farFuture = new DateTime(2027, 5, 10, 0, 0, 0, DateTimeKind.Utc);

        var builder = Build(
            backlog: [Leaf(coScheduled, "Supplier B"), Leaf(succ, "Assemble", logic: PredecessorLogic.Any)],
            edges: [Edge(distant, succ), Edge(coScheduled, succ)],
            offHorizon: [Leaf(distant, "Supplier A", farFuture, farFuture.AddDays(1))]);

        var problem = await builder.BuildAsync(Preview(), CancellationToken.None);

        // Scheduled, not withheld — and constrained by the co-scheduled predecessor rather than
        // by a 2027 date, so no bound is carried over from the distant one.
        problem.Withheld.Should().BeNullOrEmpty();
        problem.Requests.Should().ContainSingle(r => r.RequestId == succ);
        problem.Requests.Single(r => r.RequestId == succ).EarliestStart.Should().BeNull();
        problem.Dependencies.Should().ContainSingle(e =>
            e.PredecessorRequestId == coScheduled && e.SuccessorRequestId == succ);
    }

    [Fact]
    public async Task KOfNJoin_BindsOnlyThePlacedPredecessorsTheSolverCannotCover()
    {
        // k=2 of {A placed, B co-scheduled}. Placed work alone cannot reach 2, so the solver edge
        // is kept AND exactly one placed bound is needed — the earliest. Max-folding every placed
        // bound regardless is what used to re-impose a date the join did not require.
        Guid a = Guid.NewGuid(), b = Guid.NewGuid(), succ = Guid.NewGuid();

        var builder = Build(
            backlog: [Leaf(b, "B"), Leaf(succ, "Assemble", logic: PredecessorLogic.KOfN, k: 2)],
            edges: [Edge(a, succ), Edge(b, succ)],
            offHorizon: [Leaf(a, "A", Early, Early.AddDays(1))]);

        var problem = await builder.BuildAsync(Preview(), CancellationToken.None);

        // A's bound binds (10 May + 1) and B stays a solver constraint: k=2 of 2 needs both.
        problem.Requests.Single(r => r.RequestId == succ).EarliestStart
            .Should().Be(new DateOnly(2026, 5, 11));
        problem.Dependencies.Should().ContainSingle(e =>
            e.PredecessorRequestId == b && e.SuccessorRequestId == succ);
    }

    [Fact]
    public async Task KOfNJoin_UsesPlacedWorkAloneWhenItAlreadySatisfiesTheJoin()
    {
        // k=2 of {A placed early, B placed late, C co-scheduled}: the placed pair already meets
        // the requirement inside the window, so the answer is the 2nd-earliest placed bound and
        // C is left unconstrained rather than dragged in.
        Guid a = Guid.NewGuid(), b = Guid.NewGuid(), c = Guid.NewGuid(), succ = Guid.NewGuid();

        var builder = Build(
            backlog: [Leaf(c, "C"), Leaf(succ, "Assemble", logic: PredecessorLogic.KOfN, k: 2)],
            edges: [Edge(a, succ), Edge(b, succ), Edge(c, succ)],
            offHorizon:
            [
                Leaf(a, "A", Early, Early.AddDays(1)),
                Leaf(b, "B", Late, Late.AddDays(1)),
            ]);

        var problem = await builder.BuildAsync(Preview(), CancellationToken.None);

        problem.Requests.Single(r => r.RequestId == succ).EarliestStart
            .Should().Be(new DateOnly(2026, 5, 21));
        problem.Dependencies.Should().NotContain(e => e.SuccessorRequestId == succ);
    }

    [Fact]
    public async Task AnyJoin_SurvivesOnePredecessorBeingBlocked()
    {
        // The blocking walk used to be plain reachability, which re-imposed "all": blocking one
        // predecessor of an "any" successor withheld the successor even though the other one was
        // perfectly placeable.
        Guid doomed = Guid.NewGuid(), healthy = Guid.NewGuid(), missing = Guid.NewGuid(), succ = Guid.NewGuid();

        var builder = Build(
            backlog:
            [
                // `doomed` is itself blocked: its own predecessor is neither placed nor in the run.
                Leaf(doomed, "Doomed"),
                Leaf(healthy, "Healthy"),
                Leaf(succ, "Assemble", logic: PredecessorLogic.Any),
            ],
            edges: [Edge(missing, doomed), Edge(doomed, succ), Edge(healthy, succ)],
            offHorizon: []);

        var problem = await builder.BuildAsync(Preview(), CancellationToken.None);

        problem.Withheld.Should().ContainSingle(w => w.RequestId == doomed);
        problem.Withheld.Should().NotContain(w => w.RequestId == succ);
        problem.Requests.Should().ContainSingle(r => r.RequestId == succ);
    }

    [Fact]
    public async Task AllJoin_IsStillBlockedWhenAPredecessorIs()
    {
        // The control for the test above: under "all" a blocked predecessor must still take its
        // successor down with it, or the run would schedule work with nothing holding it back.
        Guid doomed = Guid.NewGuid(), healthy = Guid.NewGuid(), missing = Guid.NewGuid(), succ = Guid.NewGuid();

        var builder = Build(
            backlog: [Leaf(doomed, "Doomed"), Leaf(healthy, "Healthy"), Leaf(succ, "Assemble")],
            edges: [Edge(missing, doomed), Edge(doomed, succ), Edge(healthy, succ)],
            offHorizon: []);

        var problem = await builder.BuildAsync(Preview(), CancellationToken.None);

        problem.Withheld.Should().Contain(w => w.RequestId == succ);
    }

    [Fact]
    public async Task JoinConditionsTravelWithTheProblem()
    {
        Guid pred = Guid.NewGuid(), succ = Guid.NewGuid();

        var builder = Build(
            backlog: [Leaf(succ, "Assemble", logic: PredecessorLogic.KOfN, k: 2)],
            edges: [Edge(pred, succ)],
            offHorizon: [Leaf(pred, "Delivered", Early, Early.AddDays(1))]);

        var problem = await builder.BuildAsync(Preview(), CancellationToken.None);

        // They are part of the preview's identity, so the fingerprint can see them.
        problem.JoinConditions.Should().NotBeNull();
        problem.JoinConditions![succ].Logic.Should().Be(PredecessorLogic.KOfN);
        problem.JoinConditions![succ].K.Should().Be(2);
    }
}
