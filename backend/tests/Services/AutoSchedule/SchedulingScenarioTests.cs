using Api.Models;
using Api.Services.AutoSchedule;
using Microsoft.Extensions.Logging.Abstractions;
using static Orkyo.Foundation.Tests.Services.AutoSchedule.AutoScheduleTestHelpers;

namespace Orkyo.Foundation.Tests.Services.AutoSchedule;

public class SchedulingScenarioTests
{
    private readonly SchedulingFeasibilityAnalyzer _analyzer = new();
    private readonly GreedySchedulingSolver _greedy = new();
    private readonly OrToolsSchedulingSolver _orTools = new(
        NullLogger<OrToolsSchedulingSolver>.Instance);

    private static RequestNode Req(
        string name, int days, int priority = 1,
        DateOnly? earliest = null, DateOnly? latest = null,
        params Guid[] criteria)
        => MakeRequest(name: name, durationDays: days, priority: priority, earliest: earliest, latest: latest,
            criteria: criteria.Length > 0 ? criteria.ToHashSet() : null);

    private static SpaceNode Space(string name, params Guid[] criteria)
        => MakeSpace(name: name,
            criteria: criteria.Length > 0 ? criteria.ToHashSet() : null);

    private async Task<SchedulingSolution> RunPipeline(
        SchedulingProblem problem, ISchedulingSolver solver)
    {
        var analyzed = _analyzer.Analyze(problem);
        return await solver.SolveAsync(analyzed, CancellationToken.None);
    }

    private static void AssertNoOverlaps(SchedulingSolution solution)
    {
        var bySpace = solution.Assignments.GroupBy(a => a.SpaceId);
        foreach (var group in bySpace)
        {
            var sorted = group.OrderBy(a => a.Start).ToList();
            for (int i = 1; i < sorted.Count; i++)
            {
                sorted[i].Start.Should().BeAfter(sorted[i - 1].End,
                    $"assignments on space {group.Key} should not overlap: " +
                    $"'{sorted[i - 1].Start}-{sorted[i - 1].End}' vs '{sorted[i].Start}-{sorted[i].End}'");
            }
        }
    }

    [Theory]
    [InlineData("Greedy")]
    [InlineData("OrTools")]
    public async Task Throughput_ConstrainedRequestsAreNotStarved(string solverName)
    {
        var solver = solverName == "Greedy" ? (ISchedulingSolver)_greedy : _orTools;

        var spaceA = Space("Room A");
        var spaceB = Space("Room B");

        var constrained1 = Req("Constrained-1", days: 3, priority: 2,
            earliest: new DateOnly(2026, 4, 14), latest: new DateOnly(2026, 4, 19));
        var constrained2 = Req("Constrained-2", days: 3, priority: 2,
            earliest: new DateOnly(2026, 4, 14), latest: new DateOnly(2026, 4, 19));
        var flexible = Req("Flexible-1", days: 5, priority: 1);

        var problem = MakeProblem(
            [constrained1, constrained2, flexible],
            [spaceA, spaceB]);

        var result = await RunPipeline(problem, solver);

        result.Assignments.Should().HaveCount(3);
        result.Assignments.Should().Contain(a => a.RequestId == constrained1.RequestId);
        result.Assignments.Should().Contain(a => a.RequestId == constrained2.RequestId);
        result.Assignments.Should().Contain(a => a.RequestId == flexible.RequestId);
        AssertNoOverlaps(result);
    }

    [Theory]
    [InlineData("Greedy")]
    [InlineData("OrTools")]
    public async Task CapabilityBottleneck_SpecializedRequestsGetSpecializedSpace(string solverName)
    {
        var solver = solverName == "Greedy" ? (ISchedulingSolver)_greedy : _orTools;

        var labCriterion = Guid.NewGuid();
        var specializedSpace = Space("Lab", labCriterion);
        var genericSpace1 = Space("Room 1");
        var genericSpace2 = Space("Room 2");

        var labRequest = Req("Lab Work", days: 10, priority: 2, criteria: labCriterion);
        var generic1 = Req("Meeting 1", days: 10, priority: 1);
        var generic2 = Req("Meeting 2", days: 10, priority: 1);

        var problem = MakeProblem(
            [labRequest, generic1, generic2],
            [specializedSpace, genericSpace1, genericSpace2]);

        var result = await RunPipeline(problem, solver);

        var labPlacement = result.Assignments.FirstOrDefault(a => a.RequestId == labRequest.RequestId);
        labPlacement.Should().NotBeNull("lab request should be scheduled");
        labPlacement!.SpaceId.Should().Be(specializedSpace.SpaceId,
            "lab request must go to the only compatible space");

        result.Assignments.Should().HaveCount(3);
        AssertNoOverlaps(result);
    }

    [Theory]
    [InlineData("Greedy")]
    [InlineData("OrTools")]
    public async Task TightDeadline_ImpossibleRequestsFlagged(string solverName)
    {
        var solver = solverName == "Greedy" ? (ISchedulingSolver)_greedy : _orTools;

        var space = Space("Room");

        var impossible = Req("Impossible", days: 10,
            earliest: new DateOnly(2026, 4, 14), latest: new DateOnly(2026, 4, 16));
        var possible = Req("Possible", days: 3,
            earliest: new DateOnly(2026, 4, 14), latest: new DateOnly(2026, 5, 14));

        var problem = MakeProblem([impossible, possible], [space]);
        var result = await RunPipeline(problem, solver);

        result.Assignments.Should().ContainSingle(a => a.RequestId == possible.RequestId);
        result.Unscheduled.Should().Contain(u => u.RequestId == impossible.RequestId);
    }

    [Theory]
    [InlineData("Greedy")]
    [InlineData("OrTools")]
    public async Task DensePacking_MaximizesUtilization(string solverName)
    {
        var solver = solverName == "Greedy" ? (ISchedulingSolver)_greedy : _orTools;

        var space = Space("Single Room");

        var requests = Enumerable.Range(1, 10)
            .Select(i => Req($"Task-{i}", days: 7, priority: i))
            .ToList();

        var problem = MakeProblem(requests, [space],
            horizonStart: new DateOnly(2026, 4, 14),
            horizonEnd: new DateOnly(2026, 7, 13));

        var result = await RunPipeline(problem, solver);

        result.Assignments.Should().HaveCount(10, "all 10 requests should fit in 90 days");
        AssertNoOverlaps(result);
    }

    [Theory]
    [InlineData("Greedy")]
    [InlineData("OrTools")]
    public async Task FixedAssignments_AreRespected(string solverName)
    {
        var solver = solverName == "Greedy" ? (ISchedulingSolver)_greedy : _orTools;

        var spaceId = Guid.NewGuid();
        var space = new SpaceNode(spaceId, "Room", new HashSet<Guid>());

        var fixedReqId = Guid.NewGuid();
        var fixedOcc = new FixedOccupancy(fixedReqId, spaceId,
            new DateOnly(2026, 4, 14), new DateOnly(2026, 4, 28));

        var newReq = Req("New Task", days: 5);

        var problem = MakeProblem([newReq], [space], fixedAssignments: [fixedOcc]);
        var result = await RunPipeline(problem, solver);

        result.Assignments.Should().ContainSingle();
        var placement = result.Assignments[0];
        placement.Start.Should().BeAfter(new DateOnly(2026, 4, 28),
            "new request must start after the fixed assignment ends");
    }

    [Theory]
    [InlineData("Greedy")]
    [InlineData("OrTools")]
    public async Task MultiSpace_CapabilityRoutingWorks(string solverName)
    {
        var solver = solverName == "Greedy" ? (ISchedulingSolver)_greedy : _orTools;

        var critA = Guid.NewGuid();
        var critB = Guid.NewGuid();

        var spaceA = Space("Space A", critA);
        var spaceB = Space("Space B", critB);
        var spaceAB = Space("Space AB", critA, critB);

        var reqA = Req("Needs A", days: 5, criteria: critA);
        var reqB = Req("Needs B", days: 5, criteria: critB);
        var reqAB = Req("Needs A+B", days: 5, criteria: [critA, critB]);

        var problem = MakeProblem([reqA, reqB, reqAB], [spaceA, spaceB, spaceAB]);
        var result = await RunPipeline(problem, solver);

        result.Assignments.Should().HaveCount(3);

        var abPlacement = result.Assignments.First(a => a.RequestId == reqAB.RequestId);
        abPlacement.SpaceId.Should().Be(spaceAB.SpaceId);

        AssertNoOverlaps(result);
    }

    [Fact]
    public async Task BothSolvers_ProduceValidSolutions_ForSameInput()
    {
        var critA = Guid.NewGuid();
        var spaces = new[]
        {
            Space("Room 1", critA),
            Space("Room 2"),
            Space("Room 3", critA),
        };

        var requests = new[]
        {
            Req("R1", days: 5, priority: 3, criteria: critA),
            Req("R2", days: 3, priority: 2),
            Req("R3", days: 7, priority: 1),
            Req("R4", days: 4, priority: 2, criteria: critA),
            Req("R5", days: 2, priority: 1,
                earliest: new DateOnly(2026, 5, 1), latest: new DateOnly(2026, 5, 10)),
        };

        var problem = MakeProblem(requests, spaces);

        var greedyResult = await RunPipeline(problem, _greedy);
        var orToolsResult = await RunPipeline(problem, _orTools);

        greedyResult.Assignments.Should().NotBeEmpty();
        orToolsResult.Assignments.Should().NotBeEmpty();

        AssertNoOverlaps(greedyResult);
        AssertNoOverlaps(orToolsResult);

        orToolsResult.Assignments.Count.Should()
            .BeGreaterThanOrEqualTo(greedyResult.Assignments.Count);
    }
}
