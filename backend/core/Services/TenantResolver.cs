using System.Collections.Concurrent;
using System.Data.Common;
using Api.Models;
using Npgsql;
using Orkyo.Shared;

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

    public async Task<TenantContext?> ResolveTenantAsync(string? subdomain, string? tenantHeader, CancellationToken ct = default)
    {
        var tenantSlug = TenantSlugSelectionPolicy.SelectSlug(subdomain, tenantHeader);
        if (tenantSlug == null) return null;

        var nowUtc = DateTime.UtcNow;
        var cacheKey = TenantCacheKeyPolicy.Canonicalize(tenantSlug);
        if (_cache.TryGetValue(cacheKey, out var cached) && cached.IsFresh(nowUtc))
            return cached.Context;

        var context = await ResolveTenantFromDbAsync(tenantSlug, ct);
        _cache[cacheKey] = new TenantResolverCacheEntry(context, TenantResolverPolicy.GetCacheExpiryUtc(nowUtc));
        return context;
    }

    public void InvalidateCache(string slug)
    {
        _cache.TryRemove(TenantCacheKeyPolicy.Canonicalize(slug), out _);
    }

    private async Task<TenantContext?> ResolveTenantFromDbAsync(string tenantSlug, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_controlPlaneConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            $"SELECT id, slug, db_identifier, status FROM tenants WHERE slug = @slug AND status != '{TenantStatusConstants.Deleting}'",
            conn);
        cmd.Parameters.AddWithValue("slug", tenantSlug);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await ReadTenantOrNullAsync(reader, _controlPlaneConnectionString);
    }

    private static async Task<TenantContext?> ReadTenantOrNullAsync(DbDataReader reader, string controlPlaneConnectionString)
    {
        if (!await reader.ReadAsync()) return null;
        return TenantContextMapper.MapFromResolverRow(reader, controlPlaneConnectionString);
    }
}
