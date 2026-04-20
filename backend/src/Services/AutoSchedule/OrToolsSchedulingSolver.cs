using Api.Models;
using Google.OrTools.Sat;

namespace Api.Services.AutoSchedule;

/// <summary>
/// CP-SAT constraint programming solver using Google OR-Tools.
/// Models each request-space candidate as an optional interval variable,
/// enforces no-overlap per space and at-most-one assignment per request,
/// and maximizes a weighted objective (throughput → priority → early completion).
/// </summary>
public sealed class OrToolsSchedulingSolver : ISchedulingSolver
{
    private readonly ILogger<OrToolsSchedulingSolver> _logger;

    /// <summary>Solver time limit for interactive preview responsiveness.</summary>
    private static readonly TimeSpan SolverTimeLimit = TimeSpan.FromSeconds(5);

    // Objective function weights — throughput >> priority >> early completion.
    // Rationale: 100_000 per scheduled request ensures throughput dominates;
    // 1_000 * priority differentiates between placements of equal count;
    // -1 * startOffset is a tiebreaker favouring earlier completion.
    private const int ThroughputWeight = 100_000;
    private const int PriorityWeight = 1_000;

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
        var horizonStart = problem.Problem.HorizonStart;
        var horizonLength = problem.Problem.HorizonEnd.DayNumber - horizonStart.DayNumber;

        // Group candidates by request and by space
        var candidatesByRequest = problem.Candidates.GroupBy(c => c.RequestId).ToList();
        var candidatesBySpace = problem.Candidates.GroupBy(c => c.SpaceId).ToList();

        // Decision variables: one BoolVar per candidate (request → space assignment)
        var candidateVars = new Dictionary<(Guid RequestId, Guid SpaceId), BoolVar>();
        var candidateStarts = new Dictionary<(Guid RequestId, Guid SpaceId), IntVar>();
        var candidateIntervals = new Dictionary<(Guid RequestId, Guid SpaceId), IntervalVar>();

        foreach (var candidate in problem.Candidates)
        {
            var key = (candidate.RequestId, candidate.SpaceId);
            var presence = model.NewBoolVar($"assign_{candidate.RequestId}_{candidate.SpaceId}");
            candidateVars[key] = presence;

            // Start variable bounded by feasible range
            var minStart = candidate.FeasibleStartDays.Min(d => d.DayNumber) - horizonStart.DayNumber;
            var maxStart = candidate.FeasibleStartDays.Max(d => d.DayNumber) - horizonStart.DayNumber;
            var startVar = model.NewIntVar(minStart, maxStart, $"start_{candidate.RequestId}_{candidate.SpaceId}");
            candidateStarts[key] = startVar;

            // Constrain start to only feasible days
            var table = model.AddAllowedAssignments([startVar]);
            foreach (var day in candidate.FeasibleStartDays)
            {
                table.AddTuple(new[] { (long)(day.DayNumber - horizonStart.DayNumber) });
            }

            // Optional interval: active only when presence is true
            var interval = model.NewOptionalFixedSizeIntervalVar(
                startVar, candidate.DurationDays, presence,
                $"interval_{candidate.RequestId}_{candidate.SpaceId}");
            candidateIntervals[key] = interval;
        }

        // Constraint: each request assigned at most once
        foreach (var group in candidatesByRequest)
        {
            var vars = group.Select(c => candidateVars[(c.RequestId, c.SpaceId)]).ToArray();
            model.Add(LinearExpr.Sum(vars) <= 1);
        }

        // Constraint: no-overlap per space (including fixed occupancy)
        foreach (var spaceGroup in candidatesBySpace)
        {
            var spaceId = spaceGroup.Key;
            var intervals = spaceGroup
                .Select(c => candidateIntervals[(c.RequestId, c.SpaceId)])
                .ToList();

            // Add fixed occupancy as mandatory intervals
            var fixedOnSpace = problem.Problem.FixedAssignments
                .Where(a => a.SpaceId == spaceId)
                .ToList();

            foreach (var fixedOcc in fixedOnSpace)
            {
                var fixedStart = fixedOcc.Start.DayNumber - horizonStart.DayNumber;
                var fixedDuration = fixedOcc.End.DayNumber - fixedOcc.Start.DayNumber + 1;
                var fixedInterval = model.NewFixedSizeIntervalVar(
                    fixedStart, fixedDuration,
                    $"fixed_{fixedOcc.RequestId}_{spaceId}");
                intervals.Add(fixedInterval);
            }

            if (intervals.Count > 1)
            {
                model.AddNoOverlap(intervals);
            }
        }

        // Objective: maximize throughput, then priority, then early completion
        var objectiveTerms = new List<LinearExpr>();
        foreach (var candidate in problem.Candidates)
        {
            var key = (candidate.RequestId, candidate.SpaceId);
            var presence = candidateVars[key];
            var startVar = candidateStarts[key];

            objectiveTerms.Add(ThroughputWeight * presence);
            objectiveTerms.Add(PriorityWeight * candidate.Priority * presence);

            // Early completion: penalize late starts (negative contribution)
            // Only active when presence is true — approximate with weighted start
            objectiveTerms.Add(-1 * startVar);
        }

        if (objectiveTerms.Count > 0)
        {
            model.Maximize(LinearExpr.Sum(objectiveTerms));
        }

        // Solve
        var solver = new CpSolver();
        solver.StringParameters = $"max_time_in_seconds:{SolverTimeLimit.TotalSeconds:F1}";

        _logger.LogInformation("Starting CP-SAT solve: {CandidateCount} candidates, {RequestCount} requests, horizon {Days} days",
            problem.Candidates.Count, candidatesByRequest.Count, horizonLength);

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

        foreach (var candidate in problem.Candidates)
        {
            var key = (candidate.RequestId, candidate.SpaceId);
            if (solver.BooleanValue(candidateVars[key]))
            {
                var startOffset = (int)solver.Value(candidateStarts[key]);
                var startDay = horizonStart.AddDays(startOffset);
                var endDay = startDay.AddDays(candidate.DurationDays - 1);

                assignments.Add(new ScheduledPlacement(
                    candidate.RequestId, candidate.SpaceId,
                    startDay, endDay,
                    candidate.DurationDays, candidate.Priority));

                scheduledRequestIds.Add(candidate.RequestId);
            }
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
