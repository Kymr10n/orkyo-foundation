using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;

namespace Api.Services.Insights;

/// <summary>
/// Read-through cache with a brief TTL and single-flight, for the dashboard's pure read-aggregations.
/// </summary>
/// <remarks>
/// Process-wide and size-bounded; tenant isolation is the caller's job, by putting the org id in the
/// key. No explicit invalidation — an entry is at most <see cref="Ttl"/> stale, the same posture as
/// the <c>private, max-age=60</c> header already on dashboard GETs.
/// <para>
/// Single-flight matters more here than the cache itself. The Insights page fans out one request per
/// resource type at once, and without it every one of those concurrent misses would run the same
/// computation. Entries are removed on completion, so a faulted task is never cached.
/// </para>
/// </remarks>
public static class ShortTtlCache
{
    public static readonly TimeSpan Ttl = TimeSpan.FromSeconds(60);

    private static readonly MemoryCache Cache = new(new MemoryCacheOptions { SizeLimit = 10_000 });
    private static readonly ConcurrentDictionary<string, object> InFlight = new();

    public static async Task<T> GetOrComputeAsync<T>(string key, Func<Task<T>> compute)
        where T : class
    {
        if (Cache.TryGetValue(key, out T? hit) && hit is not null)
            return hit;

        var lazy = (Lazy<Task<T>>)InFlight.GetOrAdd(key, _ => new Lazy<Task<T>>(async () =>
        {
            var value = await compute();
            Cache.Set(key, value, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = Ttl,
                Size = 1,
            });
            return value;
        }));

        try
        {
            return await lazy.Value;
        }
        finally
        {
            InFlight.TryRemove(key, out _);
        }
    }
}
