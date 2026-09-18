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

    private ISchedulingSolver Solver(string name) => name == "Greedy" ? _greedy : _orTools;

    private static RequestNode Req(
        string name, int minutes, int priority = 1,
        int? earliest = null, int? latest = null,
        params Guid[] criteria)
        => MakeRequest(name: name, durationMinutes: minutes, priority: priority, earliest: earliest, latest: latest,
            criteria: criteria.Length > 0 ? criteria.ToHashSet() : null);

    private static ResourceNode Space(string name, params Guid[] criteria)
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
        var bySpace = solution.Assignments.GroupBy(a => a.ResourceId);
        foreach (var group in bySpace)
        {
            var sorted = group.OrderBy(a => a.Start).ToList();
            for (int i = 1; i < sorted.Count; i++)
            {
                sorted[i].Start.Should().BeGreaterThanOrEqualTo(sorted[i - 1].End,
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
        var spaceA = Space("Room A");
        var spaceB = Space("Room B");

        var constrained1 = Req("Constrained-1", Day(3), priority: 2, earliest: Day(0), latest: Day(6));
        var constrained2 = Req("Constrained-2", Day(3), priority: 2, earliest: Day(0), latest: Day(6));
        var flexible = Req("Flexible-1", Day(5), priority: 1);

        var problem = MakeProblem(
            [constrained1, constrained2, flexible],
            [spaceA, spaceB]);

        var result = await RunPipeline(problem, Solver(solverName));

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
        var labCriterion = Guid.NewGuid();
        var specializedSpace = Space("Lab", labCriterion);
        var genericSpace1 = Space("Room 1");
        var genericSpace2 = Space("Room 2");

        var labRequest = Req("Lab Work", Day(10), priority: 2, criteria: labCriterion);
        var generic1 = Req("Meeting 1", Day(10), priority: 1);
        var generic2 = Req("Meeting 2", Day(10), priority: 1);

        var problem = MakeProblem(
            [labRequest, generic1, generic2],
            [specializedSpace, genericSpace1, genericSpace2]);

        var result = await RunPipeline(problem, Solver(solverName));

        var labPlacement = result.Assignments.FirstOrDefault(a => a.RequestId == labRequest.RequestId);
        labPlacement.Should().NotBeNull("lab request should be scheduled");
        labPlacement!.ResourceId.Should().Be(specializedSpace.ResourceId,
            "lab request must go to the only compatible space");

        result.Assignments.Should().HaveCount(3);
        AssertNoOverlaps(result);
    }

    [Theory]
    [InlineData("Greedy")]
    [InlineData("OrTools")]
    public async Task TightDeadline_ImpossibleRequestsFlagged(string solverName)
    {
        var space = Space("Room");

        var impossible = Req("Impossible", Day(10), earliest: Day(0), latest: Day(3));
        var possible = Req("Possible", Day(3), earliest: Day(0), latest: Day(31));

        var problem = MakeProblem([impossible, possible], [space]);
        var result = await RunPipeline(problem, Solver(solverName));

        result.Assignments.Should().ContainSingle(a => a.RequestId == possible.RequestId);
        result.Unscheduled.Should().Contain(u => u.RequestId == impossible.RequestId);
    }

    [Theory]
    [InlineData("Greedy")]
    [InlineData("OrTools")]
    public async Task DensePacking_MaximizesUtilization(string solverName)
    {
        var space = Space("Single Room");

        var requests = Enumerable.Range(1, 10)
            .Select(i => Req($"Task-{i}", Day(7), priority: i))
            .ToList();

        var problem = MakeProblem(requests, [space],
            horizonStart: new DateOnly(2026, 4, 14),
            horizonEnd: new DateOnly(2026, 7, 13));

        var result = await RunPipeline(problem, Solver(solverName));

        result.Assignments.Should().HaveCount(10, "all 10 requests should fit in 91 days");
        AssertNoOverlaps(result);
    }

    [Theory]
    [InlineData("Greedy")]
    [InlineData("OrTools")]
    public async Task FixedAssignments_AreRespected(string solverName)
    {
        var resourceId = Guid.NewGuid();
        var space = new ResourceNode(resourceId, "Room", new HashSet<Guid>());

        var fixedOcc = new FixedOccupancy(Guid.NewGuid(), resourceId, Day(0), Day(15));
        var newReq = Req("New Task", Day(5));

        var problem = MakeProblem([newReq], [space], fixedAssignments: [fixedOcc]);
        var result = await RunPipeline(problem, Solver(solverName));

        result.Assignments.Should().ContainSingle();
        result.Assignments[0].Start.Should().BeGreaterThanOrEqualTo(Day(15),
            "new request must start once the fixed assignment ends");
    }

    [Theory]
    [InlineData("Greedy")]
    [InlineData("OrTools")]
    public async Task MultiSpace_CapabilityRoutingWorks(string solverName)
    {
        var critA = Guid.NewGuid();
        var critB = Guid.NewGuid();

        var spaceA = Space("Space A", critA);
        var spaceB = Space("Space B", critB);
        var spaceAB = Space("Space AB", critA, critB);

        var reqA = Req("Needs A", Day(5), criteria: critA);
        var reqB = Req("Needs B", Day(5), criteria: critB);
        var reqAB = Req("Needs A+B", Day(5), criteria: [critA, critB]);

        var problem = MakeProblem([reqA, reqB, reqAB], [spaceA, spaceB, spaceAB]);
        var result = await RunPipeline(problem, Solver(solverName));

        result.Assignments.Should().HaveCount(3);

        var abPlacement = result.Assignments.First(a => a.RequestId == reqAB.RequestId);
        abPlacement.ResourceId.Should().Be(spaceAB.ResourceId);

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
            Req("R1", Day(5), priority: 3, criteria: critA),
            Req("R2", Day(3), priority: 2),
            Req("R3", Day(7), priority: 1),
            Req("R4", Day(4), priority: 2, criteria: critA),
            Req("R5", Day(2), priority: 1, earliest: Day(17), latest: Day(27)),
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

    // ── The machine shop ──────────────────────────────────────────────────────
    // Saw 20 min → mill 3 h → deburr 45 min, each on its own station, in a site that works
    // 08:00–17:00 on weekdays. Under day resolution this took three days at minimum; the point
    // of the minute axis is that it finishes before lunch.

    private static readonly Guid SawId = Guid.NewGuid();
    private static readonly Guid MillId = Guid.NewGuid();
    private static readonly Guid DeburrId = Guid.NewGuid();

    private static WorkingTimeAxis WeekdayAxis(DateOnly start, DateOnly end)
        => WorkingTimeAxis.Build(start, end, SchedulingSettingsInfo.Default(Guid.NewGuid()) with
        {
            WorkingHoursEnabled = true,
            WorkingDayStart = new TimeOnly(8, 0),
            WorkingDayEnd = new TimeOnly(17, 0),
            WeekendsEnabled = false,
        }, respectSchedulingSettings: true);

    private static SchedulingProblem MachineShop(int sawToMillLagMinutes = 0)
    {
        // Monday 13 April 2026 to Friday 17 April: one working week, 45 working hours.
        var start = new DateOnly(2026, 4, 13);
        var end = new DateOnly(2026, 4, 17);
        var sawCrit = Guid.NewGuid();
        var millCrit = Guid.NewGuid();
        var benchCrit = Guid.NewGuid();

        return MakeProblem(
            requests:
            [
                MakeRequest(SawId, "Saw", 20, criteria: new HashSet<Guid> { sawCrit }),
                MakeRequest(MillId, "Mill", 180, criteria: new HashSet<Guid> { millCrit }),
                MakeRequest(DeburrId, "Deburr", 45, criteria: new HashSet<Guid> { benchCrit }),
            ],
            spaces:
            [
                Space("Band saw", sawCrit),
                Space("Mill", millCrit),
                Space("Deburr bench", benchCrit),
            ],
            horizonStart: start, horizonEnd: end,
            axis: WeekdayAxis(start, end),
            dependencies:
            [
                new DependencyEdge(SawId, MillId, sawToMillLagMinutes),
                new DependencyEdge(MillId, DeburrId, 0),
            ]);
    }

    [Theory]
    [InlineData("Greedy")]
    [InlineData("OrTools")]
    public async Task MachineShop_AFiveHourRoutingFinishesTheSameMorning(string solverName)
    {
        var problem = MachineShop();

        var result = await RunPipeline(problem, Solver(solverName));

        result.Assignments.Should().HaveCount(3);
        var saw = result.Assignments.Single(a => a.RequestId == SawId);
        var mill = result.Assignments.Single(a => a.RequestId == MillId);
        var deburr = result.Assignments.Single(a => a.RequestId == DeburrId);

        // Back to back, in order, and the whole part is done 4 h 05 min after the saw starts.
        saw.Start.Should().Be(0);
        mill.Start.Should().Be(saw.End);
        deburr.Start.Should().Be(mill.End);
        deburr.End.Should().Be(20 + 180 + 45);

        // In calendar terms: Monday 08:00 to Monday 12:05.
        problem.Axis.StartAt(saw.Start).Should().Be(new DateTime(2026, 4, 13, 8, 0, 0, DateTimeKind.Utc));
        problem.Axis.EndAt(deburr.End).Should().Be(new DateTime(2026, 4, 13, 12, 5, 0, DateTimeKind.Utc));
    }

    [Theory]
    [InlineData("Greedy")]
    [InlineData("OrTools")]
    public async Task MachineShop_AFourHourLagDelaysByFourHours_NotADay(string solverName)
    {
        var problem = MachineShop(sawToMillLagMinutes: 240);

        var result = await RunPipeline(problem, Solver(solverName));

        var saw = result.Assignments.Single(a => a.RequestId == SawId);
        var mill = result.Assignments.Single(a => a.RequestId == MillId);

        mill.Start.Should().Be(saw.End + 240);
        // Still Monday: 08:20 + 4 h = 12:20.
        problem.Axis.StartAt(mill.Start).Should().Be(new DateTime(2026, 4, 13, 12, 20, 0, DateTimeKind.Utc));
    }

    [Theory]
    [InlineData("Greedy")]
    [InlineData("OrTools")]
    public async Task WorkingHours_AJobSpansTheNightAndIsWrittenWithinTheWorkingDay(string solverName)
    {
        // A 12-hour job in a 9-hour working day: starts Monday 08:00, ends Tuesday 11:00 — and
        // a 9-hour job placed after it ends Wednesday at close of business, not Thursday 08:00.
        var start = new DateOnly(2026, 4, 13);
        var end = new DateOnly(2026, 4, 17);
        var resourceId = Guid.NewGuid();
        var twelveHours = MakeRequest(name: "Long", durationMinutes: 12 * 60, priority: 2);
        var nineHours = MakeRequest(name: "Day", durationMinutes: 9 * 60, priority: 1);

        var problem = MakeProblem([twelveHours, nineHours], [new ResourceNode(resourceId, "Cell", new HashSet<Guid>())],
            horizonStart: start, horizonEnd: end, axis: WeekdayAxis(start, end));

        var result = await RunPipeline(problem, Solver(solverName));

        result.Assignments.Should().HaveCount(2);
        AssertNoOverlaps(result);
        var ordered = result.Assignments.OrderBy(a => a.Start).ToList();
        var first = ordered[0];
        var second = ordered[1];

        problem.Axis.StartAt(first.Start).Should().Be(new DateTime(2026, 4, 13, 8, 0, 0, DateTimeKind.Utc));
        problem.Axis.EndAt(first.End).Should().Be(first.DurationMinutes == 12 * 60
            ? new DateTime(2026, 4, 14, 11, 0, 0, DateTimeKind.Utc)
            : new DateTime(2026, 4, 13, 17, 0, 0, DateTimeKind.Utc));
        // Whichever order, 21 working hours end exactly at Wednesday 11:00.
        problem.Axis.EndAt(second.End).Should().Be(new DateTime(2026, 4, 15, 11, 0, 0, DateTimeKind.Utc));
    }

    [Theory]
    [InlineData("Greedy")]
    [InlineData("OrTools")]
    public async Task WorkingHours_ASaturdayBookingBlocksAJobThatWouldSpanTheWeekend(string solverName)
    {
        // Friday to Monday is one continuous stretch on a weekday axis, but a hand-made Saturday
        // booking on the resource still holds it over the weekend. The booking collapses to a
        // point at the Friday/Monday boundary; a job may end or start there, not run across it.
        var start = new DateOnly(2026, 4, 13); // Monday
        var end = new DateOnly(2026, 4, 24);   // the following Friday
        var axis = WeekdayAxis(start, end);
        var resourceId = Guid.NewGuid();

        var saturday = new DateTime(2026, 4, 18, 9, 0, 0, DateTimeKind.Utc);
        var booking = new FixedOccupancy(Guid.NewGuid(), resourceId,
            axis.ToOffset(saturday), axis.ToOffsetEnd(saturday.AddHours(4)));
        booking.End.Should().Be(booking.Start, "the booking lies entirely in non-working time");
        booking.Start.Should().Be(5 * 9 * 60, "Friday 17:00 is 45 working hours in");

        // Friday's 9 h are already taken; the 10 h job cannot start earlier than Thursday
        // 16:00 and finish Friday, so without the point it would run Friday into Monday.
        var fridayTaken = new FixedOccupancy(Guid.NewGuid(), resourceId, 4 * 9 * 60, 5 * 9 * 60);
        var tenHours = MakeRequest(name: "Long", durationMinutes: 10 * 60, earliest: 4 * 9 * 60 - 60);

        var problem = MakeProblem([tenHours], [new ResourceNode(resourceId, "Cell", new HashSet<Guid>())],
            horizonStart: start, horizonEnd: end, axis: axis, fixedAssignments: [fridayTaken, booking]);

        var result = await RunPipeline(problem, Solver(solverName));

        var placement = result.Assignments.Should().ContainSingle().Subject;
        placement.Start.Should().BeGreaterThanOrEqualTo(booking.Start, "it may not span the weekend point");
        axis.StartAt(placement.Start).Should().Be(new DateTime(2026, 4, 20, 8, 0, 0, DateTimeKind.Utc));
    }
}
