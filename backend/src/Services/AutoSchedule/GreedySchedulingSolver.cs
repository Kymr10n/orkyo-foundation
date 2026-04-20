using Api.Models;

namespace Api.Services.AutoSchedule;

/// <summary>
/// Greedy earliest-fit solver. Assigns requests one at a time in priority order,
/// picking the first feasible start day on the first compatible space.
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
            .OrderBy(g => g.Min(c => c.FeasibleStartDays.Count))
            .ThenBy(g => g.Min(c => c.LatestEnd.DayNumber))
            .ThenByDescending(g => g.Max(c => c.Priority));

        var occupied = BuildOccupiedMap(problem.Problem.FixedAssignments);

        foreach (var requestGroup in grouped)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var placed = false;

            foreach (var candidate in requestGroup.OrderBy(c => c.FeasibleStartDays.Count))
            {
                foreach (var start in candidate.FeasibleStartDays.OrderBy(x => x.DayNumber))
                {
                    var end = start.AddDays(candidate.DurationDays - 1);
                    if (Conflicts(occupied, candidate.SpaceId, start, end))
                        continue;

                    Reserve(occupied, candidate.SpaceId, start, end);

                    assignments.Add(new ScheduledPlacement(
                        candidate.RequestId,
                        candidate.SpaceId,
                        start, end,
                        candidate.DurationDays,
                        candidate.Priority));

                    placed = true;
                    break;
                }

                if (placed) break;
            }

            if (!placed)
            {
                unscheduled.Add(new UnscheduledPlacement(
                    requestGroup.Key,
                    [SchedulingReasonCode.BlockedByFixedAssignments]));
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

    private static Dictionary<Guid, List<(DateOnly Start, DateOnly End)>> BuildOccupiedMap(
        IReadOnlyList<FixedOccupancy> fixedAssignments)
        => fixedAssignments
            .GroupBy(x => x.SpaceId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(x => (x.Start, x.End)).ToList());

    private static bool Conflicts(
        Dictionary<Guid, List<(DateOnly Start, DateOnly End)>> occupied,
        Guid spaceId, DateOnly start, DateOnly end)
    {
        if (!occupied.TryGetValue(spaceId, out var ranges))
            return false;
        return ranges.Any(x => !(end < x.Start || start > x.End));
    }

    private static void Reserve(
        Dictionary<Guid, List<(DateOnly Start, DateOnly End)>> occupied,
        Guid spaceId, DateOnly start, DateOnly end)
    {
        if (!occupied.TryGetValue(spaceId, out var ranges))
        {
            ranges = [];
            occupied[spaceId] = ranges;
        }
        ranges.Add((start, end));
    }
}
