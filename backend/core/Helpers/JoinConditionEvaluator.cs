using Api.Models;

namespace Api.Helpers;

/// <summary>
/// A request's join condition: which of its predecessors must be met before it may start.
/// </summary>
/// <param name="Logic">all, any, or k_of_n.</param>
/// <param name="K">The k of k_of_n; null otherwise. May exceed the number of predecessors.</param>
public readonly record struct JoinCondition(PredecessorLogic Logic, int? K)
{
    public static JoinCondition Of(PredecessorLogic logic, int? k) => new(logic, k);

    /// <summary>The condition a request carries.</summary>
    public static JoinCondition Of(RequestInfo request) => new(request.PredecessorLogic, request.PredecessorLogicK);

    /// <summary>The condition every request had before the concept existed.</summary>
    public static JoinCondition All => new(PredecessorLogic.All, null);
}

/// <summary>
/// Evaluates join conditions. This is the SINGLE source of truth for what "the predecessors are
/// met" means — the critical path, the auto-schedule problem builder, the conflict detector, the
/// execution gate and the planner read model all resolve their joins through here, so none of
/// them can drift from the others.
///
/// Two rules govern every method:
///
/// <list type="number">
/// <item><description><b>Cancelled and deferred predecessors leave the set.</b> Callers filter
/// them out with <see cref="RequestStatusCalculator.Effective"/> before calling, so n shrinks and
/// k clamps with it. Without that an "all" join would stay shut forever behind a cancelled
/// predecessor, with no way out but deleting the edge and losing the record that it existed. An
/// empty live set counts as met — there is nothing left to wait for.</description></item>
/// <item><description><b>k is clamped, never trusted.</b> Edges are added and removed
/// independently of the stored k, so a k of 5 on a node with 3 live predecessors means "all 3".
/// The database only shape-checks k (>= 1, present exactly for k_of_n); the clamp lives
/// here.</description></item>
/// </list>
/// </summary>
public static class JoinConditionEvaluator
{
    /// <summary>
    /// How many of <paramref name="liveCount"/> predecessors must be met. Always in
    /// [0, liveCount], so an empty live set requires nothing.
    /// </summary>
    public static int RequiredCount(JoinCondition condition, int liveCount)
    {
        if (liveCount <= 0) return 0;

        return condition.Logic switch
        {
            PredecessorLogic.All => liveCount,
            PredecessorLogic.Any => 1,
            PredecessorLogic.KOfN => Math.Clamp(condition.K ?? liveCount, 1, liveCount),
            _ => liveCount,
        };
    }

    /// <summary>True when <paramref name="metCount"/> of <paramref name="liveCount"/> satisfies the condition.</summary>
    public static bool IsMet(JoinCondition condition, int metCount, int liveCount)
        => metCount >= RequiredCount(condition, liveCount);

    /// <summary>
    /// Folds one candidate earliest-start per live predecessor into the single date this request
    /// may start on: the latest of them for "all", the earliest for "any", and the k-th earliest
    /// for k_of_n — the day the k-th predecessor frees it. Null when nothing constrains it.
    /// </summary>
    public static DateOnly? FoldEarliestStart(JoinCondition condition, IReadOnlyList<DateOnly> bounds)
    {
        if (bounds.Count == 0) return null;

        var required = RequiredCount(condition, bounds.Count);
        if (required <= 0) return null;

        // Sorting a copy: the caller's list is theirs, and these lists are a handful of entries.
        var ordered = bounds.ToArray();
        Array.Sort(ordered);

        // The k-th earliest (1-based) is the point at which k predecessors have cleared.
        // "all" lands on the last element and "any" on the first, so one expression covers all three.
        return ordered[required - 1];
    }

    /// <summary>
    /// Evaluates the execution gate for a request whose predecessors are in
    /// <paramref name="predecessors"/>, as stored status plus schedule so effective status can be
    /// derived here rather than by each caller.
    /// </summary>
    public static JoinGateResult EvaluateGate(
        JoinCondition condition,
        IEnumerable<PredecessorState> predecessors,
        DateTime now)
    {
        var live = predecessors
            .Select(p => (
                p.Name,
                p.StoredStatus,
                Effective: RequestStatusCalculator.Effective(p.StoredStatus, p.StartTs, p.EndTs, now)))
            .Where(p => p.Effective is not (RequestStatus.Cancelled or RequestStatus.Deferred))
            .ToList();

        // Met by EITHER reading. Effective catches work whose scheduled window has passed, which
        // is how most work completes here. Stored catches the rest: a request finished without
        // ever being scheduled derives to "new" no matter what its column says, and counting only
        // the derived value would hold its successors shut forever with no way out but deleting
        // the edge. Dependencies are about order, not placement — an unscheduled predecessor
        // marked done really is done.
        var met = live.Count(p => p.Effective == RequestStatus.Done || p.StoredStatus == RequestStatus.Done);
        var required = RequiredCount(condition, live.Count);

        return new JoinGateResult(
            IsMet: met >= required,
            MetCount: met,
            LiveCount: live.Count,
            RequiredCount: required,
            UnmetNames: live
                .Where(p => p.Effective != RequestStatus.Done && p.StoredStatus != RequestStatus.Done)
                .Select(p => p.Name)
                .ToList());
    }

    /// <summary>
    /// True when a request will not happen, so it constrains nothing downstream. Every consumer
    /// drops such predecessors before folding or counting — the rule that keeps an "all" join
    /// from waiting forever on abandoned work.
    /// </summary>
    public static bool IsAbandoned(RequestInfo request, DateTime now)
        => RequestStatusCalculator.Effective(request.Status, request.StartTs, request.EndTs, now)
            is RequestStatus.Cancelled or RequestStatus.Deferred;

    /// <summary>
    /// One sentence naming what is still missing, shared by the execution gate's refusal and the
    /// scheduling conflict's message so a user never sees the same shortfall described two ways.
    /// </summary>
    public static string DescribeShortfall(int requiredCount, int liveCount, int metCount)
        => requiredCount >= liveCount
            ? $"all {liveCount} predecessor{(liveCount == 1 ? "" : "s")} must be done; {metCount} {(metCount == 1 ? "is" : "are")}"
            : $"{requiredCount} of {liveCount} predecessors must be done; {metCount} {(metCount == 1 ? "is" : "are")}";
}

/// <summary>
/// A predecessor as the gate needs it. <paramref name="StoredStatus"/> must be the value in the
/// column, NOT <see cref="RequestInfo.Status"/> — that one has already been through
/// <see cref="RequestStatusCalculator"/>, which erases a manual <c>done</c> on unscheduled work.
/// </summary>
public readonly record struct PredecessorState(string Name, RequestStatus StoredStatus, DateTime? StartTs, DateTime? EndTs);

/// <summary>The outcome of an execution-gate evaluation, carrying the counts the message needs.</summary>
public readonly record struct JoinGateResult(
    bool IsMet,
    int MetCount,
    int LiveCount,
    int RequiredCount,
    IReadOnlyList<string> UnmetNames);
