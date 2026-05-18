using Api.Constants;
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
        var additionalResources = problem.Problem.AdditionalResources ?? [];

        foreach (var requestGroup in grouped)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var placed = false;
            var request = problem.Problem.Requests.FirstOrDefault(r => r.RequestId == requestGroup.Key);
            var additionalReqs = request?.AdditionalRequirements ?? [];

            foreach (var candidate in requestGroup.OrderBy(c => c.FeasibleStartDays.Count))
            {
                foreach (var start in candidate.FeasibleStartDays.OrderBy(x => x.DayNumber))
                {
                    var end = start.AddDays(candidate.DurationDays - 1);
                    if (Conflicts(occupied, candidate.ResourceId, start, end))
                        continue;

                    // Greedy-assign additional resources (one per requirement, first-fit).
                    var additionalIds = ResolveAdditionalResources(
                        additionalReqs, additionalResources, occupied, start, end);
                    if (additionalIds is null) continue; // can't satisfy all requirements

                    Reserve(occupied, candidate.ResourceId, start, end);
                    foreach (var rid in additionalIds)
                        Reserve(occupied, rid, start, end);

                    assignments.Add(new ScheduledPlacement(
                        candidate.RequestId,
                        candidate.ResourceId,
                        start, end,
                        candidate.DurationDays,
                        candidate.Priority,
                        additionalIds));

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
            .GroupBy(x => x.ResourceId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(x => (x.Start, x.End)).ToList());

    /// <summary>
    /// Returns one resource ID per requirement (first-fit), or null if any requirement can't be met.
    /// Already-reserved IDs within this call are excluded from subsequent picks.
    /// </summary>
    private static IReadOnlyList<Guid>? ResolveAdditionalResources(
        IReadOnlyList<IResourceRequirement> requirements,
        IReadOnlyList<ResourceNode> pool,
        Dictionary<Guid, List<(DateOnly Start, DateOnly End)>> occupied,
        DateOnly start, DateOnly end)
    {
        if (requirements.Count == 0) return [];

        var picked = new List<Guid>();
        var pickedSet = new HashSet<Guid>();

        foreach (var req in requirements)
        {
            var found = false;
            foreach (var node in pool)
            {
                if (pickedSet.Contains(node.ResourceId)) continue;
                if (node.ResourceTypeId != req.ResourceTypeId) continue;
                if (!req.RequiredCriterionIds.All(node.CriterionIds.Contains)) continue;
                if (node.AllocationMode == AllocationModes.Exclusive &&
                    Conflicts(occupied, node.ResourceId, start, end)) continue;

                picked.Add(node.ResourceId);
                pickedSet.Add(node.ResourceId);
                found = true;
                break;
            }
            if (!found) return null;
        }

        return picked;
    }

    private static bool Conflicts(
        Dictionary<Guid, List<(DateOnly Start, DateOnly End)>> occupied,
        Guid resourceId, DateOnly start, DateOnly end)
    {
        if (!occupied.TryGetValue(resourceId, out var ranges))
            return false;
        return ranges.Any(x => !(end < x.Start || start > x.End));
    }

    private static void Reserve(
        Dictionary<Guid, List<(DateOnly Start, DateOnly End)>> occupied,
        Guid resourceId, DateOnly start, DateOnly end)
    {
        if (!occupied.TryGetValue(resourceId, out var ranges))
        {
            ranges = [];
            occupied[resourceId] = ranges;
        }
        ranges.Add((start, end));
    }
}
