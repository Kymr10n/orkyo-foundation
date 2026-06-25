using Api.Models.Insights;
using Api.Services;
using Microsoft.Extensions.Caching.Memory;

namespace Api.Services.Insights;

/// <summary>
/// Short-TTL read-through cache over <see cref="IInsightsService"/>. The dashboard re-fetches the same
/// bucketed series repeatedly and each call is pure read-aggregation, so a brief per-(org, query) cache
/// cuts repeated recomputation at the cost of at most <see cref="Ttl"/> staleness — the same posture as
/// the <c>private, max-age=60</c> response header already applied to dashboard GETs. No explicit
/// invalidation. Keyed by <see cref="OrgContext.OrgId"/> so tenants never share entries.
///
/// The cache is process-wide; in a multi-instance deployment each instance keeps its own copy, so a
/// given query can be up to <see cref="Ttl"/> stale per instance. Acceptable for a dashboard; if
/// cross-instance consistency is ever needed, swap the backing store for the existing Valkey layer
/// behind this same decorator.
/// </summary>
public sealed class CachingInsightsService(IInsightsService inner, OrgContext orgContext) : IInsightsService
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(60);

    // Process-wide, size-bounded. Shared across scopes; tenant isolation comes from OrgId in the key.
    private static readonly MemoryCache Cache = new(new MemoryCacheOptions { SizeLimit = 10_000 });

    public Task<InsightsOverview> GetOverviewAsync(InsightsFilter filter, CancellationToken ct = default)
        => GetOrComputeAsync("overview", filter, () => inner.GetOverviewAsync(filter, ct));

    public Task<InsightsUtilization> GetUtilizationTrendAsync(InsightsFilter filter, CancellationToken ct = default)
        => GetOrComputeAsync("utilization", filter, () => inner.GetUtilizationTrendAsync(filter, ct));

    public Task<InsightsConflicts> GetConflictTrendAsync(InsightsFilter filter, CancellationToken ct = default)
        => GetOrComputeAsync("conflicts", filter, () => inner.GetConflictTrendAsync(filter, ct));

    public Task<InsightsRequests> GetRequestTrendAsync(InsightsFilter filter, CancellationToken ct = default)
        => GetOrComputeAsync("requests", filter, () => inner.GetRequestTrendAsync(filter, ct));

    private async Task<T> GetOrComputeAsync<T>(string op, InsightsFilter f, Func<Task<T>> compute)
        where T : class
    {
        var key = string.Join('|',
            orgContext.OrgId, op, f.From.Ticks, f.To.Ticks,
            f.SiteId?.ToString() ?? "-", f.Bucket ?? "-", f.ResourceType ?? "-");

        if (Cache.TryGetValue(key, out T? hit) && hit is not null)
            return hit;

        var value = await compute();
        Cache.Set(key, value, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = Ttl,
            Size = 1,
        });
        return value;
    }
}
