using Api.Models;

namespace Api.Services.AutoSchedule;

/// <summary>
/// Expands request→space candidates, rejects impossible ones, and
/// enumerates feasible start days. Output feeds directly into the solver.
/// </summary>
public sealed class SchedulingFeasibilityAnalyzer
{
    public AnalyzedSchedulingProblem Analyze(SchedulingProblem problem)
    {
        var candidates = new List<SchedulingCandidate>();
        var rejections = new List<CandidateRejection>();
        var diagnostics = new List<string>();

        foreach (var request in problem.Requests)
        {
            if (request.DurationDays <= 0)
            {
                rejections.Add(new CandidateRejection(
                    request.RequestId, null,
                    SchedulingReasonCode.InvalidDuration,
                    "Duration must be > 0 days."));
                continue;
            }

            // Find spaces whose criterion set is a superset of the request's requirements
            var compatibleSpaces = problem.Spaces
                .Where(space => request.RequiredCriterionIds.All(space.CriterionIds.Contains))
                .ToList();

            if (compatibleSpaces.Count == 0)
            {
                rejections.Add(new CandidateRejection(
                    request.RequestId, null,
                    SchedulingReasonCode.NoCompatibleSpace,
                    "No space satisfies all required criteria."));
                continue;
            }

            foreach (var space in compatibleSpaces)
            {
                var feasibleStartDays = EnumerateFeasibleStarts(problem, request, space).ToList();

                if (feasibleStartDays.Count == 0)
                {
                    rejections.Add(new CandidateRejection(
                        request.RequestId, space.SpaceId,
                        SchedulingReasonCode.InsufficientCapacity,
                        "No feasible start day within the horizon for this space."));
                    continue;
                }

                candidates.Add(new SchedulingCandidate(
                    request.RequestId,
                    space.SpaceId,
                    request.EarliestStart ?? problem.HorizonStart,
                    request.LatestEnd ?? problem.HorizonEnd,
                    request.DurationDays,
                    request.Priority,
                    feasibleStartDays));
            }
        }

        // Add diagnostics summary
        var noCompatibleCount = rejections.Count(r => r.ReasonCode == SchedulingReasonCode.NoCompatibleSpace);
        if (noCompatibleCount > 0)
            diagnostics.Add($"{noCompatibleCount} request(s) removed: no compatible space exists.");

        var tightWindowCount = rejections.Count(r => r.ReasonCode == SchedulingReasonCode.InsufficientCapacity);
        if (tightWindowCount > 0)
            diagnostics.Add($"{tightWindowCount} request-space pair(s) removed: no feasible start day.");

        return new AnalyzedSchedulingProblem(problem, candidates, rejections, diagnostics);
    }

    private static IEnumerable<DateOnly> EnumerateFeasibleStarts(
        SchedulingProblem problem,
        RequestNode request,
        SpaceNode space)
    {
        var earliest = request.EarliestStart ?? problem.HorizonStart;
        var latestFinish = request.LatestEnd ?? problem.HorizonEnd;
        var latestStart = latestFinish.AddDays(-(request.DurationDays - 1));

        if (latestStart < earliest) yield break;

        // Pre-compute fixed occupancy intervals for this space
        var spaceOccupancy = problem.FixedAssignments
            .Where(a => a.SpaceId == space.SpaceId)
            .Select(a => (a.Start, a.End))
            .ToList();

        // Pre-compute off-time date ranges if scheduling settings apply
        var offDates = new HashSet<DateOnly>();
        if (request.RespectSchedulingSettings && problem.OffTimes != null)
        {
            foreach (var ot in problem.OffTimes.Where(o => o.Enabled))
            {
                // Only include off-times that apply to this space
                if (!ot.AppliesToAllSpaces && ot.SpaceIds != null && !ot.SpaceIds.Contains(space.SpaceId))
                    continue;

                var otStart = DateOnly.FromDateTime(ot.StartTs);
                var otEnd = DateOnly.FromDateTime(ot.EndTs);
                for (var d = otStart; d <= otEnd; d = d.AddDays(1))
                    offDates.Add(d);
            }
        }

        for (var day = earliest; day <= latestStart; day = day.AddDays(1))
        {
            // Skip weekends if scheduling settings apply and weekends are excluded
            if (request.RespectSchedulingSettings && problem.Settings is { WeekendsEnabled: false })
            {
                var dow = day.DayOfWeek;
                if (dow is DayOfWeek.Saturday or DayOfWeek.Sunday)
                    continue;
            }

            // Skip off-time days
            if (offDates.Contains(day))
                continue;

            // Check that the entire placement interval doesn't conflict with fixed occupancy
            var end = day.AddDays(request.DurationDays - 1);
            var conflicts = spaceOccupancy.Any(occ => !(end < occ.Start || day > occ.End));
            if (conflicts)
                continue;

            yield return day;
        }
    }
}
