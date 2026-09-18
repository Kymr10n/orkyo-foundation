using Api.Models;

namespace Api.Services.AutoSchedule;

/// <summary>
/// Expands request→resource candidates, rejects impossible ones, and computes the start
/// windows each survivor may begin in: the request's own window on the axis, minus every
/// start that would collide with what the resource is already taken for. Output feeds
/// directly into the solvers.
/// </summary>
public sealed class SchedulingFeasibilityAnalyzer
{
    public AnalyzedSchedulingProblem Analyze(SchedulingProblem problem)
    {
        var candidates = new List<SchedulingCandidate>();
        var rejections = new List<CandidateRejection>();
        var diagnostics = new List<string>();

        var occupancyByResource = problem.FixedAssignments
            .GroupBy(a => a.ResourceId)
            .ToDictionary(g => g.Key, g => g.Select(a => (a.Start, a.End)).ToList());

        foreach (var request in problem.Requests)
        {
            if (request.DurationMinutes <= 0)
            {
                rejections.Add(new CandidateRejection(
                    request.RequestId, null,
                    SchedulingReasonCode.InvalidDuration,
                    "Duration must be > 0 minutes."));
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

            var earliest = request.EarliestStart ?? 0;
            var latestEnd = request.LatestEnd ?? problem.Axis.Length;
            var lastStart = latestEnd - request.DurationMinutes;

            foreach (var resource in compatibleResources)
            {
                var windows = lastStart < earliest
                    ? []
                    : StartWindows.Subtract(
                        earliest, lastStart + 1,
                        occupancyByResource.GetValueOrDefault(resource.ResourceId) ?? [],
                        request.DurationMinutes);

                if (windows.Count == 0)
                {
                    rejections.Add(new CandidateRejection(
                        request.RequestId, resource.ResourceId,
                        SchedulingReasonCode.InsufficientCapacity,
                        "No feasible start within the horizon for this resource."));
                    continue;
                }

                candidates.Add(new SchedulingCandidate(
                    request.RequestId,
                    resource.ResourceId,
                    earliest,
                    latestEnd,
                    request.DurationMinutes,
                    request.Priority,
                    windows));
            }
        }

        // Add diagnostics summary
        var noCompatibleCount = rejections.Count(r => r.ReasonCode == SchedulingReasonCode.NoCompatibleResource);
        if (noCompatibleCount > 0)
            diagnostics.Add($"{noCompatibleCount} request(s) removed: no compatible resource exists.");

        var tightWindowCount = rejections.Count(r => r.ReasonCode == SchedulingReasonCode.InsufficientCapacity);
        if (tightWindowCount > 0)
            diagnostics.Add($"{tightWindowCount} request-resource pair(s) removed: no feasible start.");

        return new AnalyzedSchedulingProblem(problem, candidates, rejections, diagnostics);
    }
}
