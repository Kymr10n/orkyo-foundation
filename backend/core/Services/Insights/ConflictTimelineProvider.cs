using Api.Constants;
using Api.Repositories;

namespace Api.Services.Insights;

/// <summary>One conflict, placed at the scheduled start of the request that carries it.</summary>
public sealed record ConflictPoint(DateTime StartTs, string Kind);

/// <summary>
/// The conflict timeline for a window, shared by every Insights report that needs it.
/// </summary>
/// <remarks>
/// Computing it means running live conflict detection over every scheduled request in the window,
/// which is the most expensive thing the dashboard does. It is also the same answer for all of
/// them: the timeline depends on the window and the site, never on a resource type. Insights used
/// to recompute it inside each report — once for the overview, once for the conflicts trend, and
/// once per active resource type for the utilization charts, which is eleven full scans for one
/// page on a workspace with nine types. Behind this seam it is computed once and shared.
/// </remarks>
public interface IConflictTimelineProvider
{
    Task<List<ConflictPoint>> GetAsync(DateTime from, DateTime to, Guid? siteId, CancellationToken ct = default);
}

public sealed class ConflictTimelineProvider(
    IConflictService conflictService,
    IRequestRepository requestRepository) : IConflictTimelineProvider
{
    /// <summary>
    /// Flattens the live conflict registry into (scheduled-start, kind) points so conflicts can be
    /// bucketed by when they occur. Joins each conflict's request back to its start_ts/site via the
    /// scheduled-request set (the conflict registry itself carries no timestamp). Site-filtered here —
    /// site-neutral requests (no site) are kept under every site.
    /// </summary>
    public async Task<List<ConflictPoint>> GetAsync(
        DateTime from, DateTime to, Guid? siteId, CancellationToken ct = default)
    {
        var registry = await conflictService.GetAllAsync(from, to, ct);
        if (registry.Count == 0) return [];

        var scheduled = (await requestRepository.GetScheduledLiteAsync(from, to, ct))
            .ToDictionary(r => r.Id);

        var points = new List<ConflictPoint>();
        foreach (var rc in registry)
        {
            if (!scheduled.TryGetValue(rc.RequestId, out var request)) continue;
            // Exclude only when the request is bound to a *different* site; site-neutral stays in.
            if (siteId.HasValue && request.SiteId.HasValue && request.SiteId != siteId) continue;
            foreach (var c in rc.Conflicts)
                points.Add(new ConflictPoint(request.StartTs, c.Kind));
        }
        return points;
    }
}
