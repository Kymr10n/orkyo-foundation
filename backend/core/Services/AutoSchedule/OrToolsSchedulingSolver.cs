using Api.Models;
using Google.OrTools.Sat;
using Google.OrTools.Util;

namespace Api.Services.AutoSchedule;

/// <summary>
/// CP-SAT constraint programming solver using Google OR-Tools.
///
/// One start variable per request, on the working-minute axis, and one optional interval per
/// request→resource candidate sharing that start. Enforces no-overlap per resource (fixed
/// occupancy included), exactly one resource per open type when a request is placed,
/// precedence between requests, and maximizes a weighted objective (throughput → priority →
/// early start).
/// </summary>
public sealed class OrToolsSchedulingSolver : ISchedulingSolver
{
    private readonly ILogger<OrToolsSchedulingSolver> _logger;

    /// <summary>Solver time limit for interactive preview responsiveness.</summary>
    private static readonly TimeSpan SolverTimeLimit = TimeSpan.FromSeconds(5);

    public SolverKind Kind => SolverKind.OrToolsCpSat;
    public int Priority => 100;

    public OrToolsSchedulingSolver(ILogger<OrToolsSchedulingSolver> logger)
    {
        _logger = logger;
    }

    public Task<SchedulingSolution> SolveAsync(
        AnalyzedSchedulingProblem problem,
        CancellationToken cancellationToken)
    {
        var model = new CpModel();
        var horizonMinutes = problem.Problem.Axis.Length;

        var candidatesByRequest = problem.Candidates
            .GroupBy(c => c.RequestId)
            .ToDictionary(g => g.Key, g => g.ToList());
        var candidatesByResource = problem.Candidates.GroupBy(c => c.ResourceId).ToList();

        // Decision variables. Per request: where it starts and whether it is placed at all.
        // Per candidate: whether this is the resource it is placed on.
        var starts = new Dictionary<Guid, IntVar>();
        var scheduled = new Dictionary<Guid, BoolVar>();
        var durations = new Dictionary<Guid, int>();
        var priorities = new Dictionary<Guid, int>();
        var presence = new Dictionary<(Guid RequestId, Guid ResourceId), BoolVar>();
        var intervals = new Dictionary<(Guid RequestId, Guid ResourceId), IntervalVar>();

        foreach (var (requestId, candidates) in candidatesByRequest)
        {
            // The start may fall anywhere one of its candidates allows; which candidate is
            // chosen narrows it further below. A domain of allowed ranges rather than an
            // enumerated table: at minute resolution a table would list every minute.
            var union = Domain.FromIntervals(candidates
                .SelectMany(c => c.FeasibleStartWindows)
                .Select(w => new long[] { w.From, w.To - 1 })
                .ToArray());
            var start = model.NewIntVarFromDomain(union, $"start_{requestId}");
            var isScheduled = model.NewBoolVar($"scheduled_{requestId}");

            starts[requestId] = start;
            scheduled[requestId] = isScheduled;
            durations[requestId] = candidates[0].DurationMinutes;
            priorities[requestId] = candidates[0].Priority;

            foreach (var candidate in candidates)
            {
                var key = (candidate.RequestId, candidate.ResourceId);
                var present = model.NewBoolVar($"assign_{candidate.RequestId}_{candidate.ResourceId}");
                presence[key] = present;

                // On this resource, only this candidate's windows are open.
                var own = Domain.FromIntervals(candidate.FeasibleStartWindows
                    .Select(w => new long[] { w.From, w.To - 1 })
                    .ToArray());
                model.AddLinearExpressionInDomain(start, own).OnlyEnforceIf(present);

                intervals[key] = model.NewOptionalFixedSizeIntervalVar(
                    start, candidate.DurationMinutes, present,
                    $"interval_{candidate.RequestId}_{candidate.ResourceId}");
            }

            // Placed on exactly one resource of every type it has open, or on none: a request
            // needing a mill and a fixture is placed with both at the same start, or stays in
            // the backlog.
            foreach (var typeGroup in candidates.GroupBy(c => c.ResourceTypeKey, StringComparer.Ordinal))
            {
                var typePresences = typeGroup.Select(c => presence[(c.RequestId, c.ResourceId)]).ToList();
                model.Add(LinearExpr.Sum(typePresences) == isScheduled);
            }
        }

        // Constraint: precedence. A successor may not start until its predecessor has finished
        // plus the lag — the same minute is fine. Conditional on both being placed, because
        // either may go unscheduled; an unconditional bound would force both in or make the
        // model infeasible. And a successor cannot be placed while its predecessor is not:
        // otherwise the conditional bound is vacuously satisfied by leaving the predecessor out.
        //
        // Successors the precedence block forces out. Without this the reason falls through to
        // the generic capacity default below, and the same input reports "insufficient capacity"
        // here while the greedy fallback reports "predecessor unscheduled".
        var precedenceBlocked = new HashSet<Guid>();

        foreach (var edge in problem.Problem.Dependencies ?? [])
        {
            // No successor candidates means nothing to constrain.
            if (!scheduled.TryGetValue(edge.SuccessorRequestId, out var succScheduled)) continue;

            if (!scheduled.TryGetValue(edge.PredecessorRequestId, out var predScheduled))
            {
                // The predecessor has no feasible resource at all, so it cannot be placed in
                // this run. Skipping the edge here would leave the successor free to schedule
                // ahead of work that never happens — forbid it instead, matching what the
                // greedy solver does with the same situation.
                model.Add(succScheduled == 0);
                precedenceBlocked.Add(edge.SuccessorRequestId);
                continue;
            }

            model.Add(starts[edge.SuccessorRequestId]
                      >= starts[edge.PredecessorRequestId] + durations[edge.PredecessorRequestId] + edge.LagMinutes)
                 .OnlyEnforceIf([predScheduled, succScheduled]);
            model.Add(succScheduled <= predScheduled);
        }

        // Constraint: no-overlap per resource (including fixed occupancy). A fixed occupancy
        // of size zero is a point the axis compressed a booking to; NoOverlap lets an interval
        // touch it but not span it, which is exactly what that booking still means.
        var fixedByResource = problem.Problem.FixedAssignments
            .GroupBy(a => a.ResourceId)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var resourceGroup in candidatesByResource)
        {
            var resourceId = resourceGroup.Key;
            var onResource = resourceGroup
                .Select(c => intervals[(c.RequestId, c.ResourceId)])
                .ToList();

            if (fixedByResource.TryGetValue(resourceId, out var fixedOnResource))
            {
                var n = 0;
                foreach (var fixedOcc in fixedOnResource)
                {
                    onResource.Add(model.NewFixedSizeIntervalVar(
                        fixedOcc.Start, fixedOcc.End - fixedOcc.Start,
                        $"fixed_{resourceId}_{n++}"));
                }
            }

            if (onResource.Count > 1)
                model.AddNoOverlap(onResource);
        }

        // Objective: maximize throughput, then priority, then early start. The weights are
        // derived from the problem, not constants: at minute resolution a start can be in the
        // hundreds of thousands, and a fixed throughput weight below the sum of all possible
        // start penalties would make the solver prefer leaving work unscheduled to placing it
        // late. One priority unit outweighs every start penalty combined, and one placement
        // outweighs every priority and start term combined.
        var requestCount = Math.Max(1, starts.Count);
        var maxPriority = Math.Max(0, priorities.Values.DefaultIfEmpty(0).Max());
        var priorityWeight = (long)requestCount * horizonMinutes + 1;
        var throughputWeight = requestCount * (maxPriority * priorityWeight + horizonMinutes) + 1;

        var objectiveTerms = new List<LinearExpr>();
        foreach (var (requestId, isScheduled) in scheduled)
        {
            objectiveTerms.Add(LinearExpr.Term(isScheduled, throughputWeight));
            objectiveTerms.Add(LinearExpr.Term(isScheduled, priorityWeight * priorities[requestId]));
            objectiveTerms.Add(LinearExpr.Term(starts[requestId], -1));
        }

        if (objectiveTerms.Count > 0)
        {
            model.Maximize(LinearExpr.Sum(objectiveTerms));
        }

        // Solve
        var solver = new CpSolver();
        solver.StringParameters = $"max_time_in_seconds:{SolverTimeLimit.TotalSeconds:F1}";

        _logger.LogInformation(
            "Starting CP-SAT solve: {CandidateCount} candidates, {RequestCount} requests, horizon {Minutes} working minutes, weights throughput={Throughput} priority={Priority}",
            problem.Candidates.Count, starts.Count, horizonMinutes, throughputWeight, priorityWeight);

        cancellationToken.ThrowIfCancellationRequested();

        var status = solver.Solve(model);

        cancellationToken.ThrowIfCancellationRequested();

        _logger.LogInformation("CP-SAT solve completed: status={Status}, objective={Objective}, wallTime={WallTime:F2}s",
            status, solver.ObjectiveValue, solver.WallTime());

        // Map results
        var solverStatus = status switch
        {
            CpSolverStatus.Optimal => SolverStatus.Optimal,
            CpSolverStatus.Feasible => SolverStatus.Feasible,
            CpSolverStatus.Infeasible => SolverStatus.Infeasible,
            _ => SolverStatus.Unknown
        };

        if (solverStatus is SolverStatus.Infeasible or SolverStatus.Unknown)
        {
            // Return empty solution — caller will fall back to greedy
            return Task.FromResult(new SchedulingSolution(
                SolverUsed: SolverKind.OrToolsCpSat,
                Status: solverStatus,
                Assignments: [],
                Unscheduled: problem.Candidates
                    .Select(c => c.RequestId).Distinct()
                    .Select(id => new UnscheduledPlacement(id, [SchedulingReasonCode.InternalSolverLimit]))
                    .ToList(),
                Diagnostics: [.. problem.Diagnostics, $"CP-SAT status: {status}"]));
        }

        var assignments = new List<ScheduledPlacement>();
        var scheduledRequestIds = new HashSet<Guid>();

        foreach (var (requestId, candidates) in candidatesByRequest)
        {
            if (!solver.BooleanValue(scheduled[requestId])) continue;

            var chosen = candidates
                .Where(c => solver.BooleanValue(presence[(c.RequestId, c.ResourceId)]))
                .Select(c => new PlacedResource(c.ResourceTypeKey, c.ResourceId))
                .ToList();
            var start = (int)solver.Value(starts[requestId]);
            assignments.Add(new ScheduledPlacement(
                requestId, chosen,
                start, start + durations[requestId],
                durations[requestId], priorities[requestId]));

            scheduledRequestIds.Add(requestId);
        }

        // Unscheduled: requests not assigned by solver + rejected during feasibility
        var unscheduled = new List<UnscheduledPlacement>();

        var allRequestIds = problem.Candidates
            .Select(c => c.RequestId).Distinct()
            .Union(problem.Rejections.Select(r => r.RequestId))
            .Distinct();

        foreach (var requestId in allRequestIds)
        {
            if (scheduledRequestIds.Contains(requestId)) continue;

            var reasons = problem.Rejections
                .Where(r => r.RequestId == requestId)
                .Select(r => r.ReasonCode)
                .Distinct()
                .ToList();

            if (reasons.Count == 0 && precedenceBlocked.Contains(requestId))
                reasons.Add(SchedulingReasonCode.PredecessorUnscheduled);

            if (reasons.Count == 0)
                reasons.Add(SchedulingReasonCode.InsufficientCapacity);

            unscheduled.Add(new UnscheduledPlacement(requestId, reasons));
        }

        return Task.FromResult(new SchedulingSolution(
            SolverUsed: SolverKind.OrToolsCpSat,
            Status: solverStatus,
            Assignments: assignments,
            Unscheduled: unscheduled,
            Diagnostics: [.. problem.Diagnostics, $"CP-SAT: {status}, objective={solver.ObjectiveValue:F0}, time={solver.WallTime():F2}s"]));
    }
}
