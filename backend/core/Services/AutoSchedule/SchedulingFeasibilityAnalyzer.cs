using Api.Models;

namespace Api.Services.AutoSchedule;

/// <summary>
/// Expands request→resource candidates, rejects impossible ones, and
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

            // Find resources whose criterion set is a superset of the request's requirements
            var compatibleResources = problem.Resources
                .Where(resource => request.RequiredCriterionIds.All(resource.CriterionIds.Contains))
                .ToList();

            if (compatibleResources.Count == 0)
            {
                rejections.Add(new CandidateRejection(
                    request.RequestId, null,
                    SchedulingReasonCode.NoCompatibleResource,
                    "No resource satisfies all required criteria."));
                continue;
            }

            foreach (var resource in compatibleResources)
            {
                var feasibleStartDays = EnumerateFeasibleStarts(problem, request, resource).ToList();

                if (feasibleStartDays.Count == 0)
                {
                    rejections.Add(new CandidateRejection(
                        request.RequestId, resource.ResourceId,
                        SchedulingReasonCode.InsufficientCapacity,
                        "No feasible start day within the horizon for this resource."));
                    continue;
                }

                candidates.Add(new SchedulingCandidate(
                    request.RequestId,
                    resource.ResourceId,
                    request.EarliestStart ?? problem.HorizonStart,
                    request.LatestEnd ?? problem.HorizonEnd,
                    request.DurationDays,
                    request.Priority,
                    feasibleStartDays));
            }
        }

        // Add diagnostics summary
        var noCompatibleCount = rejections.Count(r => r.ReasonCode == SchedulingReasonCode.NoCompatibleResource);
        if (noCompatibleCount > 0)
            diagnostics.Add($"{noCompatibleCount} request(s) removed: no compatible resource exists.");

        var tightWindowCount = rejections.Count(r => r.ReasonCode == SchedulingReasonCode.InsufficientCapacity);
        if (tightWindowCount > 0)
            diagnostics.Add($"{tightWindowCount} request-resource pair(s) removed: no feasible start day.");

        return new AnalyzedSchedulingProblem(problem, candidates, rejections, diagnostics);
    }

    private static IEnumerable<DateOnly> EnumerateFeasibleStarts(
        SchedulingProblem problem,
        RequestNode request,
        ResourceNode resource)
    {
        var earliest = request.EarliestStart ?? problem.HorizonStart;
        var latestFinish = request.LatestEnd ?? problem.HorizonEnd;
        var latestStart = latestFinish.AddDays(-(request.DurationDays - 1));

        if (latestStart < earliest) yield break;

        // Pre-compute fixed occupancy intervals for this resource
        var resourceOccupancy = problem.FixedAssignments
            .Where(a => a.ResourceId == resource.ResourceId)
            .Select(a => (a.Start, a.End))
            .ToList();

        // Pre-compute blocked date ranges for this resource if scheduling settings apply
        var offDates = new HashSet<DateOnly>();
        if (request.RespectSchedulingSettings && problem.BlockedPeriodsByResource != null)
        {
            var periods = problem.BlockedPeriodsByResource.GetValueOrDefault(resource.ResourceId, []);
            foreach (var p in periods)
            {
                var pStart = DateOnly.FromDateTime(p.StartTs);
                var pEnd = DateOnly.FromDateTime(p.EndTs);
                for (var d = pStart; d <= pEnd; d = d.AddDays(1))
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
            var conflicts = resourceOccupancy.Any(occ => !(end < occ.Start || day > occ.End));
            if (conflicts)
                continue;

            yield return day;
        }
    }
}
