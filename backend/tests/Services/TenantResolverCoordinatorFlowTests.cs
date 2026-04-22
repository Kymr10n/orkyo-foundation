using System.Collections.Concurrent;
using Api.Models;
using Api.Services;
using Orkyo.Shared;

namespace Orkyo.Foundation.Tests.Services;

public class TenantResolverCoordinatorFlowTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData(null, "")]
    [InlineData(null, "   ")]
    public async Task ResolveAsync_ShouldReturnNull_WhenSlugCandidatesBlank(string? subdomain, string? tenantHeader)
    {
        var cache = NewCache();
        var dbCalls = 0;

        var result = await TenantResolverCoordinatorFlow.ResolveAsync(
            subdomain,
            tenantHeader,
            cache,
            _ =>
            {
                dbCalls++;
                return Task.FromResult<TenantContext?>(null);
            });

        result.Should().BeNull();
        dbCalls.Should().Be(0);
    }

    [Fact]
    public async Task ResolveAsync_ShouldReturnFreshCachedContext_WithoutDbCall()
    {
        var cache = NewCache();
        var cached = MakeContext("acme");
        TenantResolverCacheFlow.Store(cache, "acme", cached, DateTime.UtcNow);
        var dbCalls = 0;

        var result = await TenantResolverCoordinatorFlow.ResolveAsync(
            "acme",
            "header",
            cache,
            _ =>
            {
                dbCalls++;
                return Task.FromResult<TenantContext?>(null);
            });

        result.Should().BeSameAs(cached);
        dbCalls.Should().Be(0);
    }

    [Fact]
    public async Task ResolveAsync_ShouldPreferSubdomainOverHeader_WhenBothProvided()
    {
        var cache = NewCache();
        var subdomainTenant = MakeContext("subdomain-tenant");
        TenantResolverCacheFlow.Store(cache, "subdomain-tenant", subdomainTenant, DateTime.UtcNow);
        var dbCalls = 0;

        var result = await TenantResolverCoordinatorFlow.ResolveAsync(
            "subdomain-tenant",
            "header-tenant",
            cache,
            _ =>
            {
                dbCalls++;
                return Task.FromResult<TenantContext?>(null);
            });

        result.Should().BeSameAs(subdomainTenant);
        result!.TenantSlug.Should().Be("subdomain-tenant");
        dbCalls.Should().Be(0);
    }

    [Fact]
    public async Task ResolveAsync_ShouldCallDbAndCacheResult_WhenCacheMiss()
    {
        var cache = NewCache();
        var fromDb = MakeContext("acme");
        var dbCalls = 0;

        var result = await TenantResolverCoordinatorFlow.ResolveAsync(
            "acme",
            null,
            cache,
            _ =>
            {
                dbCalls++;
                return Task.FromResult<TenantContext?>(fromDb);
            });

        result.Should().BeSameAs(fromDb);
        dbCalls.Should().Be(1);

        var hit = TenantResolverCacheFlow.TryGetFresh(cache, "acme", DateTime.UtcNow, out var cached);
        hit.Should().BeTrue();
        cached.Should().BeSameAs(fromDb);
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