using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;

namespace Api.Services.Caching;

/// <summary>
/// The one in-process cache foundation services share: a read-through wrapper over the injected
/// <see cref="IMemoryCache"/> with single-flight on the compute path.
/// </summary>
/// <remarks>
/// <para>
/// Every entry is written with <c>Size = 1</c>, so a product that puts a <c>SizeLimit</c> on its
/// own <see cref="IMemoryCache"/> bounds this cache too. Foundation sets no limit itself: a limit
/// would make every <c>Set</c> without a size throw, including a product's own.
/// </para>
/// <para>
/// Tenant isolation is the caller's job — put the org id in the key. Entries are bounded by TTL
/// only; the shared instance has no entry cap. Identity entries are small and bounded in
/// practice by the number of distinct users in a five-minute window. Payload caches are NOT:
/// see <see cref="AnalyticsCache"/>, which is a separate, bounded instance for exactly that
/// reason.
/// </para>
/// <para>
/// Single-flight matters where a page fans out many concurrent requests for the same answer: the
/// Insights dashboard asks once per resource type at the same moment, and without it every one of
/// those misses would run the same computation. The in-flight entry is removed on completion, so a
/// faulted task is never cached.
/// </para>
/// </remarks>
// Not sealed: AnalyticsCache derives from it to get a separate, bounded MemoryCache.
public class SingleFlightCache(IMemoryCache cache)
{
    private readonly ConcurrentDictionary<string, object> _inFlight = new(StringComparer.Ordinal);

    /// <summary>
    /// Returns the cached value for <paramref name="key"/>, or runs <paramref name="compute"/>
    /// once for all concurrent callers and caches its result for <paramref name="ttl"/>.
    /// </summary>
    public async Task<T> GetOrComputeAsync<T>(string key, TimeSpan ttl, Func<Task<T>> compute)
        where T : class
    {
        if (cache.TryGetValue(key, out T? hit) && hit is not null)
            return hit;

        var lazy = (Lazy<Task<T>>)_inFlight.GetOrAdd(key, _ => new Lazy<Task<T>>(async () =>
        {
            var value = await compute();
            Set(key, value, ttl);
            return value;
        }));

        try
        {
            return await lazy.Value;
        }
        finally
        {
            // Remove OUR entry, not whatever is under the key now. Every awaiter of a shared
            // Lazy runs this, so an unconditional TryRemove let a late awaiter of a finished
            // computation evict a NEWER in-flight entry that another caller was still awaiting,
            // and the next caller then recomputed. Wrong results were never possible; duplicated
            // work was, which is the one thing this class exists to prevent.
            _inFlight.TryRemove(new KeyValuePair<string, object>(key, lazy));
        }
    }

    /// <summary>Reads an entry without computing one. False when the key is absent or expired.</summary>
    public bool TryGet<T>(string key, out T? value) => cache.TryGetValue(key, out value);

    /// <summary>Writes an entry that expires <paramref name="ttl"/> from now.</summary>
    public void Set<T>(string key, T value, TimeSpan ttl) =>
        cache.Set(key, value, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = ttl,
            Size = 1,
        });

    /// <summary>Drops an entry, for a write that invalidates what it cached.</summary>
    public void Remove(string key) => cache.Remove(key);
}
