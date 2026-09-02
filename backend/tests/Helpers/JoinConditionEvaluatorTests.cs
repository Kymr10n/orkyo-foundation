using Api.Helpers;
using Api.Models;

namespace Orkyo.Foundation.Tests.Helpers;

/// <summary>
/// The specification for join conditions. Every consumer — critical path, problem builder,
/// conflict detector, execution gate, planner read model — resolves joins through this class, so
/// the rules are pinned here once rather than re-asserted in each consumer's tests.
/// </summary>
public class JoinConditionEvaluatorTests
{
    private static readonly DateTime Now = new(2026, 3, 12, 12, 0, 0, DateTimeKind.Utc);

    // ── RequiredCount ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(PredecessorLogic.All, null, 3, 3)]
    [InlineData(PredecessorLogic.Any, null, 3, 1)]
    [InlineData(PredecessorLogic.KOfN, 2, 3, 2)]
    [InlineData(PredecessorLogic.KOfN, 3, 3, 3)]
    public void RequiredCount_ForTheOrdinaryCases(PredecessorLogic logic, int? k, int live, int expected) =>
        JoinConditionEvaluator.RequiredCount(new JoinCondition(logic, k), live).Should().Be(expected);

    [Theory]
    [InlineData(PredecessorLogic.All)]
    [InlineData(PredecessorLogic.Any)]
    [InlineData(PredecessorLogic.KOfN)]
    public void RequiredCount_IsZeroWhenNothingIsLeftToWaitFor(PredecessorLogic logic) =>
        // An empty live set is met by every logic — including "any", which would otherwise
        // require one predecessor that does not exist and hold the request shut forever.
        JoinConditionEvaluator.RequiredCount(new JoinCondition(logic, 2), 0).Should().Be(0);

    [Fact]
    public void RequiredCount_ClampsAKLargerThanTheLiveSet() =>
        // Edges come and go independently of the stored k, so k=5 over 3 predecessors reads
        // as "all 3" rather than becoming unsatisfiable.
        JoinConditionEvaluator.RequiredCount(new JoinCondition(PredecessorLogic.KOfN, 5), 3).Should().Be(3);

    [Fact]
    public void RequiredCount_ClampsAKBelowOne() =>
        // The CHECK constraint forbids it, but a clamp here means a bad row degrades to
        // "any" instead of making the request start with nothing done.
        JoinConditionEvaluator.RequiredCount(new JoinCondition(PredecessorLogic.KOfN, 0), 3).Should().Be(1);

    [Fact]
    public void RequiredCount_TreatsAMissingKAsAll() =>
        JoinConditionEvaluator.RequiredCount(new JoinCondition(PredecessorLogic.KOfN, null), 4).Should().Be(4);

    // ── IsMet ─────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(PredecessorLogic.All, null, 2, 3, false)]
    [InlineData(PredecessorLogic.All, null, 3, 3, true)]
    [InlineData(PredecessorLogic.Any, null, 0, 3, false)]
    [InlineData(PredecessorLogic.Any, null, 1, 3, true)]
    [InlineData(PredecessorLogic.KOfN, 2, 1, 3, false)]
    [InlineData(PredecessorLogic.KOfN, 2, 2, 3, true)]
    public void IsMet_CountsAgainstTheRequirement(PredecessorLogic logic, int? k, int met, int live, bool expected) =>
        JoinConditionEvaluator.IsMet(new JoinCondition(logic, k), met, live).Should().Be(expected);

    // ── FoldEarliestStart ─────────────────────────────────────────────────────

    private static readonly DateOnly Mar10 = new(2026, 3, 10);
    private static readonly DateOnly Mar12 = new(2026, 3, 12);
    private static readonly DateOnly Mar20 = new(2026, 3, 20);

    [Fact]
    public void Fold_ForAll_TakesTheLatestBound() =>
        JoinConditionEvaluator.FoldEarliestStart(JoinCondition.All, [Mar12, Mar20, Mar10]).Should().Be(Mar20);

    [Fact]
    public void Fold_ForAny_TakesTheEarliestBound() =>
        // One predecessor is enough, so the request is free as soon as the first one clears.
        JoinConditionEvaluator.FoldEarliestStart(new JoinCondition(PredecessorLogic.Any, null), [Mar12, Mar20, Mar10])
            .Should().Be(Mar10);

    [Fact]
    public void Fold_ForKOfN_TakesTheKthEarliestBound() =>
        // 2 of 3: free once the second predecessor has cleared, which is the middle date.
        JoinConditionEvaluator.FoldEarliestStart(new JoinCondition(PredecessorLogic.KOfN, 2), [Mar12, Mar20, Mar10])
            .Should().Be(Mar12);

    [Fact]
    public void Fold_ForKOfN_ClampsKToTheBoundsGiven() =>
        JoinConditionEvaluator.FoldEarliestStart(new JoinCondition(PredecessorLogic.KOfN, 9), [Mar12, Mar10])
            .Should().Be(Mar12);

    [Fact]
    public void Fold_WithTiedBounds_IsStable() =>
        JoinConditionEvaluator.FoldEarliestStart(new JoinCondition(PredecessorLogic.KOfN, 2), [Mar12, Mar12, Mar20])
            .Should().Be(Mar12);

    [Fact]
    public void Fold_WithASingleBound_ReturnsItForEveryLogic()
    {
        foreach (var logic in new[] { PredecessorLogic.All, PredecessorLogic.Any, PredecessorLogic.KOfN })
            JoinConditionEvaluator.FoldEarliestStart(new JoinCondition(logic, 1), [Mar12]).Should().Be(Mar12);
    }

    [Fact]
    public void Fold_WithNoBounds_ConstrainsNothing() =>
        JoinConditionEvaluator.FoldEarliestStart(JoinCondition.All, []).Should().BeNull();

    [Fact]
    public void Fold_DoesNotReorderTheCallersList()
    {
        var bounds = new List<DateOnly> { Mar20, Mar10, Mar12 };
        JoinConditionEvaluator.FoldEarliestStart(JoinCondition.All, bounds);
        bounds.Should().Equal(Mar20, Mar10, Mar12);
    }

    // ── EvaluateGate ──────────────────────────────────────────────────────────

    private static PredecessorState Done(string name) =>
        new(name, RequestStatus.New, Now.AddDays(-3), Now.AddDays(-1));   // window has passed → Done
    private static PredecessorState Running(string name) =>
        new(name, RequestStatus.New, Now.AddDays(-1), Now.AddDays(1));    // inside window → InProgress
    private static PredecessorState Unscheduled(string name) =>
        new(name, RequestStatus.New, null, null);                          // → New
    private static PredecessorState Cancelled(string name) =>
        new(name, RequestStatus.Cancelled, Now.AddDays(-3), Now.AddDays(-1));
    private static PredecessorState Deferred(string name) =>
        new(name, RequestStatus.Deferred, null, null);

    [Fact]
    public void Gate_WithNoPredecessors_IsMet()
    {
        var result = JoinConditionEvaluator.EvaluateGate(JoinCondition.All, [], Now);

        result.IsMet.Should().BeTrue();
        result.LiveCount.Should().Be(0);
        result.RequiredCount.Should().Be(0);
    }

    [Fact]
    public void Gate_ForAll_NeedsEveryLivePredecessorDone()
    {
        var result = JoinConditionEvaluator.EvaluateGate(
            JoinCondition.All, [Done("Cut"), Running("Weld")], Now);

        result.IsMet.Should().BeFalse();
        result.MetCount.Should().Be(1);
        result.LiveCount.Should().Be(2);
        result.UnmetNames.Should().Equal("Weld");
    }

    [Fact]
    public void Gate_ForAny_IsMetByOne()
    {
        var result = JoinConditionEvaluator.EvaluateGate(
            new JoinCondition(PredecessorLogic.Any, null),
            [Done("Supplier A"), Unscheduled("Supplier B")], Now);

        result.IsMet.Should().BeTrue();
        result.RequiredCount.Should().Be(1);
    }

    [Fact]
    public void Gate_ForKOfN_NeedsK()
    {
        var predecessors = new[] { Done("A"), Done("B"), Unscheduled("C") };

        JoinConditionEvaluator.EvaluateGate(new JoinCondition(PredecessorLogic.KOfN, 2), predecessors, Now)
            .IsMet.Should().BeTrue();
        JoinConditionEvaluator.EvaluateGate(new JoinCondition(PredecessorLogic.KOfN, 3), predecessors, Now)
            .IsMet.Should().BeFalse();
    }

    [Fact]
    public void Gate_CountsAPredecessorMarkedDoneWithoutEverBeingScheduled()
    {
        // Derivation returns "new" for anything without dates, whatever the column says. Counting
        // only the derived value held such a predecessor's successors shut forever, with no way
        // out but deleting the edge — and dependencies are about order, not placement.
        var markedDone = new PredecessorState("Permit approved", RequestStatus.Done, null, null);

        var result = JoinConditionEvaluator.EvaluateGate(JoinCondition.All, [markedDone], Now);

        result.IsMet.Should().BeTrue();
        result.MetCount.Should().Be(1);
        result.UnmetNames.Should().BeEmpty();
    }

    [Fact]
    public void Gate_StillRefusesAPredecessorThatIsNeitherStoredNorDerivedDone()
    {
        var running = new PredecessorState("Weld", RequestStatus.InProgress, Now.AddDays(-1), Now.AddDays(1));

        JoinConditionEvaluator.EvaluateGate(JoinCondition.All, [running], Now)
            .IsMet.Should().BeFalse();
    }

    [Fact]
    public void Gate_ExcludesCancelledAndDeferredPredecessors()
    {
        // The rule that keeps an "all" join from deadlocking behind work that will never run.
        var result = JoinConditionEvaluator.EvaluateGate(
            JoinCondition.All, [Done("Cut"), Cancelled("Scrapped"), Deferred("Postponed")], Now);

        result.IsMet.Should().BeTrue();
        result.LiveCount.Should().Be(1);
        result.UnmetNames.Should().BeEmpty();
    }

    [Fact]
    public void Gate_WithEveryPredecessorCancelled_IsMet()
    {
        var result = JoinConditionEvaluator.EvaluateGate(
            new JoinCondition(PredecessorLogic.Any, null), [Cancelled("A"), Cancelled("B")], Now);

        result.IsMet.Should().BeTrue();
        result.LiveCount.Should().Be(0);
    }

    [Fact]
    public void Gate_ClampsKToTheLiveSetAfterExclusions()
    {
        // k=3 over 3 edges, but one is cancelled → 2 live, so two done predecessors satisfy it.
        var result = JoinConditionEvaluator.EvaluateGate(
            new JoinCondition(PredecessorLogic.KOfN, 3),
            [Done("A"), Done("B"), Cancelled("C")], Now);

        result.IsMet.Should().BeTrue();
        result.RequiredCount.Should().Be(2);
    }

    [Theory]
    [InlineData(RequestStatus.New)]
    [InlineData(RequestStatus.InProgress)]
    [InlineData(RequestStatus.Done)]
    [InlineData(RequestStatus.Cancelled)]
    [InlineData(RequestStatus.Deferred)]
    public void Gate_TreatsAnAlreadyEffectiveStatusTheSameAsAStoredOne(RequestStatus stored)
    {
        // RequestService hands the gate a RequestInfo.Status, which the mapper has already run
        // through RequestStatusCalculator. That is only sound if derivation is idempotent, so it
        // is asserted here rather than assumed at the call site.
        var start = Now.AddDays(-3);
        var end = Now.AddDays(-1);

        var once = RequestStatusCalculator.Effective(stored, start, end, Now);
        var twice = RequestStatusCalculator.Effective(once, start, end, Now);

        twice.Should().Be(once);
    }

    // ── DescribeShortfall ─────────────────────────────────────────────────────

    [Fact]
    public void Describe_ForAll_NamesThemAll() =>
        JoinConditionEvaluator.DescribeShortfall(3, 3, 1).Should().Be("all 3 predecessors must be done; 1 is");

    [Fact]
    public void Describe_ForAPartialRequirement_NamesTheCount() =>
        JoinConditionEvaluator.DescribeShortfall(2, 3, 1).Should().Be("2 of 3 predecessors must be done; 1 is");

    [Fact]
    public void Describe_AgreesWithTheVerbForPluralCounts() =>
        JoinConditionEvaluator.DescribeShortfall(2, 3, 2).Should().Be("2 of 3 predecessors must be done; 2 are");

    [Fact]
    public void Describe_ForASinglePredecessor_ReadsNaturally() =>
        JoinConditionEvaluator.DescribeShortfall(1, 1, 0).Should().Be("all 1 predecessor must be done; 0 are");
}
