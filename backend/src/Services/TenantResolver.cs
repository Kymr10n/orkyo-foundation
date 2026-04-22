using System.Collections.Concurrent;
using Npgsql;

namespace Api.Services;

public class TenantResolver : ITenantResolver
{
    private readonly string _controlPlaneConnectionString;

    // In-memory cache: slug → (TenantContext?, expiry). Singleton-friendly.
    private static readonly ConcurrentDictionary<string, TenantResolverCacheEntry> _cache = new(TenantCacheKeyPolicy.Comparer);

    /// <summary>Clear all cached tenants (used by integration tests).</summary>
    internal static void ClearCache() => _cache.Clear();

    public TenantResolver(IConfiguration configuration)
    {
        _controlPlaneConnectionString = TenantResolverPolicy.ResolveControlPlaneConnectionString(configuration);
    }

    public async Task<TenantContext?> ResolveTenantAsync(string? subdomain, string? tenantHeader)
    {
        return await TenantResolverCoordinatorFlow.ResolveAsync(
            subdomain,
            tenantHeader,
            _cache,
            ResolveTenantFromDbAsync);
    }

    public void InvalidateCache(string slug)
    {
        TenantResolverCacheFlow.Invalidate(_cache, slug);
    }

    private async Task<TenantContext?> ResolveTenantFromDbAsync(string tenantSlug)
    {
        await using var conn = new NpgsqlConnection(_controlPlaneConnectionString);
        await conn.OpenAsync();
        return await TenantResolverDbFlow.ResolveFromOpenConnectionAsync(conn, tenantSlug, _controlPlaneConnectionString);
    }
}
