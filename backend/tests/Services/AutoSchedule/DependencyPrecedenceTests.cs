using Api.Models;
using Api.Services.AutoSchedule;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static Orkyo.Foundation.Tests.Services.AutoSchedule.AutoScheduleTestHelpers;

namespace Api.Tests.Services.AutoSchedule;

/// <summary>
/// The precedence rule, checked against both solvers: a successor never starts before its
/// predecessor has finished plus the lag. The greedy solver matters as much as OR-Tools here —
/// it is the fallback, so a fallback that ignores edges would quietly ship violations.
/// </summary>
public class DependencyPrecedenceTests
{
    private static readonly Guid ResourceA = Guid.NewGuid();
    private static readonly Guid Pred = Guid.NewGuid();
    private static readonly Guid Succ = Guid.NewGuid();

    private static AnalyzedSchedulingProblem Problem(
        IReadOnlyList<DependencyEdge> edges,
        int durationMinutes = 120,
        int horizonMinutes = 20 * 1440,
        Guid? successorResource = null)
    {
        var window = new StartWindow(0, horizonMinutes - durationMinutes + 1);
        var succResource = successorResource ?? ResourceA;

        var problem = MakeProblem(
            requests:
            [
                MakeRequest(Pred, "Mill", durationMinutes, priority: 0),
                MakeRequest(Succ, "Grind", durationMinutes, priority: 0)
            ],
            spaces:
            [
                MakeSpace(ResourceA, "Cell A"),
                MakeSpace(succResource, "Cell B")
            ],
            dependencies: edges);

        return new AnalyzedSchedulingProblem(
            problem,
            [
                new SchedulingCandidate(Pred, ResourceA, Space, 0, horizonMinutes, durationMinutes, 0, [window]),
                new SchedulingCandidate(Succ, succResource, Space, 0, horizonMinutes, durationMinutes, 0, [window])
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
        var problem = Problem([new DependencyEdge(Pred, Succ, LagMinutes: 0)], successorResource: Guid.NewGuid());

        var solution = await solver.SolveAsync(problem, CancellationToken.None);

        var pred = solution.Assignments.SingleOrDefault(a => a.RequestId == Pred);
        var succ = solution.Assignments.SingleOrDefault(a => a.RequestId == Succ);
        Assert.NotNull(pred);
        Assert.NotNull(succ);

        // Finish-to-start: the successor starts once the predecessor is done — and, since both
        // prefer an early start, exactly then.
        Assert.Equal(pred!.End, succ!.Start);
    }

    [Theory]
    [MemberData(nameof(Solvers))]
    public async Task Lag_PushesTheSuccessorFurtherOut(ISchedulingSolver solver)
    {
        const int lag = 240;
        var problem = Problem([new DependencyEdge(Pred, Succ, LagMinutes: lag)], successorResource: Guid.NewGuid());

        var solution = await solver.SolveAsync(problem, CancellationToken.None);

        var pred = solution.Assignments.Single(a => a.RequestId == Pred);
        var succ = solution.Assignments.Single(a => a.RequestId == Succ);

        Assert.True(succ.Start >= pred.End + lag,
            $"{solver.Kind}: lag of {lag} minutes not honoured (pred ends {pred.End}, succ starts {succ.Start})");
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
        var problem = MakeProblem(
            requests: [MakeRequest(Pred, "Mill", 60, priority: 0), MakeRequest(Succ, "Grind", 60, priority: 0)],
            spaces: [MakeSpace(ResourceA, "Cell A")],
            dependencies: [new DependencyEdge(Pred, Succ, 0)]);

        var analyzed = new AnalyzedSchedulingProblem(
            problem,
            [
                new SchedulingCandidate(Pred, ResourceA, Space, 0, 60, 60, 0, [new StartWindow(0, 1)]),
                new SchedulingCandidate(Succ, ResourceA, Space, 0, 60, 60, 0, [new StartWindow(0, 1)])
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
        var problem = MakeProblem(
            requests: [MakeRequest(Pred, "A", 60, priority: 0), MakeRequest(Succ, "B", 60, priority: 0)],
            spaces: [MakeSpace(ResourceA, "Cell A")],
            dependencies:
            [
                new DependencyEdge(Pred, Succ, 0),
                new DependencyEdge(Succ, Pred, 0)
            ]);

        var window = new StartWindow(0, Day(10));
        var analyzed = new AnalyzedSchedulingProblem(
            problem,
            [
                new SchedulingCandidate(Pred, ResourceA, Space, 0, Day(10), 60, 0, [window]),
                new SchedulingCandidate(Succ, ResourceA, Space, 0, Day(10), 60, 0, [window])
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
        var problem = MakeProblem(
            requests: [MakeRequest(Pred, "Mill", 60, priority: 0), MakeRequest(Succ, "Grind", 60, priority: 0)],
            spaces: [MakeSpace(ResourceA, "Cell A")],
            dependencies: [new DependencyEdge(Pred, Succ, 0)]);

        // Only the successor has a candidate; the predecessor has none.
        var analyzed = new AnalyzedSchedulingProblem(
            problem,
            [new SchedulingCandidate(Succ, ResourceA, Space, 0, Day(5), 60, 0, [new StartWindow(0, Day(5))])],
            [],
            []);

        var solution = await new OrToolsSchedulingSolver(
            NullLogger<OrToolsSchedulingSolver>.Instance).SolveAsync(analyzed, CancellationToken.None);

        Assert.DoesNotContain(solution.Assignments, a => a.RequestId == Succ);
        Assert.Contains(SchedulingReasonCode.PredecessorUnscheduled,
            solution.Unscheduled.Single(u => u.RequestId == Succ).ReasonCodes);
    }
}
