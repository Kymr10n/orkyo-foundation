using Api.Constants;
using Api.Models;
using Api.Services.AutoSchedule;
using Xunit;

namespace Orkyo.Foundation.Tests.Services;

/// <summary>
/// Verifies that the Greedy solver correctly handles multi-resource requirements
/// (request requires one Space + one Person) while keeping single-Space requests
/// byte-equivalent to the pre-Phase-4 behavior.
/// </summary>
public class MultiResourceRequirementTests
{
    private static readonly Guid PersonTypeId = Guid.NewGuid();

    private static SchedulingProblem BuildProblem(
        IReadOnlyList<RequestNode> requests,
        IReadOnlyList<SpaceNode> spaces,
        IReadOnlyList<ResourceNode>? additionalResources = null,
        IReadOnlyList<FixedOccupancy>? fixedAssignments = null)
    {
        var horizon = DateOnly.FromDateTime(DateTime.Today);
        return new SchedulingProblem(
            SiteId: Guid.NewGuid(),
            HorizonStart: horizon,
            HorizonEnd: horizon.AddDays(30),
            Requests: requests,
            Spaces: spaces,
            FixedAssignments: fixedAssignments ?? [],
            Settings: null,
            BlockedPeriodsByResource: null,
            Mode: AutoScheduleMode.FillGapsOnly,
            AdditionalResources: additionalResources ?? []);
    }

    private static RequestNode MakeRequest(
        Guid id, int durationDays,
        IReadOnlyList<IResourceRequirement>? additionalRequirements = null) =>
        new(id, "Test Request", null, null, durationDays, Priority: 2,
            RespectSchedulingSettings: false,
            RequiredCriterionIds: new HashSet<Guid>(),
            AdditionalRequirements: additionalRequirements ?? []);

    private static SpaceNode MakeSpace(Guid id) =>
        new(id, "Test Space", new HashSet<Guid>());

    private static ResourceNode MakePerson(Guid id) =>
        new(id, PersonTypeId, AllocationModes.Exclusive, new HashSet<Guid>());

    [Fact]
    public async Task SingleSpaceRequest_ByteEquivalentBehavior()
    {
        var spaceId = Guid.NewGuid();
        var requestId = Guid.NewGuid();

        var problem = BuildProblem(
            requests: [MakeRequest(requestId, durationDays: 5)],
            spaces: [MakeSpace(spaceId)]);

        var analyzed = new SchedulingFeasibilityAnalyzer().Analyze(problem);
        var solution = await new GreedySchedulingSolver().SolveAsync(analyzed, CancellationToken.None);

        Assert.Single(solution.Assignments);
        var assignment = solution.Assignments[0];
        Assert.Equal(requestId, assignment.RequestId);
        Assert.Equal(spaceId, assignment.ResourceId);
        Assert.True(assignment.AdditionalResourceIds is null or { Count: 0 });
    }

    [Fact]
    public async Task MultiResourceRequest_SpaceAndPerson_BothAssigned()
    {
        var spaceId = Guid.NewGuid();
        var personId = Guid.NewGuid();
        var requestId = Guid.NewGuid();

        var personReq = new MultiTypeResourceRequirement(
            ResourceTypeId: PersonTypeId,
            Count: 1,
            RequiredCriterionIds: []);

        var problem = BuildProblem(
            requests: [MakeRequest(requestId, durationDays: 3, additionalRequirements: [personReq])],
            spaces: [MakeSpace(spaceId)],
            additionalResources: [MakePerson(personId)]);

        var analyzed = new SchedulingFeasibilityAnalyzer().Analyze(problem);
        var solution = await new GreedySchedulingSolver().SolveAsync(analyzed, CancellationToken.None);

        Assert.Single(solution.Assignments);
        var assignment = solution.Assignments[0];
        Assert.Equal(requestId, assignment.RequestId);
        Assert.Equal(spaceId, assignment.ResourceId);
        Assert.Contains(personId, assignment.AdditionalResourceIds!);
    }

    [Fact]
    public async Task MultiResourceRequest_NoPerson_RequestUnscheduled()
    {
        var spaceId = Guid.NewGuid();
        var requestId = Guid.NewGuid();

        var personReq = new MultiTypeResourceRequirement(
            ResourceTypeId: PersonTypeId,
            Count: 1,
            RequiredCriterionIds: []);

        var problem = BuildProblem(
            requests: [MakeRequest(requestId, durationDays: 3, additionalRequirements: [personReq])],
            spaces: [MakeSpace(spaceId)],
            additionalResources: []);

        var analyzed = new SchedulingFeasibilityAnalyzer().Analyze(problem);
        var solution = await new GreedySchedulingSolver().SolveAsync(analyzed, CancellationToken.None);

        Assert.Empty(solution.Assignments);
        Assert.Single(solution.Unscheduled);
        Assert.Equal(requestId, solution.Unscheduled[0].RequestId);
    }

    [Fact]
    public async Task MultiResourceRequest_PersonAlreadyOccupied_SecondRequestUnscheduled()
    {
        var personId = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.Today);

        var personReq = new MultiTypeResourceRequirement(
            ResourceTypeId: PersonTypeId,
            Count: 1,
            RequiredCriterionIds: []);

        var requestId1 = Guid.NewGuid();
        var requestId2 = Guid.NewGuid();

        // 5-day requests in a 5-day horizon: both must start on the same day → person conflict.
        var req1 = new RequestNode(requestId1, "Request 1",
            EarliestStart: today, LatestEnd: today.AddDays(4), DurationDays: 5, Priority: 2,
            RespectSchedulingSettings: false, RequiredCriterionIds: new HashSet<Guid>(),
            AdditionalRequirements: [personReq]);
        var req2 = new RequestNode(requestId2, "Request 2",
            EarliestStart: today, LatestEnd: today.AddDays(4), DurationDays: 5, Priority: 1,
            RespectSchedulingSettings: false, RequiredCriterionIds: new HashSet<Guid>(),
            AdditionalRequirements: [personReq]);

        var problem = new SchedulingProblem(
            SiteId: Guid.NewGuid(),
            HorizonStart: today,
            HorizonEnd: today.AddDays(5),
            Requests: [req1, req2],
            Spaces: [MakeSpace(Guid.NewGuid()), MakeSpace(Guid.NewGuid())],
            FixedAssignments: [],
            Settings: null,
            BlockedPeriodsByResource: null,
            Mode: AutoScheduleMode.FillGapsOnly,
            AdditionalResources: [MakePerson(personId)]);

        var analyzed = new SchedulingFeasibilityAnalyzer().Analyze(problem);
        var solution = await new GreedySchedulingSolver().SolveAsync(analyzed, CancellationToken.None);

        // Only one request can get the person
        Assert.Single(solution.Assignments);
        Assert.Single(solution.Unscheduled);
    }
}
