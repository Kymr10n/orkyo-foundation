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
        var spaceId = Guid.NewGuid();
        var candidate = MakeCandidate(requestId: reqId, spaceId: spaceId);

        var result = await _solver.SolveAsync(MakeAnalyzed([candidate]), CancellationToken.None);

        result.Status.Should().BeOneOf(SolverStatus.Optimal, SolverStatus.Feasible);
        result.SolverUsed.Should().Be(SolverKind.OrToolsCpSat);
        result.Assignments.Should().ContainSingle(a => a.RequestId == reqId);
    }

    [Fact]
    public async Task NoOverlap_IsEnforced()
    {
        var spaceId = Guid.NewGuid();
        var r1 = Guid.NewGuid();
        var r2 = Guid.NewGuid();

        var starts = Enumerable.Range(0, 20)
            .Select(i => new DateOnly(2026, 4, 14).AddDays(i))
            .ToList();

        var c1 = MakeCandidate(requestId: r1, spaceId: spaceId, durationDays: 5, feasibleStarts: starts);
        var c2 = MakeCandidate(requestId: r2, spaceId: spaceId, durationDays: 5, feasibleStarts: starts);

        var result = await _solver.SolveAsync(MakeAnalyzed([c1, c2]), CancellationToken.None);

        result.Status.Should().BeOneOf(SolverStatus.Optimal, SolverStatus.Feasible);

        var assignments = result.Assignments.OrderBy(a => a.Start).ToList();
        if (assignments.Count == 2)
            assignments[0].End.Should().BeBefore(assignments[1].Start);
    }

    [Fact]
    public async Task RequestAssignedAtMostOnce()
    {
        var reqId = Guid.NewGuid();
        var space1 = Guid.NewGuid();
        var space2 = Guid.NewGuid();

        var starts = Enumerable.Range(0, 20)
            .Select(i => new DateOnly(2026, 4, 14).AddDays(i))
            .ToList();

        var c1 = MakeCandidate(requestId: reqId, spaceId: space1, durationDays: 3, feasibleStarts: starts);
        var c2 = MakeCandidate(requestId: reqId, spaceId: space2, durationDays: 3, feasibleStarts: starts);

        var result = await _solver.SolveAsync(MakeAnalyzed([c1, c2]), CancellationToken.None);

        result.Assignments.Count(a => a.RequestId == reqId).Should().BeLessThanOrEqualTo(1);
    }

    [Fact]
    public async Task FixedOccupancy_IsRespected()
    {
        var spaceId = Guid.NewGuid();
        var fixedOcc = new FixedOccupancy(
            Guid.NewGuid(), spaceId,
            new DateOnly(2026, 4, 14), new DateOnly(2026, 4, 18));

        var reqId = Guid.NewGuid();
        var starts = Enumerable.Range(0, 20)
            .Select(i => new DateOnly(2026, 4, 14).AddDays(i))
            .ToList();
        var candidate = MakeCandidate(requestId: reqId, spaceId: spaceId, durationDays: 3, feasibleStarts: starts);

        var result = await _solver.SolveAsync(
            MakeAnalyzed([candidate], fixedAssignments: [fixedOcc]),
            CancellationToken.None);

        if (result.Assignments.Any(a => a.RequestId == reqId))
        {
            var placement = result.Assignments.First(a => a.RequestId == reqId);
            var overlaps = !(placement.End < fixedOcc.Start || placement.Start > fixedOcc.End);
            overlaps.Should().BeFalse("solver must not overlap with fixed occupancy");
        }
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
        var spaceId = Guid.NewGuid();
        var r1 = Guid.NewGuid();
        var r2 = Guid.NewGuid();

        var starts = Enumerable.Range(0, 30)
            .Select(i => new DateOnly(2026, 4, 14).AddDays(i))
            .ToList();

        var c1 = MakeCandidate(requestId: r1, spaceId: spaceId, durationDays: 5, priority: 1, feasibleStarts: starts);
        var c2 = MakeCandidate(requestId: r2, spaceId: spaceId, durationDays: 5, priority: 2, feasibleStarts: starts);

        var result = await _solver.SolveAsync(MakeAnalyzed([c1, c2]), CancellationToken.None);

        result.Assignments.Should().HaveCount(2);
    }

    [Fact]
    public async Task Diagnostics_ContainSolverInfo()
    {
        var candidate = MakeCandidate();
        var result = await _solver.SolveAsync(MakeAnalyzed([candidate]), CancellationToken.None);

        result.Diagnostics.Should().Contain(d => d.Contains("CP-SAT"));
    }
}
