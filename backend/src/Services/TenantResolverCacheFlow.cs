using System.Collections.Concurrent;

namespace Api.Services;

public static class TenantResolverCacheFlow
{
    public static bool TryGetFresh(
        ConcurrentDictionary<string, TenantResolverCacheEntry> cache,
        string tenantSlug,
        DateTime nowUtc,
        out TenantContext? context)
    {
        var cacheKey = TenantCacheKeyPolicy.Canonicalize(tenantSlug);
        if (cache.TryGetValue(cacheKey, out var cached) && cached.IsFresh(nowUtc))
        {
            context = cached.Context;
            return true;
        }

        context = null;
        return false;
    }

    public static void Store(
        ConcurrentDictionary<string, TenantResolverCacheEntry> cache,
        string tenantSlug,
        TenantContext? context,
        DateTime nowUtc)
    {
        var cacheKey = TenantCacheKeyPolicy.Canonicalize(tenantSlug);
        cache[cacheKey] = new TenantResolverCacheEntry(context, TenantResolverPolicy.GetCacheExpiryUtc(nowUtc));
    }

    public static void Invalidate(ConcurrentDictionary<string, TenantResolverCacheEntry> cache, string slug)
    {
        cache.TryRemove(TenantCacheKeyPolicy.Canonicalize(slug), out _);
    }
}