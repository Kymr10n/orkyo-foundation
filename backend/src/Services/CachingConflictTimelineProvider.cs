using Api.Services;

namespace Api.Services.Insights;

/// <summary>
/// Short-TTL cache over <see cref="IConflictTimelineProvider"/>.
/// </summary>
/// <remarks>
/// The timeline is the same answer for every report on a page — it varies by window and site, never
/// by resource type — so the nine utilization charts, the overview and the conflicts trend all ask
/// for one thing. Single-flight collapses that burst into one live conflict scan; the TTL covers the
/// re-fetches that follow.
/// </remarks>
public sealed class CachingConflictTimelineProvider(
    IConflictTimelineProvider inner, OrgContext orgContext) : IConflictTimelineProvider
{
    public Task<List<ConflictPoint>> GetAsync(
        DateTime from, DateTime to, Guid? siteId, CancellationToken ct = default)
    {
        var key = string.Join('|',
            orgContext.OrgId, "conflict-timeline", from.Ticks, to.Ticks, siteId?.ToString() ?? "-");

        return ShortTtlCache.GetOrComputeAsync(key, () => inner.GetAsync(from, to, siteId, ct));
    }
}
