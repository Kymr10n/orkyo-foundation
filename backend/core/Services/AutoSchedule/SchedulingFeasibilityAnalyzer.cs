using Api.Models;

namespace Api.Services.AutoSchedule;

/// <summary>
/// Expands request→resource candidates for every type a request has open, rejects impossible
/// ones, and computes the start windows each survivor may begin in: the request's own window
/// on the axis, minus every start that would collide with what the resource is already taken
/// for. A request reaches the solvers with candidates for all of its open types or with none:
/// a placement fills every type at once, so a request that cannot fill one of them cannot be
/// placed at all.
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
        var resourcesByType = problem.Resources
            .GroupBy(r => r.ResourceTypeKey, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        var scopes = problem.CriterionTypeScopes ?? new Dictionary<Guid, IReadOnlySet<string>>();

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

            var earliest = request.EarliestStart ?? 0;
            var latestEnd = request.LatestEnd ?? problem.Axis.Length;
            var lastStart = latestEnd - request.DurationMinutes;

            var forRequest = new List<SchedulingCandidate>();
            var everyTypeHasACandidate = true;

            foreach (var typeKey in request.OpenTypeKeys.Order(StringComparer.Ordinal))
            {
                // Only the requirements scoped to this type apply: a criterion written for
                // mills says nothing about the van the same request also needs. A criterion
                // with no scope recorded applies to every type.
                var required = request.RequiredCriterionIds
                    .Where(c => !scopes.TryGetValue(c, out var scope) || scope.Count == 0 || scope.Contains(typeKey))
                    .ToList();

                // Find resources of this type whose criterion set is a superset of the requirements
                var compatibleResources = resourcesByType.GetValueOrDefault(typeKey, [])
                    .Where(resource => required.All(resource.CriterionIds.Contains))
                    .ToList();

                if (compatibleResources.Count == 0)
                {
                    rejections.Add(new CandidateRejection(
                        request.RequestId, null,
                        SchedulingReasonCode.NoCompatibleResource,
                        $"No {typeKey} satisfies all required criteria."));
                    everyTypeHasACandidate = false;
                    continue;
                }

                var anyFit = false;
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

                    anyFit = true;
                    forRequest.Add(new SchedulingCandidate(
                        request.RequestId,
                        resource.ResourceId,
                        typeKey,
                        earliest,
                        latestEnd,
                        request.DurationMinutes,
                        request.Priority,
                        windows));
                }

                if (!anyFit) everyTypeHasACandidate = false;
            }

            if (everyTypeHasACandidate) candidates.AddRange(forRequest);
        }

        // Add diagnostics summary
        var noCompatibleCount = rejections.Count(r => r.ReasonCode == SchedulingReasonCode.NoCompatibleResource);
        if (noCompatibleCount > 0)
            diagnostics.Add($"{noCompatibleCount} request type(s) removed: no compatible resource exists.");

        var tightWindowCount = rejections.Count(r => r.ReasonCode == SchedulingReasonCode.InsufficientCapacity);
        if (tightWindowCount > 0)
            diagnostics.Add($"{tightWindowCount} request-resource pair(s) removed: no feasible start.");

        return new AnalyzedSchedulingProblem(problem, candidates, rejections, diagnostics);
    }
}
