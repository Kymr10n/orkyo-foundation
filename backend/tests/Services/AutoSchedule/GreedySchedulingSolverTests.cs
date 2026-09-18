using Api.Models;
using Api.Services.AutoSchedule;
using static Orkyo.Foundation.Tests.Services.AutoSchedule.AutoScheduleTestHelpers;

namespace Orkyo.Foundation.Tests.Services.AutoSchedule;

public class GreedySchedulingSolverTests
{
    private readonly GreedySchedulingSolver _solver = new();

    [Fact]
    public async Task ChoosesFeasiblePlacement()
    {
        var reqId = Guid.NewGuid();
        var resourceId = Guid.NewGuid();
        var candidate = MakeCandidate(requestId: reqId, resourceId: resourceId, durationMinutes: Day(3));

        var result = await _solver.SolveAsync(MakeAnalyzed([candidate]), CancellationToken.None);

        result.Assignments.Should().ContainSingle(a =>
            a.RequestId == reqId && a.ResourceId == resourceId);
        result.Status.Should().Be(SolverStatus.Feasible);
        result.SolverUsed.Should().Be(SolverKind.Greedy);
    }

    [Fact]
    public async Task RespectsFixedAssignments_NoOverlap()
    {
        var resourceId = Guid.NewGuid();
        var fixedOcc = new FixedOccupancy(Guid.NewGuid(), resourceId, Day(0), Day(5));

        var reqId = Guid.NewGuid();
        var candidate = MakeCandidate(requestId: reqId, resourceId: resourceId, durationMinutes: Day(3));

        var result = await _solver.SolveAsync(
            MakeAnalyzed([candidate], fixedAssignments: [fixedOcc]),
            CancellationToken.None);

        result.Assignments.Should().ContainSingle();
        var placement = result.Assignments[0];
        var overlaps = placement.Start < fixedOcc.End && placement.End > fixedOcc.Start;
        overlaps.Should().BeFalse();
    }

    [Fact]
    public async Task PlacesRightAfterAFixedOccupancy_NotADayLater()
    {
        // The whole point of minute resolution: the next job starts the minute the resource
        // frees up, not the next morning.
        var resourceId = Guid.NewGuid();
        var fixedOcc = new FixedOccupancy(Guid.NewGuid(), resourceId, 0, 20);
        var candidate = MakeCandidate(resourceId: resourceId, durationMinutes: 180);

        var result = await _solver.SolveAsync(
            MakeAnalyzed([candidate], fixedAssignments: [fixedOcc]),
            CancellationToken.None);

        result.Assignments.Single().Start.Should().Be(20);
    }

    [Fact]
    public async Task ReturnsUnscheduled_WhenAllSlotsBlocked()
    {
        var resourceId = Guid.NewGuid();
        var reqId = Guid.NewGuid();

        var candidate = MakeCandidate(
            requestId: reqId,
            resourceId: resourceId,
            durationMinutes: Day(5),
            windows: [new StartWindow(Day(0), Day(3))]);

        var fixedOcc = new FixedOccupancy(Guid.NewGuid(), resourceId, Day(0), Day(12));

        var result = await _solver.SolveAsync(
            MakeAnalyzed([candidate], fixedAssignments: [fixedOcc]),
            CancellationToken.None);

        result.Assignments.Should().BeEmpty();
        result.Unscheduled.Should().ContainSingle(u => u.RequestId == reqId);
        result.Unscheduled[0].ReasonCodes.Should()
            .Contain(SchedulingReasonCode.BlockedByFixedAssignments);
    }

    [Fact]
    public async Task MultipleRequests_LeastFlexibleFirst()
    {
        var resourceId = Guid.NewGuid();

        var constrainedId = Guid.NewGuid();
        var constrained = MakeCandidate(
            requestId: constrainedId,
            resourceId: resourceId,
            durationMinutes: Day(3),
            windows: [new StartWindow(Day(0), Day(2))]);

        var flexibleId = Guid.NewGuid();
        var flexible = MakeCandidate(
            requestId: flexibleId,
            resourceId: resourceId,
            durationMinutes: Day(3));

        var result = await _solver.SolveAsync(
            MakeAnalyzed([flexible, constrained]),
            CancellationToken.None);

        result.Assignments.Should().HaveCount(2);
        result.Assignments.Should().Contain(a => a.RequestId == constrainedId);
        result.Assignments.Should().Contain(a => a.RequestId == flexibleId);

        var placements = result.Assignments.OrderBy(a => a.Start).ToList();
        placements[0].End.Should().BeLessThanOrEqualTo(placements[1].Start);
    }

    [Fact]
    public async Task RejectedRequests_IncludedInUnscheduled()
    {
        var rejectedId = Guid.NewGuid();
        var rejection = new CandidateRejection(
            rejectedId, null, SchedulingReasonCode.NoCompatibleResource);

        var result = await _solver.SolveAsync(
            MakeAnalyzed([], rejections: [rejection]),
            CancellationToken.None);

        result.Unscheduled.Should().ContainSingle(u =>
            u.RequestId == rejectedId);
        result.Unscheduled[0].ReasonCodes.Should()
            .Contain(SchedulingReasonCode.NoCompatibleResource);
    }

    [Fact]
    public async Task CancellationToken_IsRespected()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var candidates = Enumerable.Range(0, 100)
            .Select(_ => MakeCandidate())
            .ToList();

        var act = () => _solver.SolveAsync(MakeAnalyzed(candidates), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
