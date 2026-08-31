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
        DateTime? start = null, DateTime? end = null, DateTime? latestEnd = null) => new()
        {
            Id = id,
            Name = name,
            PlanningMode = PlanningMode.Leaf,
            Assignments = [],
            TargetResourceTypeKeys = [],
            MinimalDurationValue = durationDays * 24 * 60,
            MinimalDurationUnit = DurationUnit.Minutes,
            Status = RequestStatus.New,
            SchedulingSettingsApply = true,
            StartTs = start,
            EndTs = end,
            LatestEndTs = latestEnd,
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
}
