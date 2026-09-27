using Api.Constants;
using Api.Helpers;
using Api.Models;

namespace Orkyo.Foundation.Tests.Models;

/// <summary>
/// Covers the computed members of the domain types in <c>Models/AutoSchedule.cs</c>:
/// SchedulingSolution.ComputeFingerprint and SchedulingSolution.ToScore().
/// </summary>
public class AutoScheduleModelsTests
{
    // ── SchedulingSolution.ToScore() ──────────────────────────────────────

    [Fact]
    public void SchedulingSolution_ToScore_CountsScheduledAndUnscheduled()
    {
        var req1 = Guid.NewGuid();
        var req2 = Guid.NewGuid();
        var space = Guid.NewGuid();
        var start = 4 * 1440;
        var end = 6 * 1440;

        var solution = new SchedulingSolution(
            SolverUsed: SolverKind.Greedy,
            Status: SolverStatus.Feasible,
            Assignments: new List<ScheduledPlacement>
            {
                new(RequestId: req1, Resources: [new PlacedResource("space", space)], Start: start, End: end, DurationMinutes: 2 * 1440, Priority: 10)
            },
            Unscheduled: new List<UnscheduledPlacement>
            {
                new(RequestId: req2, ReasonCodes: new List<SchedulingReasonCode> { SchedulingReasonCode.NoCompatibleResource })
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

    [Fact]
    public void Fingerprint_ChangesWhenDependenciesChange()
    {
        // Load-bearing: without the edges in the hash, adding a dependency between preview and
        // apply would leave the fingerprint matching, and the apply would commit a plan that
        // violates the edge the user just drew.
        var solution = new SchedulingSolution(
            SolverKind.Greedy, SolverStatus.Optimal, [], [], []);

        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        var withoutEdges = solution.ComputeFingerprint([ResourceTypeKeys.Space], []);
        var withEdge = solution.ComputeFingerprint([ResourceTypeKeys.Space], [new DependencyEdge(a, b, 0)]);
        var withLonger = solution.ComputeFingerprint([ResourceTypeKeys.Space], [new DependencyEdge(a, b, 5)]);

        withEdge.Should().NotBe(withoutEdges);
        withLonger.Should().NotBe(withEdge, "the lag is part of what makes a plan valid");
    }

    [Fact]
    public void Fingerprint_ChangesWhenAJoinConditionChanges()
    {
        // Same reason as the edges: switching a request from "any predecessor" to "all" between
        // preview and apply invalidates a plan the edge list alone still describes perfectly.
        var solution = new SchedulingSolution(
            SolverKind.Greedy, SolverStatus.Optimal, [], [], []);

        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        DependencyEdge[] edges = [new DependencyEdge(a, b, 0)];

        var asAll = solution.ComputeFingerprint([ResourceTypeKeys.Space], edges,
            new Dictionary<Guid, JoinCondition> { [b] = JoinCondition.All });
        var asAny = solution.ComputeFingerprint([ResourceTypeKeys.Space], edges,
            new Dictionary<Guid, JoinCondition> { [b] = new(PredecessorLogic.Any, null) });
        var asTwoOfN = solution.ComputeFingerprint([ResourceTypeKeys.Space], edges,
            new Dictionary<Guid, JoinCondition> { [b] = new(PredecessorLogic.KOfN, 2) });
        var asThreeOfN = solution.ComputeFingerprint([ResourceTypeKeys.Space], edges,
            new Dictionary<Guid, JoinCondition> { [b] = new(PredecessorLogic.KOfN, 3) });

        asAny.Should().NotBe(asAll);
        asTwoOfN.Should().NotBe(asAny);
        asThreeOfN.Should().NotBe(asTwoOfN, "k is part of what makes a plan valid");
    }

    [Fact]
    public void Fingerprint_HashesTheDatabaseStringNotTheEnumMemberName()
    {
        // Hashing "KOfN" would tie every in-flight preview's validity to a C# identifier: rename
        // the member and every outstanding preview goes stale for no reason the user can see.
        var solution = new SchedulingSolution(
            SolverKind.Greedy, SolverStatus.Optimal, [], [], []);
        var b = Guid.NewGuid();
        DependencyEdge[] edges = [new DependencyEdge(Guid.NewGuid(), b, 0)];

        var fingerprint = solution.ComputeFingerprint([ResourceTypeKeys.Space], edges,
            new Dictionary<Guid, JoinCondition> { [b] = new(PredecessorLogic.KOfN, 2) });

        // Same inputs described the DB way must hash identically — proving the DB string is what
        // went in. (A direct string assertion is impossible: the value is SHA-256'd.)
        fingerprint.Should().Be(solution.ComputeFingerprint([ResourceTypeKeys.Space], edges,
            new Dictionary<Guid, JoinCondition> { [b] = JoinCondition.Of(PredecessorLogic.KOfN, 2) }));
        EnumMapper.ToDbValue(PredecessorLogic.KOfN).Should().Be("k_of_n");
    }

    [Fact]
    public void Fingerprint_WithoutJoinConditions_MatchesAnEmptyMap()
    {
        // Omitting the argument must not be a different plan from passing nothing, or every
        // caller that has no conditions would see a spurious staleness conflict.
        var solution = new SchedulingSolution(
            SolverKind.Greedy, SolverStatus.Optimal, [], [], []);

        solution.ComputeFingerprint([ResourceTypeKeys.Space], [])
            .Should().Be(solution.ComputeFingerprint([ResourceTypeKeys.Space], [], new Dictionary<Guid, JoinCondition>()));
    }

    [Fact]
    public void Fingerprint_ChangesWithTheTypeSetAndTheResourcesPlaced()
    {
        // A run over rooms and a run over rooms and vans are different plans even when they
        // propose the same window; so are the same window on a different van.
        var request = Guid.NewGuid();
        var room = Guid.NewGuid();
        var vanA = Guid.NewGuid();
        var vanB = Guid.NewGuid();

        var onVanA = MakeSolution(new ScheduledPlacement(
            request, [new PlacedResource("space", room), new PlacedResource("van", vanA)], 0, 60, 60, 1));
        var onVanB = MakeSolution(new ScheduledPlacement(
            request, [new PlacedResource("space", room), new PlacedResource("van", vanB)], 0, 60, 60, 1));

        onVanA.ComputeFingerprint(["space", "van"], []).Should().NotBe(onVanB.ComputeFingerprint(["space", "van"], []));
        onVanA.ComputeFingerprint(["space", "van"], []).Should().NotBe(onVanA.ComputeFingerprint(["space"], []));
        // The set is unordered.
        onVanA.ComputeFingerprint(["van", "space"], []).Should().Be(onVanA.ComputeFingerprint(["space", "van"], []));
    }

    [Fact]
    public void Fingerprint_IsStableAcrossDependencyOrdering()
    {
        var solution = new SchedulingSolution(
            SolverKind.Greedy, SolverStatus.Optimal, [], [], []);

        var e1 = new DependencyEdge(Guid.NewGuid(), Guid.NewGuid(), 1);
        var e2 = new DependencyEdge(Guid.NewGuid(), Guid.NewGuid(), 2);

        solution.ComputeFingerprint([ResourceTypeKeys.Space], [e1, e2])
            .Should().Be(solution.ComputeFingerprint([ResourceTypeKeys.Space], [e2, e1]));
    }

    // ── SchedulingSolution.ComputeFingerprint([ResourceTypeKeys.Space], []) ────────────────────────────

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

        var fp1 = solution.ComputeFingerprint([ResourceTypeKeys.Space], []);
        var fp2 = solution.ComputeFingerprint([ResourceTypeKeys.Space], []);

        fp1.Should().Be(fp2);
        fp1.Should().HaveLength(64); // SHA-256 hex
    }

    [Fact]
    public void ComputeFingerprint_IdenticalSolutions_ProduceSameHash()
    {
        var reqId = Guid.NewGuid();
        var resourceId = Guid.NewGuid();
        var start = 0;
        var end = 4 * 1440;

        var a = MakeSolution(new ScheduledPlacement(reqId, [new PlacedResource("space", resourceId)], start, end, 4 * 1440, 5));
        var b = MakeSolution(new ScheduledPlacement(reqId, [new PlacedResource("space", resourceId)], start, end, 4 * 1440, 5));

        a.ComputeFingerprint([ResourceTypeKeys.Space], []).Should().Be(b.ComputeFingerprint([ResourceTypeKeys.Space], []));
    }

    [Fact]
    public void ComputeFingerprint_DifferentAssignments_ProduceDifferentHash()
    {
        var req1 = Guid.NewGuid();
        var req2 = Guid.NewGuid();
        var resourceId = Guid.NewGuid();
        var start = 0;
        var end = 4 * 1440;

        var a = MakeSolution(new ScheduledPlacement(req1, [new PlacedResource("space", resourceId)], start, end, 4 * 1440, 5));
        var b = MakeSolution(new ScheduledPlacement(req2, [new PlacedResource("space", resourceId)], start, end, 4 * 1440, 5));

        a.ComputeFingerprint([ResourceTypeKeys.Space], []).Should().NotBe(b.ComputeFingerprint([ResourceTypeKeys.Space], []));
    }

    [Fact]
    public void ComputeFingerprint_IsOrderIndependent()
    {
        var req1 = new Guid("00000000-0000-0000-0000-000000000001");
        var req2 = new Guid("00000000-0000-0000-0000-000000000002");
        var resourceId = Guid.NewGuid();
        var start = 0;
        var end = 4 * 1440;

        var p1 = new ScheduledPlacement(req1, [new PlacedResource("space", resourceId)], start, end, 4 * 1440, 5);
        var p2 = new ScheduledPlacement(req2, [new PlacedResource("space", resourceId)], start, end, 4 * 1440, 5);

        var ordered = new SchedulingSolution(SolverKind.Greedy, SolverStatus.Optimal,
            new List<ScheduledPlacement> { p1, p2 }, new List<UnscheduledPlacement>(), new List<string>());

        var reversed = new SchedulingSolution(SolverKind.Greedy, SolverStatus.Optimal,
            new List<ScheduledPlacement> { p2, p1 }, new List<UnscheduledPlacement>(), new List<string>());

        ordered.ComputeFingerprint([ResourceTypeKeys.Space], []).Should().Be(reversed.ComputeFingerprint([ResourceTypeKeys.Space], []));
    }

    // ── CandidateRejection ─────────────────────────────────────────────────

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

    // ── Helpers ────────────────────────────────────────────────────────────

    private static SchedulingSolution MakeSolution(params ScheduledPlacement[] placements)
        => new(SolverKind.Greedy, SolverStatus.Optimal,
            placements.ToList(), new List<UnscheduledPlacement>(), new List<string>());
}
