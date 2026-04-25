using System.Collections.Concurrent;

namespace Api.Services;

public static class TenantResolverCoordinatorFlow
{
    public static async Task<TenantContext?> ResolveAsync(
        string? subdomain,
        string? tenantHeader,
        ConcurrentDictionary<string, TenantResolverCacheEntry> cache,
        Func<string, Task<TenantContext?>> resolveTenantFromDbAsync)
    {
        var tenantSlug = TenantSlugSelectionPolicy.SelectSlug(subdomain, tenantHeader);
        if (tenantSlug == null)
            return null;

        var nowUtc = DateTime.UtcNow;
        if (TenantResolverCacheFlow.TryGetFresh(cache, tenantSlug, nowUtc, out var cachedContext))
        {
            return cachedContext;
        }

        var context = await resolveTenantFromDbAsync(tenantSlug);
        TenantResolverCacheFlow.Store(cache, tenantSlug, context, nowUtc);
        return context;
    }
}