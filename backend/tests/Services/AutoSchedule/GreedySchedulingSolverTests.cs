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
        var spaceId = Guid.NewGuid();
        var candidate = MakeCandidate(requestId: reqId, spaceId: spaceId, durationDays: 3);

        var result = await _solver.SolveAsync(MakeAnalyzed([candidate]), CancellationToken.None);

        result.Assignments.Should().ContainSingle(a =>
            a.RequestId == reqId && a.SpaceId == spaceId);
        result.Status.Should().Be(SolverStatus.Feasible);
        result.SolverUsed.Should().Be(SolverKind.Greedy);
    }

    [Fact]
    public async Task RespectsFixedAssignments_NoOverlap()
    {
        var spaceId = Guid.NewGuid();
        var fixedOcc = new FixedOccupancy(
            Guid.NewGuid(), spaceId,
            new DateOnly(2026, 4, 14), new DateOnly(2026, 4, 18));

        var reqId = Guid.NewGuid();
        var candidate = MakeCandidate(
            requestId: reqId,
            spaceId: spaceId,
            durationDays: 3,
            feasibleStarts: Enumerable.Range(0, 30)
                .Select(i => new DateOnly(2026, 4, 14).AddDays(i))
                .ToList());

        var result = await _solver.SolveAsync(
            MakeAnalyzed([candidate], fixedAssignments: [fixedOcc]),
            CancellationToken.None);

        result.Assignments.Should().ContainSingle();
        var placement = result.Assignments[0];
        var overlaps = !(placement.End < fixedOcc.Start || placement.Start > fixedOcc.End);
        overlaps.Should().BeFalse();
    }

    [Fact]
    public async Task ReturnsUnscheduled_WhenAllSlotsBlocked()
    {
        var spaceId = Guid.NewGuid();
        var reqId = Guid.NewGuid();

        var candidate = MakeCandidate(
            requestId: reqId,
            spaceId: spaceId,
            durationDays: 5,
            feasibleStarts: [
                new DateOnly(2026, 4, 14),
                new DateOnly(2026, 4, 15),
                new DateOnly(2026, 4, 16)
            ]);

        var fixedOcc = new FixedOccupancy(
            Guid.NewGuid(), spaceId,
            new DateOnly(2026, 4, 14), new DateOnly(2026, 4, 25));

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
        var spaceId = Guid.NewGuid();

        var constrainedId = Guid.NewGuid();
        var constrained = MakeCandidate(
            requestId: constrainedId,
            spaceId: spaceId,
            durationDays: 3,
            feasibleStarts: [new DateOnly(2026, 4, 14), new DateOnly(2026, 4, 15)]);

        var flexibleId = Guid.NewGuid();
        var flexible = MakeCandidate(
            requestId: flexibleId,
            spaceId: spaceId,
            durationDays: 3,
            feasibleStarts: Enumerable.Range(0, 30)
                .Select(i => new DateOnly(2026, 4, 14).AddDays(i))
                .ToList());

        var result = await _solver.SolveAsync(
            MakeAnalyzed([flexible, constrained]),
            CancellationToken.None);

        result.Assignments.Should().HaveCount(2);
        result.Assignments.Should().Contain(a => a.RequestId == constrainedId);
        result.Assignments.Should().Contain(a => a.RequestId == flexibleId);

        var placements = result.Assignments.OrderBy(a => a.Start).ToList();
        placements[0].End.Should().BeBefore(placements[1].Start);
    }

    [Fact]
    public async Task RejectedRequests_IncludedInUnscheduled()
    {
        var rejectedId = Guid.NewGuid();
        var rejection = new CandidateRejection(
            rejectedId, null, SchedulingReasonCode.NoCompatibleSpace);

        var result = await _solver.SolveAsync(
            MakeAnalyzed([], rejections: [rejection]),
            CancellationToken.None);

        result.Unscheduled.Should().ContainSingle(u =>
            u.RequestId == rejectedId);
        result.Unscheduled[0].ReasonCodes.Should()
            .Contain(SchedulingReasonCode.NoCompatibleSpace);
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
