using Api.Models;

namespace Api.Services.AutoSchedule;

/// <summary>
/// Greedy earliest-fit solver. Assigns requests one at a time in priority order,
/// picking the earliest feasible start on the first compatible resource.
/// Acts as fallback when OR-Tools is unavailable or times out.
/// </summary>
public sealed class GreedySchedulingSolver : ISchedulingSolver
{
    public SolverKind Kind => SolverKind.Greedy;
    public int Priority => 10;

    public Task<SchedulingSolution> SolveAsync(
        AnalyzedSchedulingProblem problem,
        CancellationToken cancellationToken)
    {
        var assignments = new List<ScheduledPlacement>();
        var unscheduled = new List<UnscheduledPlacement>();

        // Group candidates by request, order: least flexible first, then earliest deadline, then highest priority
        var grouped = problem.Candidates
            .GroupBy(x => x.RequestId)
            .OrderBy(g => g.Min(c => TotalWindow(c)))
            .ThenBy(g => g.Min(c => c.LatestEnd))
            .ThenByDescending(g => g.Max(c => c.Priority))
            .ToList();

        // Precedence overrides the flexibility heuristic: a successor placed before its
        // predecessor has no end to respect yet. Sort into dependency order first and keep
        // the heuristic as the tie-break within each layer.
        var dependencies = problem.Problem.Dependencies ?? [];
        if (dependencies.Count > 0)
            grouped = TopologicalOrder(grouped, dependencies);

        // The candidate windows already exclude fixed occupancy; the reservations map carries
        // this run's own placements on top of it. Seeding it with the fixed occupancy costs
        // nothing and keeps the earliest-fit honest even if a window were ever loose.
        var reserved = problem.Problem.FixedAssignments
            .GroupBy(x => x.ResourceId)
            .ToDictionary(g => g.Key, g => g.Select(x => (x.Start, x.End)).ToList());
        var placedEnds = new Dictionary<Guid, int>();
        var predecessorsOf = dependencies
            .GroupBy(e => e.SuccessorRequestId)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var requestGroup in grouped)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var placed = false;

            // The earliest this request may start, given whatever its predecessors got.
            // Topological order guarantees they were attempted before this point. A successor
            // may start the very minute its predecessor ends, plus the lag.
            var dependencyFloor = 0;
            var hasFloor = false;
            var predecessorMissing = false;
            if (predecessorsOf.TryGetValue(requestGroup.Key, out var incoming))
            {
                foreach (var edge in incoming)
                {
                    if (!placedEnds.TryGetValue(edge.PredecessorRequestId, out var predEnd))
                    {
                        predecessorMissing = true;
                        break;
                    }
                    var bound = predEnd + edge.LagMinutes;
                    if (!hasFloor || bound > dependencyFloor) dependencyFloor = bound;
                    hasFloor = true;
                }
            }

            if (predecessorMissing)
            {
                unscheduled.Add(new UnscheduledPlacement(
                    requestGroup.Key,
                    [SchedulingReasonCode.PredecessorUnscheduled]));
                continue;
            }

            // Whether any start at all survived the dependency floor. It separates "the
            // resource was busy" from "the predecessor finishes too late to leave room", which
            // are different problems for the reader even though both end in no placement.
            var anyStartAfterFloor = !hasFloor;

            foreach (var candidate in requestGroup.OrderBy(TotalWindow))
            {
                if (hasFloor && candidate.FeasibleStartWindows.Any(w => w.To > dependencyFloor))
                    anyStartAfterFloor = true;

                if (!reserved.TryGetValue(candidate.ResourceId, out var reservations))
                    reserved[candidate.ResourceId] = reservations = [];

                var start = StartWindows.EarliestFit(
                    candidate.FeasibleStartWindows, reservations, candidate.DurationMinutes, dependencyFloor);
                if (start is not { } s) continue;

                var end = s + candidate.DurationMinutes;
                reservations.Add((s, end));

                assignments.Add(new ScheduledPlacement(
                    candidate.RequestId,
                    candidate.ResourceId,
                    s, end,
                    candidate.DurationMinutes,
                    candidate.Priority));

                placed = true;
                placedEnds[candidate.RequestId] = end;
                break;
            }

            if (!placed)
            {
                // Every predecessor was placed (the missing case returned above), so the reason
                // turns on whether the dependency left any start to try: if it did, the resource
                // was simply busy, and blaming the predecessor would send the reader after a
                // problem that does not exist.
                unscheduled.Add(new UnscheduledPlacement(
                    requestGroup.Key,
                    [anyStartAfterFloor
                        ? SchedulingReasonCode.BlockedByFixedAssignments
                        : SchedulingReasonCode.PredecessorUnscheduled]));
            }
        }

        // Add requests that were fully rejected during feasibility analysis
        foreach (var rejected in problem.Rejections.GroupBy(x => x.RequestId))
        {
            if (assignments.Any(a => a.RequestId == rejected.Key) ||
                unscheduled.Any(u => u.RequestId == rejected.Key))
                continue;

            unscheduled.Add(new UnscheduledPlacement(
                rejected.Key,
                rejected.Select(x => x.ReasonCode).Distinct().ToList()));
        }

        return Task.FromResult(new SchedulingSolution(
            SolverUsed: SolverKind.Greedy,
            Status: SolverStatus.Feasible,
            Assignments: assignments,
            Unscheduled: unscheduled,
            Diagnostics: [.. problem.Diagnostics]));
    }

    /// <summary>How much room a candidate has: the minutes its start may fall in.</summary>
    private static int TotalWindow(SchedulingCandidate candidate)
        => candidate.FeasibleStartWindows.Sum(w => w.Length);

    /// <summary>
    /// Kahn's algorithm over the solve set, keeping the incoming heuristic order as the
    /// tie-break within each layer. Requests caught in a cycle are appended at the end rather
    /// than dropped: the service layer rejects cycles on write, so reaching one here means data
    /// changed underneath us. Every member of a cycle has an unplaced predecessor, so they are
    /// reported as unscheduled with a reason rather than disappearing from the run.
    /// </summary>
    private static List<IGrouping<Guid, SchedulingCandidate>> TopologicalOrder(
        List<IGrouping<Guid, SchedulingCandidate>> groups,
        IReadOnlyList<DependencyEdge> dependencies)
    {
        var inSet = groups.Select(g => g.Key).ToHashSet();
        var indegree = groups.ToDictionary(g => g.Key, _ => 0);
        var successors = new Dictionary<Guid, List<Guid>>();

        foreach (var edge in dependencies)
        {
            // Only edges wholly inside the solve set order it; the builder folded the rest away.
            if (!inSet.Contains(edge.PredecessorRequestId) || !inSet.Contains(edge.SuccessorRequestId))
                continue;

            if (!successors.TryGetValue(edge.PredecessorRequestId, out var list))
                successors[edge.PredecessorRequestId] = list = [];
            list.Add(edge.SuccessorRequestId);
            indegree[edge.SuccessorRequestId]++;
        }

        var ordered = new List<IGrouping<Guid, SchedulingCandidate>>(groups.Count);
        var remaining = new List<IGrouping<Guid, SchedulingCandidate>>(groups);

        while (remaining.Count > 0)
        {
            // First ready request in heuristic order.
            var index = remaining.FindIndex(g => indegree[g.Key] == 0);
            if (index < 0) break; // cycle — the rest keep their heuristic order below

            var next = remaining[index];
            remaining.RemoveAt(index);
            ordered.Add(next);

            if (successors.TryGetValue(next.Key, out var outgoing))
                foreach (var succ in outgoing) indegree[succ]--;
        }

        ordered.AddRange(remaining);
        return ordered;
    }
}
