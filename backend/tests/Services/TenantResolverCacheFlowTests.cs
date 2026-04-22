using System.Collections.Concurrent;
using Api.Models;
using Api.Services;
using Orkyo.Shared;

namespace Orkyo.Foundation.Tests.Services;

public class TenantResolverCacheFlowTests
{
    [Fact]
    public void TryGetFresh_ShouldReturnFalseAndNull_WhenMiss()
    {
        var cache = NewCache();

        var found = TenantResolverCacheFlow.TryGetFresh(cache, "acme", DateTime.UtcNow, out var context);

        found.Should().BeFalse();
        context.Should().BeNull();
    }

    [Fact]
    public void StoreThenTryGetFresh_ShouldReturnHit_CaseInsensitively()
    {
        var cache = NewCache();
        var context = MakeContext("AcMe");
        var now = DateTime.UtcNow;

        TenantResolverCacheFlow.Store(cache, "AcMe", context, now);

        var found = TenantResolverCacheFlow.TryGetFresh(cache, "acme", now, out var cachedContext);

        found.Should().BeTrue();
        cachedContext.Should().BeSameAs(context);
    }

    [Fact]
    public void TryGetFresh_ShouldReturnFalse_WhenEntryExpired()
    {
        var cache = NewCache();
        var now = DateTime.UtcNow;
        cache["acme"] = new TenantResolverCacheEntry(MakeContext("acme"), now.AddSeconds(-1));

        var found = TenantResolverCacheFlow.TryGetFresh(cache, "acme", now, out var context);

        found.Should().BeFalse();
        context.Should().BeNull();
    }

    [Fact]
    public void Invalidate_ShouldRemoveEntry_CaseInsensitively()
    {
        var cache = NewCache();
        TenantResolverCacheFlow.Store(cache, "AcMe", MakeContext("AcMe"), DateTime.UtcNow);

        TenantResolverCacheFlow.Invalidate(cache, "acme");

        cache.Count.Should().Be(0);
    }

    private static ConcurrentDictionary<string, TenantResolverCacheEntry> NewCache()
    {
        return new ConcurrentDictionary<string, TenantResolverCacheEntry>(TenantCacheKeyPolicy.Comparer);
    }

    private static TenantContext MakeContext(string slug)
    {
        return new TenantContext
        {
            TenantId = Guid.NewGuid(),
            TenantSlug = slug,
            TenantDbConnectionString = "Host=localhost;Database=tenant;Username=postgres;Password=postgres",
            Tier = ServiceTier.Free,
            Status = TenantStatusConstants.Active
        };
    }
}