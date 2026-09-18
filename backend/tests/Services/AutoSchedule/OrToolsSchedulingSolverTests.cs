using Api.Models;
using Api.Services.AutoSchedule;
using Microsoft.Extensions.Logging.Abstractions;
using static Orkyo.Foundation.Tests.Services.AutoSchedule.AutoScheduleTestHelpers;

namespace Orkyo.Foundation.Tests.Services.AutoSchedule;

public class OrToolsSchedulingSolverTests
{
    private readonly OrToolsSchedulingSolver _solver = new(
        NullLogger<OrToolsSchedulingSolver>.Instance);

    [Fact]
    public async Task ModelBuilds_ForSmallScenario()
    {
        var reqId = Guid.NewGuid();
        var resourceId = Guid.NewGuid();
        var candidate = MakeCandidate(requestId: reqId, resourceId: resourceId);

        var result = await _solver.SolveAsync(MakeAnalyzed([candidate]), CancellationToken.None);

        result.Status.Should().BeOneOf(SolverStatus.Optimal, SolverStatus.Feasible);
        result.SolverUsed.Should().Be(SolverKind.OrToolsCpSat);
        result.Assignments.Should().ContainSingle(a => a.RequestId == reqId);
    }

    [Fact]
    public async Task NoOverlap_IsEnforced()
    {
        var resourceId = Guid.NewGuid();
        var r1 = Guid.NewGuid();
        var r2 = Guid.NewGuid();
        var windows = new[] { new StartWindow(0, Day(20)) };

        var c1 = MakeCandidate(requestId: r1, resourceId: resourceId, durationMinutes: Day(5), windows: windows);
        var c2 = MakeCandidate(requestId: r2, resourceId: resourceId, durationMinutes: Day(5), windows: windows);

        var result = await _solver.SolveAsync(MakeAnalyzed([c1, c2]), CancellationToken.None);

        result.Status.Should().BeOneOf(SolverStatus.Optimal, SolverStatus.Feasible);

        var assignments = result.Assignments.OrderBy(a => a.Start).ToList();
        assignments.Should().HaveCount(2);
        assignments[0].End.Should().BeLessThanOrEqualTo(assignments[1].Start);
    }

    [Fact]
    public async Task RequestAssignedAtMostOnce()
    {
        var reqId = Guid.NewGuid();
        var space1 = Guid.NewGuid();
        var space2 = Guid.NewGuid();

        var c1 = MakeCandidate(requestId: reqId, resourceId: space1, durationMinutes: Day(3));
        var c2 = MakeCandidate(requestId: reqId, resourceId: space2, durationMinutes: Day(3));

        var result = await _solver.SolveAsync(MakeAnalyzed([c1, c2]), CancellationToken.None);

        result.Assignments.Count(a => a.RequestId == reqId).Should().Be(1);
    }

    [Fact]
    public async Task FixedOccupancy_IsRespected()
    {
        var resourceId = Guid.NewGuid();
        var fixedOcc = new FixedOccupancy(Guid.NewGuid(), resourceId, Day(0), Day(5));

        var reqId = Guid.NewGuid();
        var candidate = MakeCandidate(requestId: reqId, resourceId: resourceId, durationMinutes: Day(3));

        var result = await _solver.SolveAsync(
            MakeAnalyzed([candidate], fixedAssignments: [fixedOcc]),
            CancellationToken.None);

        var placement = result.Assignments.Should().ContainSingle(a => a.RequestId == reqId).Subject;
        var overlaps = placement.Start < fixedOcc.End && placement.End > fixedOcc.Start;
        overlaps.Should().BeFalse("solver must not overlap with fixed occupancy");
    }

    [Fact]
    public async Task ZeroLengthOccupancy_CanBeTouchedButNotSpanned()
    {
        // A hand-made Saturday booking on a weekday-only axis collapses to a point. A placement
        // may end there or start there, but a job that runs across it would cover the calendar
        // weekend the booking already holds.
        var resourceId = Guid.NewGuid();
        var point = new FixedOccupancy(Guid.NewGuid(), resourceId, 100, 100);

        // Only starts strictly spanning the point are open: [1, 99]. Every one collides.
        var spanning = MakeCandidate(resourceId: resourceId, durationMinutes: 100,
            windows: [new StartWindow(1, 100)]);
        var blocked = await _solver.SolveAsync(
            MakeAnalyzed([spanning], fixedAssignments: [point]), CancellationToken.None);
        blocked.Assignments.Should().BeEmpty();

        // Touching from either side is fine.
        var touching = MakeCandidate(resourceId: resourceId, durationMinutes: 100,
            windows: [new StartWindow(0, 1), new StartWindow(100, 101)]);
        var placed = await _solver.SolveAsync(
            MakeAnalyzed([touching], fixedAssignments: [point]), CancellationToken.None);
        placed.Assignments.Should().ContainSingle()
            .Which.Start.Should().BeOneOf(0, 100);
    }

    [Fact]
    public async Task EmptyCandidates_ReturnsEmptySolution()
    {
        var result = await _solver.SolveAsync(MakeAnalyzed([]), CancellationToken.None);

        result.Assignments.Should().BeEmpty();
    }

    [Fact]
    public async Task MaximizesThroughput_OverEarlyCompletion()
    {
        var resourceId = Guid.NewGuid();
        var r1 = Guid.NewGuid();
        var r2 = Guid.NewGuid();

        var c1 = MakeCandidate(requestId: r1, resourceId: resourceId, durationMinutes: Day(5), priority: 1);
        var c2 = MakeCandidate(requestId: r2, resourceId: resourceId, durationMinutes: Day(5), priority: 2);

        var result = await _solver.SolveAsync(MakeAnalyzed([c1, c2]), CancellationToken.None);

        result.Assignments.Should().HaveCount(2);
    }

    [Fact]
    public async Task ALateStartIsStillWorthScheduling()
    {
        // At minute resolution a start offset runs into the hundreds of thousands. With a fixed
        // throughput weight the start penalty would outweigh placing the request at all, and
        // the solver would leave it unscheduled rather than place it late.
        var resourceId = Guid.NewGuid();
        var late = MakeCandidate(resourceId: resourceId, durationMinutes: 60,
            windows: [new StartWindow(Day(85), Day(85) + 1)]);

        var result = await _solver.SolveAsync(MakeAnalyzed([late]), CancellationToken.None);

        result.Assignments.Should().ContainSingle().Which.Start.Should().Be(Day(85));
    }

    [Fact]
    public async Task Diagnostics_ContainSolverInfo()
    {
        var candidate = MakeCandidate();
        var result = await _solver.SolveAsync(MakeAnalyzed([candidate]), CancellationToken.None);

        result.Diagnostics.Should().Contain(d => d.Contains("CP-SAT"));
    }
}
