using Api.Models;
using Api.Services.AutoSchedule;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Api.Tests.Services.AutoSchedule;

/// <summary>
/// The precedence rule, checked against both solvers: a successor never starts before its
/// predecessor has finished plus the lag. The greedy solver matters as much as OR-Tools here —
/// it is the fallback, so a fallback that ignores edges would quietly ship violations.
/// </summary>
public class DependencyPrecedenceTests
{
    private static readonly DateOnly Start = new(2026, 6, 1);
    private static readonly Guid ResourceA = Guid.NewGuid();
    private static readonly Guid Pred = Guid.NewGuid();
    private static readonly Guid Succ = Guid.NewGuid();

    private static AnalyzedSchedulingProblem Problem(
        IReadOnlyList<DependencyEdge> edges,
        int durationDays = 2,
        int horizonDays = 20,
        Guid? successorResource = null)
    {
        var days = Enumerable.Range(0, horizonDays).Select(Start.AddDays).ToList();
        var succResource = successorResource ?? ResourceA;

        var problem = new SchedulingProblem(
            SiteId: Guid.NewGuid(),
            HorizonStart: Start,
            HorizonEnd: Start.AddDays(horizonDays),
            Requests:
            [
                new RequestNode(Pred, "Mill", null, null, durationDays, 0, true, new HashSet<Guid>()),
                new RequestNode(Succ, "Grind", null, null, durationDays, 0, true, new HashSet<Guid>())
            ],
            Resources:
            [
                new ResourceNode(ResourceA, "Cell A", new HashSet<Guid>()),
                new ResourceNode(succResource, "Cell B", new HashSet<Guid>())
            ],
            FixedAssignments: [],
            Settings: null,
            BlockedPeriodsByResource: null,
            Dependencies: edges);

        return new AnalyzedSchedulingProblem(
            problem,
            [
                new SchedulingCandidate(Pred, ResourceA, Start, Start.AddDays(horizonDays), durationDays, 0, days),
                new SchedulingCandidate(Succ, succResource, Start, Start.AddDays(horizonDays), durationDays, 0, days)
            ],
            [],
            []);
    }

    public static TheoryData<ISchedulingSolver> Solvers() => new()
    {
        new GreedySchedulingSolver(),
        new OrToolsSchedulingSolver(NullLogger<OrToolsSchedulingSolver>.Instance),
    };

    [Theory]
    [MemberData(nameof(Solvers))]
    public async Task Successor_NeverStartsBeforePredecessorFinishes(ISchedulingSolver solver)
    {
        // Different resources, so nothing but the edge can separate them in time.
        var problem = Problem([new DependencyEdge(Pred, Succ, LagDays: 0)], successorResource: Guid.NewGuid());

        var solution = await solver.SolveAsync(problem, CancellationToken.None);

        var pred = solution.Assignments.SingleOrDefault(a => a.RequestId == Pred);
        var succ = solution.Assignments.SingleOrDefault(a => a.RequestId == Succ);
        Assert.NotNull(pred);
        Assert.NotNull(succ);

        // Finish-to-start: the successor starts after the predecessor's last occupied day.
        Assert.True(succ!.Start > pred!.End,
            $"{solver.Kind}: successor started {succ.Start} but predecessor ends {pred.End}");
    }

    [Theory]
    [MemberData(nameof(Solvers))]
    public async Task Lag_PushesTheSuccessorFurtherOut(ISchedulingSolver solver)
    {
        const int lag = 3;
        var problem = Problem([new DependencyEdge(Pred, Succ, LagDays: lag)], successorResource: Guid.NewGuid());

        var solution = await solver.SolveAsync(problem, CancellationToken.None);

        var pred = solution.Assignments.Single(a => a.RequestId == Pred);
        var succ = solution.Assignments.Single(a => a.RequestId == Succ);

        Assert.True(succ.Start >= pred.End.AddDays(1 + lag),
            $"{solver.Kind}: lag of {lag} days not honoured (pred ends {pred.End}, succ starts {succ.Start})");
    }

    [Theory]
    [MemberData(nameof(Solvers))]
    public async Task WithoutAnEdge_TheOrderIsUnconstrained(ISchedulingSolver solver)
    {
        // The control: the same shape with no edge places both, and nothing forces an order.
        var problem = Problem([], successorResource: Guid.NewGuid());

        var solution = await solver.SolveAsync(problem, CancellationToken.None);

        Assert.Equal(2, solution.Assignments.Count);
    }

    [Fact]
    public async Task Greedy_UnplaceablePredecessor_LeavesSuccessorWithAReason()
    {
        // One shared resource and a horizon with room for only one placement forces the
        // predecessor to win; the successor must then report why it stayed behind rather than
        // being placed in violation of the edge.
        var days = new List<DateOnly> { Start };
        var problem = new SchedulingProblem(
            SiteId: Guid.NewGuid(),
            HorizonStart: Start,
            HorizonEnd: Start,
            Requests:
            [
                new RequestNode(Pred, "Mill", null, null, 1, 0, true, new HashSet<Guid>()),
                new RequestNode(Succ, "Grind", null, null, 1, 0, true, new HashSet<Guid>())
            ],
            Resources: [new ResourceNode(ResourceA, "Cell A", new HashSet<Guid>())],
            FixedAssignments: [],
            Settings: null,
            BlockedPeriodsByResource: null,
            Dependencies: [new DependencyEdge(Pred, Succ, 0)]);

        var analyzed = new AnalyzedSchedulingProblem(
            problem,
            [
                new SchedulingCandidate(Pred, ResourceA, Start, Start, 1, 0, days),
                new SchedulingCandidate(Succ, ResourceA, Start, Start, 1, 0, days)
            ],
            [],
            []);

        var solution = await new GreedySchedulingSolver().SolveAsync(analyzed, CancellationToken.None);

        Assert.DoesNotContain(solution.Assignments, a => a.RequestId == Succ);
        var unscheduled = solution.Unscheduled.Single(u => u.RequestId == Succ);
        Assert.Contains(SchedulingReasonCode.PredecessorUnscheduled, unscheduled.ReasonCodes);
    }

    [Fact]
    public async Task Greedy_CycleDoesNotDropRequests()
    {
        // The service rejects cycles on write, so one here means the data changed underneath.
        // Placing them in an arbitrary order beats losing them silently.
        var days = Enumerable.Range(0, 10).Select(Start.AddDays).ToList();
        var problem = new SchedulingProblem(
            SiteId: Guid.NewGuid(),
            HorizonStart: Start,
            HorizonEnd: Start.AddDays(10),
            Requests:
            [
                new RequestNode(Pred, "A", null, null, 1, 0, true, new HashSet<Guid>()),
                new RequestNode(Succ, "B", null, null, 1, 0, true, new HashSet<Guid>())
            ],
            Resources: [new ResourceNode(ResourceA, "Cell A", new HashSet<Guid>())],
            FixedAssignments: [],
            Settings: null,
            BlockedPeriodsByResource: null,
            Dependencies:
            [
                new DependencyEdge(Pred, Succ, 0),
                new DependencyEdge(Succ, Pred, 0)
            ]);

        var analyzed = new AnalyzedSchedulingProblem(
            problem,
            [
                new SchedulingCandidate(Pred, ResourceA, Start, Start.AddDays(10), 1, 0, days),
                new SchedulingCandidate(Succ, ResourceA, Start, Start.AddDays(10), 1, 0, days)
            ],
            [],
            []);

        var solution = await new GreedySchedulingSolver().SolveAsync(analyzed, CancellationToken.None);

        var accounted = solution.Assignments.Select(a => a.RequestId)
            .Concat(solution.Unscheduled.Select(u => u.RequestId))
            .ToHashSet();
        Assert.Contains(Pred, accounted);
        Assert.Contains(Succ, accounted);
    }

    [Fact]
    public async Task OrTools_PredecessorWithNoCandidates_KeepsTheSuccessorOut()
    {
        // The predecessor is in the solve set but has no feasible resource, so it can never be
        // placed. Letting the successor through would schedule it ahead of work that never
        // happens — the conditional bound alone is satisfied vacuously.
        var days = Enumerable.Range(0, 5).Select(Start.AddDays).ToList();
        var problem = new SchedulingProblem(
            SiteId: Guid.NewGuid(),
            HorizonStart: Start,
            HorizonEnd: Start.AddDays(5),
            Requests:
            [
                new RequestNode(Pred, "Mill", null, null, 1, 0, true, new HashSet<Guid>()),
                new RequestNode(Succ, "Grind", null, null, 1, 0, true, new HashSet<Guid>())
            ],
            Resources: [new ResourceNode(ResourceA, "Cell A", new HashSet<Guid>())],
            FixedAssignments: [],
            Settings: null,
            BlockedPeriodsByResource: null,
            Dependencies: [new DependencyEdge(Pred, Succ, 0)]);

        // Only the successor has a candidate; the predecessor has none.
        var analyzed = new AnalyzedSchedulingProblem(
            problem,
            [new SchedulingCandidate(Succ, ResourceA, Start, Start.AddDays(5), 1, 0, days)],
            [],
            []);

        var solution = await new OrToolsSchedulingSolver(
            NullLogger<OrToolsSchedulingSolver>.Instance).SolveAsync(analyzed, CancellationToken.None);

        Assert.DoesNotContain(solution.Assignments, a => a.RequestId == Succ);
    }
}
