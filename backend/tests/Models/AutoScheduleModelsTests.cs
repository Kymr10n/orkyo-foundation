using Api.Models;

namespace Orkyo.Foundation.Tests.Models;

/// <summary>
/// Covers the uncovered domain types in <c>Models/AutoSchedule.cs</c>:
/// enums, SchedulingSolution.ComputeFingerprint(), SchedulingSolution.ToScore(),
/// and all internal record types.
/// </summary>
public class AutoScheduleModelsTests
{
    // ── Enum values ────────────────────────────────────────────────────────

    [Theory]
    [InlineData(SolverKind.Greedy)]
    [InlineData(SolverKind.OrToolsCpSat)]
    public void SolverKind_AllValues_AreDefined(SolverKind kind)
    {
        Enum.IsDefined(kind).Should().BeTrue();
    }

    [Theory]
    [InlineData(SolverStatus.Optimal)]
    [InlineData(SolverStatus.Feasible)]
    [InlineData(SolverStatus.Infeasible)]
    [InlineData(SolverStatus.Unknown)]
    public void SolverStatus_AllValues_AreDefined(SolverStatus status)
    {
        Enum.IsDefined(status).Should().BeTrue();
    }

    [Theory]
    [InlineData(SchedulingReasonCode.NoCompatibleSpace)]
    [InlineData(SchedulingReasonCode.InsufficientCapacity)]
    [InlineData(SchedulingReasonCode.BlockedByFixedAssignments)]
    [InlineData(SchedulingReasonCode.InvalidDuration)]
    [InlineData(SchedulingReasonCode.InternalSolverLimit)]
    public void SchedulingReasonCode_AllValues_AreDefined(SchedulingReasonCode code)
    {
        Enum.IsDefined(code).Should().BeTrue();
    }

    // ── SchedulingSolution.ToScore() ──────────────────────────────────────

    [Fact]
    public void SchedulingSolution_ToScore_CountsScheduledAndUnscheduled()
    {
        var req1 = Guid.NewGuid();
        var req2 = Guid.NewGuid();
        var space = Guid.NewGuid();
        var start = new DateOnly(2026, 1, 5);
        var end = new DateOnly(2026, 1, 7);

        var solution = new SchedulingSolution(
            SolverUsed: SolverKind.Greedy,
            Status: SolverStatus.Feasible,
            Assignments: new List<ScheduledPlacement>
            {
                new(RequestId: req1, ResourceId: space, Start: start, End: end, DurationDays: 2, Priority: 10)
            },
            Unscheduled: new List<UnscheduledPlacement>
            {
                new(RequestId: req2, ReasonCodes: new List<SchedulingReasonCode> { SchedulingReasonCode.NoCompatibleSpace })
            },
            Diagnostics: new List<string>()
        );

        var score = solution.ToScore();

        score.ScheduledCount.Should().Be(1);
        score.UnscheduledCount.Should().Be(1);
        score.PriorityScore.Should().Be(10);
    }

    [Fact]
    public void SchedulingSolution_ToScore_EmptySolution_AllZero()
    {
        var solution = new SchedulingSolution(
            SolverUsed: SolverKind.Greedy,
            Status: SolverStatus.Infeasible,
            Assignments: new List<ScheduledPlacement>(),
            Unscheduled: new List<UnscheduledPlacement>(),
            Diagnostics: new List<string>()
        );

        var score = solution.ToScore();

        score.ScheduledCount.Should().Be(0);
        score.UnscheduledCount.Should().Be(0);
        score.PriorityScore.Should().Be(0);
    }

    // ── SchedulingSolution.ComputeFingerprint() ────────────────────────────

    [Fact]
    public void ComputeFingerprint_EmptySolution_ProducesConsistentHash()
    {
        var solution = new SchedulingSolution(
            SolverUsed: SolverKind.Greedy,
            Status: SolverStatus.Infeasible,
            Assignments: new List<ScheduledPlacement>(),
            Unscheduled: new List<UnscheduledPlacement>(),
            Diagnostics: new List<string>()
        );

        var fp1 = solution.ComputeFingerprint();
        var fp2 = solution.ComputeFingerprint();

        fp1.Should().Be(fp2);
        fp1.Should().HaveLength(64); // SHA-256 hex
    }

    [Fact]
    public void ComputeFingerprint_IdenticalSolutions_ProduceSameHash()
    {
        var reqId = Guid.NewGuid();
        var resourceId = Guid.NewGuid();
        var start = new DateOnly(2026, 3, 1);
        var end = new DateOnly(2026, 3, 5);

        var a = MakeSolution(new ScheduledPlacement(reqId, resourceId, start, end, 4, 5));
        var b = MakeSolution(new ScheduledPlacement(reqId, resourceId, start, end, 4, 5));

        a.ComputeFingerprint().Should().Be(b.ComputeFingerprint());
    }

    [Fact]
    public void ComputeFingerprint_DifferentAssignments_ProduceDifferentHash()
    {
        var req1 = Guid.NewGuid();
        var req2 = Guid.NewGuid();
        var resourceId = Guid.NewGuid();
        var start = new DateOnly(2026, 3, 1);
        var end = new DateOnly(2026, 3, 5);

        var a = MakeSolution(new ScheduledPlacement(req1, resourceId, start, end, 4, 5));
        var b = MakeSolution(new ScheduledPlacement(req2, resourceId, start, end, 4, 5));

        a.ComputeFingerprint().Should().NotBe(b.ComputeFingerprint());
    }

    [Fact]
    public void ComputeFingerprint_IsOrderIndependent()
    {
        var req1 = new Guid("00000000-0000-0000-0000-000000000001");
        var req2 = new Guid("00000000-0000-0000-0000-000000000002");
        var resourceId = Guid.NewGuid();
        var start = new DateOnly(2026, 3, 1);
        var end = new DateOnly(2026, 3, 5);

        var p1 = new ScheduledPlacement(req1, resourceId, start, end, 4, 5);
        var p2 = new ScheduledPlacement(req2, resourceId, start, end, 4, 5);

        var ordered = new SchedulingSolution(SolverKind.Greedy, SolverStatus.Optimal,
            new List<ScheduledPlacement> { p1, p2 }, new List<UnscheduledPlacement>(), new List<string>());

        var reversed = new SchedulingSolution(SolverKind.Greedy, SolverStatus.Optimal,
            new List<ScheduledPlacement> { p2, p1 }, new List<UnscheduledPlacement>(), new List<string>());

        ordered.ComputeFingerprint().Should().Be(reversed.ComputeFingerprint());
    }

    // ── RequestNode ────────────────────────────────────────────────────────

    [Fact]
    public void RequestNode_StoresAllFields()
    {
        var reqId = Guid.NewGuid();
        var criterionId = Guid.NewGuid();

        var node = new RequestNode(
            RequestId: reqId,
            DisplayName: "Install HVAC",
            EarliestStart: new DateOnly(2026, 1, 1),
            LatestEnd: new DateOnly(2026, 12, 31),
            DurationDays: 5,
            Priority: 10,
            RespectSchedulingSettings: true,
            RequiredCriterionIds: new HashSet<Guid> { criterionId });

        node.RequestId.Should().Be(reqId);
        node.DurationDays.Should().Be(5);
        node.Priority.Should().Be(10);
        node.RequiredCriterionIds.Should().Contain(criterionId);
    }

    // ── SpaceNode ──────────────────────────────────────────────────────────

    [Fact]
    public void SpaceNode_StoresAllFields()
    {
        var resourceId = Guid.NewGuid();
        var criterionId = Guid.NewGuid();

        var node = new SpaceNode(
            ResourceId: resourceId,
            DisplayName: "Hall A",
            CriterionIds: new HashSet<Guid> { criterionId });

        node.ResourceId.Should().Be(resourceId);
        node.DisplayName.Should().Be("Hall A");
        node.CriterionIds.Should().Contain(criterionId);
    }

    // ── FixedOccupancy ─────────────────────────────────────────────────────

    [Fact]
    public void FixedOccupancy_StoresAllFields()
    {
        var reqId = Guid.NewGuid();
        var resourceId = Guid.NewGuid();
        var start = new DateOnly(2026, 5, 1);
        var end = new DateOnly(2026, 5, 10);

        var occ = new FixedOccupancy(reqId, resourceId, start, end);

        occ.RequestId.Should().Be(reqId);
        occ.ResourceId.Should().Be(resourceId);
        occ.Start.Should().Be(start);
        occ.End.Should().Be(end);
    }

    // ── SchedulingProblem ──────────────────────────────────────────────────

    [Fact]
    public void SchedulingProblem_StoresAllFields()
    {
        var siteId = Guid.NewGuid();
        var start = new DateOnly(2026, 1, 1);
        var end = new DateOnly(2026, 12, 31);

        var problem = new SchedulingProblem(
            SiteId: siteId,
            HorizonStart: start,
            HorizonEnd: end,
            Requests: new List<RequestNode>(),
            Spaces: new List<SpaceNode>(),
            FixedAssignments: new List<FixedOccupancy>(),
            Settings: null,
            BlockedPeriodsByResource: null);

        problem.SiteId.Should().Be(siteId);
        problem.HorizonStart.Should().Be(start);
    }

    // ── SchedulingCandidate ────────────────────────────────────────────────

    [Fact]
    public void SchedulingCandidate_StoresAllFields()
    {
        var reqId = Guid.NewGuid();
        var resourceId = Guid.NewGuid();

        var candidate = new SchedulingCandidate(
            RequestId: reqId,
            ResourceId: resourceId,
            EarliestStart: new DateOnly(2026, 1, 1),
            LatestEnd: new DateOnly(2026, 6, 30),
            DurationDays: 5,
            Priority: 7,
            FeasibleStartDays: new List<DateOnly> { new(2026, 1, 5), new(2026, 1, 6) });

        candidate.RequestId.Should().Be(reqId);
        candidate.FeasibleStartDays.Should().HaveCount(2);
    }

    // ── CandidateRejection ─────────────────────────────────────────────────

    [Fact]
    public void CandidateRejection_StoresAllFields()
    {
        var reqId = Guid.NewGuid();
        var resourceId = Guid.NewGuid();

        var rejection = new CandidateRejection(
            RequestId: reqId,
            ResourceId: resourceId,
            ReasonCode: SchedulingReasonCode.NoCompatibleSpace,
            Message: "No space matches criteria");

        rejection.RequestId.Should().Be(reqId);
        rejection.ReasonCode.Should().Be(SchedulingReasonCode.NoCompatibleSpace);
        rejection.Message.Should().Be("No space matches criteria");
    }

    [Fact]
    public void CandidateRejection_OptionalFields_AreNullByDefault()
    {
        var reqId = Guid.NewGuid();

        var rejection = new CandidateRejection(
            RequestId: reqId,
            ResourceId: null,
            ReasonCode: SchedulingReasonCode.InvalidDuration);

        rejection.ResourceId.Should().BeNull();
        rejection.Message.Should().BeNull();
    }

    // ── AnalyzedSchedulingProblem ──────────────────────────────────────────

    [Fact]
    public void AnalyzedSchedulingProblem_StoresAllFields()
    {
        var siteId = Guid.NewGuid();
        var start = new DateOnly(2026, 1, 1);
        var end = new DateOnly(2026, 12, 31);

        var problem = new SchedulingProblem(
            SiteId: siteId,
            HorizonStart: start,
            HorizonEnd: end,
            Requests: new List<RequestNode>(),
            Spaces: new List<SpaceNode>(),
            FixedAssignments: new List<FixedOccupancy>(),
            Settings: null,
            BlockedPeriodsByResource: null);

        var analyzed = new AnalyzedSchedulingProblem(
            Problem: problem,
            Candidates: new List<SchedulingCandidate>(),
            Rejections: new List<CandidateRejection>(),
            Diagnostics: new List<string> { "No spaces available" });

        analyzed.Problem.SiteId.Should().Be(siteId);
        analyzed.Diagnostics.Should().HaveCount(1);
    }

    // ── ScheduledPlacement / UnscheduledPlacement ──────────────────────────

    [Fact]
    public void ScheduledPlacement_StoresAllFields()
    {
        var reqId = Guid.NewGuid();
        var resourceId = Guid.NewGuid();

        var placement = new ScheduledPlacement(
            RequestId: reqId,
            ResourceId: resourceId,
            Start: new DateOnly(2026, 2, 1),
            End: new DateOnly(2026, 2, 5),
            DurationDays: 4,
            Priority: 9);

        placement.RequestId.Should().Be(reqId);
        placement.DurationDays.Should().Be(4);
        placement.Priority.Should().Be(9);
    }

    [Fact]
    public void UnscheduledPlacement_StoresAllFields()
    {
        var reqId = Guid.NewGuid();

        var placement = new UnscheduledPlacement(
            RequestId: reqId,
            ReasonCodes: new List<SchedulingReasonCode>
            {
                SchedulingReasonCode.InsufficientCapacity,
                SchedulingReasonCode.NoCompatibleSpace
            });

        placement.RequestId.Should().Be(reqId);
        placement.ReasonCodes.Should().HaveCount(2);
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static SchedulingSolution MakeSolution(params ScheduledPlacement[] placements)
        => new(SolverKind.Greedy, SolverStatus.Optimal,
            placements.ToList(), new List<UnscheduledPlacement>(), new List<string>());
}
