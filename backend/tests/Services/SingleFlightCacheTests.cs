using Api.Services.Caching;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Internal;
using Xunit;

namespace Orkyo.Foundation.Tests.Services;

/// <summary>
/// The one shared in-process cache. These pin the four properties its callers rely on: a hit is
/// the same instance, a key that was never written is a miss, an entry stops being served once
/// its TTL passes, and a burst of concurrent misses computes once — with a failure never cached.
/// </summary>
public class SingleFlightCacheTests
{
    private sealed record Payload(string Value);

    /// <summary>
    /// ISystemClock, not TimeProvider, and not by preference: MemoryCacheOptions on
    /// Microsoft.Extensions.Caching.Memory 10.0.x exposes only <c>Clock</c>, of that type.
    /// FakeTimeProvider cannot be passed here. Revisit when the package grows a TimeProvider
    /// property; until then this is the only seam the cache offers.
    /// </summary>
    private sealed class TestClock(DateTimeOffset start) : ISystemClock
    {
        public DateTimeOffset UtcNow { get; set; } = start;
    }

    private static SingleFlightCache NewCache(ISystemClock? clock = null) =>
        new(new MemoryCache(new MemoryCacheOptions { Clock = clock }));

    [Fact]
    public async Task SecondCall_IsServedFromCache()
    {
        var cache = NewCache();
        var calls = 0;

        var first = await cache.GetOrComputeAsync("k", TimeSpan.FromMinutes(5),
            () => { calls++; return Task.FromResult(new Payload("v")); });
        var second = await cache.GetOrComputeAsync("k", TimeSpan.FromMinutes(5),
            () => { calls++; return Task.FromResult(new Payload("other")); });

        Assert.Same(first, second);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void TryGet_MissesAnUnwrittenKey_AndHitsAWrittenOne()
    {
        var cache = NewCache();

        Assert.False(cache.TryGet<Payload>("absent", out var missing));
        Assert.Null(missing);

        var payload = new Payload("v");
        cache.Set("present", payload, TimeSpan.FromMinutes(5));

        Assert.True(cache.TryGet<Payload>("present", out var hit));
        Assert.Same(payload, hit);
    }

    [Fact]
    public void Remove_DropsTheEntry()
    {
        var cache = NewCache();
        cache.Set("k", new Payload("v"), TimeSpan.FromMinutes(5));

        cache.Remove("k");

        Assert.False(cache.TryGet<Payload>("k", out _));
    }

    [Fact]
    public void AnEntry_StopsBeingServed_OnceItsTtlPasses()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var cache = NewCache(clock);
        cache.Set("k", new Payload("v"), TimeSpan.FromSeconds(60));

        Assert.True(cache.TryGet<Payload>("k", out _));

        clock.UtcNow = clock.UtcNow.AddSeconds(61);

        Assert.False(cache.TryGet<Payload>("k", out _));
    }

    [Fact]
    public async Task ConcurrentMisses_OnTheSameKey_ComputeOnce()
    {
        var cache = NewCache();
        var gate = new TaskCompletionSource<Payload>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;

        Task<Payload> Ask() => cache.GetOrComputeAsync("k", TimeSpan.FromMinutes(5),
            () => { Interlocked.Increment(ref calls); return gate.Task; });

        var first = Ask();
        var second = Ask();
        var payload = new Payload("v");
        gate.SetResult(payload);

        Assert.Same(payload, await first);
        Assert.Same(payload, await second);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task AFaultedComputation_IsNotCached()
    {
        var cache = NewCache();
        var calls = 0;
        var payload = new Payload("v");

        Task<Payload> Ask() => cache.GetOrComputeAsync("k", TimeSpan.FromMinutes(5),
            () => Interlocked.Increment(ref calls) == 1
                ? Task.FromException<Payload>(new InvalidOperationException("boom"))
                : Task.FromResult(payload));

        await Assert.ThrowsAsync<InvalidOperationException>(Ask);

        Assert.Same(payload, await Ask());
        Assert.Equal(2, calls);
    }
}
