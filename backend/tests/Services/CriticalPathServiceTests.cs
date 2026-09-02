using Api.Helpers;
using Api.Models;
using Api.Repositories;
using Api.Services;
using Moq;
using Xunit;

namespace Api.Tests.Services;

/// <summary>
/// The forward/backward pass: earliest and latest dates, float, and which requests end up on
/// the critical path. Persistence is mocked — what is under test is the arithmetic.
/// </summary>
public class CriticalPathServiceTests
{
    private readonly Mock<IRequestDependencyRepository> _dependencies = new();
    private readonly Mock<IRequestRepository> _requests = new();
    private readonly CriticalPathService _service;

    private static readonly DateTime Day1 = new(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

    public CriticalPathServiceTests()
    {
        _service = new CriticalPathService(_dependencies.Object, _requests.Object);
    }

    // end is HALF-OPEN, matching what the product stores: a one-day request starting Day1
    // carries end = Day1.AddDays(1). SchedulingEngine.InclusiveLastDay converts it back.
    private static RequestInfo Request(Guid id, string name, int durationDays,
        DateTime? start = null, DateTime? end = null, DateTime? latestEnd = null,
        PredecessorLogic logic = PredecessorLogic.All, int? k = null,
        RequestStatus status = RequestStatus.New) => new()
        {
            Id = id,
            Name = name,
            PlanningMode = PlanningMode.Leaf,
            Assignments = [],
            TargetResourceTypeKeys = [],
            MinimalDurationValue = durationDays * 24 * 60,
            MinimalDurationUnit = DurationUnit.Minutes,
            Status = status,
            SchedulingSettingsApply = true,
            StartTs = start,
            EndTs = end,
            LatestEndTs = latestEnd,
            PredecessorLogic = logic,
            PredecessorLogicK = k,
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

    private void Setup(IEnumerable<RequestDependencyInfo> edges, params RequestInfo[] requests)
    {
        _dependencies.Setup(d => d.GetAllAsync(It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(edges.ToList());
        _requests.Setup(r => r.GetByIdsAsync(It.IsAny<IReadOnlyList<Guid>>(), false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(requests.ToList());
    }

    [Fact]
    public async Task NoEdges_ReturnsAnEmptyNetwork()
    {
        _dependencies.Setup(d => d.GetAllAsync(It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await _service.ComputeAsync(null);

        Assert.Empty(result.Nodes);
        Assert.Equal(0, result.DurationDays);
    }

    [Fact]
    public async Task Chain_EachStepStartsAfterTheOneBefore()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid(), c = Guid.NewGuid();
        Setup([Edge(a, b), Edge(b, c)],
            Request(a, "A", 2, start: Day1, end: Day1.AddDays(2)),
            Request(b, "B", 3),
            Request(c, "C", 1));

        var result = await _service.ComputeAsync(null);

        var nodeB = result.Nodes.Single(n => n.RequestId == b);
        var nodeC = result.Nodes.Single(n => n.RequestId == c);

        // A is anchored 1–2 June, so B starts the 3rd and runs three days to the 5th.
        Assert.Equal(new DateOnly(2026, 6, 3), nodeB.EarliestStart);
        Assert.Equal(new DateOnly(2026, 6, 5), nodeB.EarliestFinish);
        Assert.Equal(new DateOnly(2026, 6, 6), nodeC.EarliestStart);

        // A single chain has no slack anywhere.
        Assert.All(result.Nodes, n => Assert.True(n.IsCritical));
        Assert.Equal(6, result.DurationDays); // 1–6 June inclusive
    }

    [Fact]
    public async Task Lag_DelaysTheSuccessor()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid();
        Setup([Edge(a, b, lagMinutes: 2 * 24 * 60)],
            Request(a, "A", 1, start: Day1, end: Day1.AddDays(1)),
            Request(b, "B", 1));

        var result = await _service.ComputeAsync(null);

        // A ends 1 June; +1 day for finish-to-start, +2 days lag.
        Assert.Equal(new DateOnly(2026, 6, 4), result.Nodes.Single(n => n.RequestId == b).EarliestStart);
    }

    [Fact]
    public async Task Diamond_TheShorterBranchCarriesFloat()
    {
        // start → (long | short) → join. The long branch decides the finish; the short one can
        // slip by the difference without moving anything.
        Guid start = Guid.NewGuid(), longBranch = Guid.NewGuid(), shortBranch = Guid.NewGuid(), join = Guid.NewGuid();
        Setup(
            [Edge(start, longBranch), Edge(start, shortBranch), Edge(longBranch, join), Edge(shortBranch, join)],
            Request(start, "Start", 1, start: Day1, end: Day1.AddDays(1)),
            Request(longBranch, "Long", 5),
            Request(shortBranch, "Short", 2),
            Request(join, "Join", 1));

        var result = await _service.ComputeAsync(null);

        Assert.True(result.Nodes.Single(n => n.RequestId == longBranch).IsCritical);
        Assert.False(result.Nodes.Single(n => n.RequestId == shortBranch).IsCritical);

        // Five days of work against two leaves three days of slack.
        Assert.Equal(3, result.Nodes.Single(n => n.RequestId == shortBranch).TotalFloatDays);
        Assert.Equal(0, result.Nodes.Single(n => n.RequestId == longBranch).TotalFloatDays);
    }

    [Fact]
    public async Task ScheduledDatesAreAnchors_NotEstimates()
    {
        // B is already placed well after its predecessor allows. The pass reports where it
        // actually is, because a placement is a fact about the plan.
        Guid a = Guid.NewGuid(), b = Guid.NewGuid();
        var placedStart = Day1.AddDays(10);
        Setup([Edge(a, b)],
            Request(a, "A", 1, start: Day1, end: Day1.AddDays(1)),
            Request(b, "B", 1, start: placedStart, end: placedStart.AddDays(1)));

        var result = await _service.ComputeAsync(null);

        Assert.Equal(DateOnly.FromDateTime(placedStart), result.Nodes.Single(n => n.RequestId == b).EarliestStart);
        Assert.True(result.Nodes.Single(n => n.RequestId == b).IsScheduled);
    }

    [Fact]
    public async Task ADeadlineTightensFloat()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid();
        Setup([Edge(a, b)],
            Request(a, "A", 1, start: Day1, end: Day1.AddDays(1)),
            // Due the day it can first run: no room to move.
            Request(b, "B", 1, latestEnd: Day1.AddDays(1)));

        var result = await _service.ComputeAsync(null);

        Assert.Equal(0, result.Nodes.Single(n => n.RequestId == b).TotalFloatDays);
    }

    [Fact]
    public async Task Cycle_IsRejectedRatherThanLooping()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid();
        Setup([Edge(a, b), Edge(b, a)],
            Request(a, "A", 1),
            Request(b, "B", 1));

        await Assert.ThrowsAsync<ConflictException>(() => _service.ComputeAsync(null));
    }

    [Fact]
    public async Task EdgesReachingOutsideTheScope_AreExcludedAndReported()
    {
        // A site filter follows the successor, so a predecessor elsewhere is not in the read.
        // Inventing dates for it would be worse than saying so.
        Guid a = Guid.NewGuid(), b = Guid.NewGuid();
        Setup([Edge(a, b)], Request(b, "B", 1, start: Day1, end: Day1.AddDays(1)));

        var result = await _service.ComputeAsync(Guid.NewGuid());

        Assert.Empty(result.Nodes);
        Assert.Single(result.Diagnostics);
        Assert.Contains("outside this scope", result.Diagnostics[0]);
    }

    // ── Join conditions ───────────────────────────────────────────────────────
    // Two predecessors finishing on different days, one successor. Which day the successor may
    // start is the whole question, and each logic answers it differently.

    private (Guid Early, Guid Late, Guid Succ, RequestInfo[] Requests, RequestDependencyInfo[] Edges)
        TwoPredecessors(PredecessorLogic logic, int? k = null)
    {
        Guid early = Guid.NewGuid(), late = Guid.NewGuid(), succ = Guid.NewGuid();
        return (early, late, succ,
            [
                // Anchored so the forward pass has fixed finishes to fold: 1 day from Day1, and
                // 5 days from Day1 — so "any" frees the successor four days before "all" does.
                Request(early, "Early", 1, Day1, Day1.AddDays(1)),
                Request(late, "Late", 5, Day1, Day1.AddDays(5)),
                Request(succ, "Successor", 1, logic: logic, k: k),
            ],
            [Edge(early, succ), Edge(late, succ)]);
    }

    private DateOnly EarliestStartOf(CriticalPathResult result, Guid id) =>
        result.Nodes.Single(n => n.RequestId == id).EarliestStart;

    [Fact]
    public async Task AllJoin_WaitsForTheLatestPredecessor()
    {
        var (_, _, succ, requests, edges) = TwoPredecessors(PredecessorLogic.All);
        Setup(edges, requests);

        var result = await _service.ComputeAsync(null);

        // Day1 + 5 days, then the day after: the late predecessor governs.
        EarliestStartOf(result, succ).Should().Be(DateOnly.FromDateTime(Day1.AddDays(5)));
    }

    [Fact]
    public async Task AnyJoin_StartsAfterTheEarliestPredecessor()
    {
        var (_, _, succ, requests, edges) = TwoPredecessors(PredecessorLogic.Any);
        Setup(edges, requests);

        var result = await _service.ComputeAsync(null);

        // One predecessor is enough, so the early one frees it four days sooner.
        EarliestStartOf(result, succ).Should().Be(DateOnly.FromDateTime(Day1.AddDays(1)));
    }

    [Fact]
    public async Task KOfNJoin_StartsAfterTheKthPredecessor()
    {
        var (_, _, succ, requests, edges) = TwoPredecessors(PredecessorLogic.KOfN, k: 2);
        Setup(edges, requests);

        var result = await _service.ComputeAsync(null);

        // 2 of 2 is "all" by another name.
        EarliestStartOf(result, succ).Should().Be(DateOnly.FromDateTime(Day1.AddDays(5)));
    }

    [Fact]
    public async Task KOfNJoin_WithKOfOne_MatchesAny()
    {
        var (_, _, succ, requests, edges) = TwoPredecessors(PredecessorLogic.KOfN, k: 1);
        Setup(edges, requests);

        var result = await _service.ComputeAsync(null);

        EarliestStartOf(result, succ).Should().Be(DateOnly.FromDateTime(Day1.AddDays(1)));
    }

    [Fact]
    public async Task NonAllJoin_ExplainsThatASkippedPredecessorCarriesFloat()
    {
        var (_, _, _, requests, edges) = TwoPredecessors(PredecessorLogic.Any);
        Setup(edges, requests);

        var result = await _service.ComputeAsync(null);

        result.Diagnostics.Should().ContainSingle(d => d.Contains("float"));
    }

    [Fact]
    public async Task AnyJoin_GivesTheSlackPredecessorFloatRatherThanMarkingItCritical()
    {
        // The regression: the backward pass folded against EVERY successor, so the branch an
        // "any" join never waited for was pulled back to the binding branch's dates — producing
        // NEGATIVE float and IsCritical on a request nothing is waiting for. The Bottlenecks
        // table would then point the planner at the one task with slack to spare.
        Guid quick = Guid.NewGuid(), slow = Guid.NewGuid(), succ = Guid.NewGuid(), tail = Guid.NewGuid();
        Setup(
            [Edge(quick, succ), Edge(slow, succ), Edge(succ, tail)],
            // Both start on Day1; the quick one finishes in a day, the slow one in ten. The tail
            // keeps the project finish downstream of the join, so the slack branch is genuinely
            // slack rather than the thing that ends last.
            Request(quick, "Quick", 1, Day1, Day1.AddDays(1)),
            Request(slow, "Slow", 10, Day1, Day1.AddDays(10)),
            Request(succ, "Successor", 1, logic: PredecessorLogic.Any),
            Request(tail, "Tail", 20));

        var result = await _service.ComputeAsync(null);

        // "Any" is satisfied by the quick branch, so the slow one carries real float.
        var slowNode = result.Nodes.Single(n => n.RequestId == slow);
        slowNode.TotalFloatDays.Should().BeGreaterThan(0);
        slowNode.IsCritical.Should().BeFalse();

        // …and the branch the join actually waited for is still on the path.
        result.Nodes.Single(n => n.RequestId == quick).IsCritical.Should().BeTrue();
    }

    [Fact]
    public async Task AllJoin_KeepsEveryPredecessorOnTheCriticalPath()
    {
        // The control: under "all" both branches bind, so the backward pass is unchanged and the
        // long branch is critical exactly as before.
        Guid quick = Guid.NewGuid(), slow = Guid.NewGuid(), succ = Guid.NewGuid();
        Setup(
            [Edge(quick, succ), Edge(slow, succ)],
            Request(quick, "Quick", 1, Day1, Day1.AddDays(1)),
            Request(slow, "Slow", 10, Day1, Day1.AddDays(10)),
            Request(succ, "Successor", 1));

        var result = await _service.ComputeAsync(null);

        result.Nodes.Single(n => n.RequestId == slow).IsCritical.Should().BeTrue();
    }

    [Fact]
    public async Task AllJoin_DoesNotWaitForACancelledPredecessor()
    {
        Guid cancelled = Guid.NewGuid(), running = Guid.NewGuid(), succ = Guid.NewGuid();
        Setup(
            [Edge(cancelled, succ), Edge(running, succ)],
            Request(cancelled, "Scrapped", 5, Day1, Day1.AddDays(5), status: RequestStatus.Cancelled),
            Request(running, "Live", 1, Day1, Day1.AddDays(1)),
            Request(succ, "Successor", 1));

        var result = await _service.ComputeAsync(null);

        // Without the exclusion the successor would wait five days for work that will never run.
        EarliestStartOf(result, succ).Should().Be(DateOnly.FromDateTime(Day1.AddDays(1)));
        result.Diagnostics.Should().ContainSingle(d => d.Contains("cancelled or deferred"));
    }
}
