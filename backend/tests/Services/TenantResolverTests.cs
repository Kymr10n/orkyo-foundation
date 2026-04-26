using System.Collections.Concurrent;
using System.Reflection;
using Api.Models;
using Api.Services;
using Microsoft.Extensions.Configuration;
using Orkyo.Shared;

namespace Orkyo.Foundation.Tests.Services;

public class TenantResolverTests
{
    public TenantResolverTests()
    {
        TenantResolver.ClearCache();
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenControlPlaneConnectionStringMissing()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();

        var act = () => new TenantResolver(config);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("ControlPlane connection string not configured");
    }

    [Fact]
    public void InvalidateCache_ShouldEvictEntry_CaseInsensitively()
    {
        var resolver = CreateResolver();
        var tenant = MakeTenant("acme");
        SetCacheEntry("AcMe", tenant, DateTime.UtcNow.AddMinutes(5));

        resolver.InvalidateCache("acme");

        // Verify cache no longer holds the entry — avoids a real DB call
        var stillCached = TenantResolverCacheFlow.TryGetFresh(
            GetCache(), "AcMe", DateTime.UtcNow, out var ctx);
        stillCached.Should().BeFalse();
        ctx.Should().BeNull();
    }

    [Fact]
    public void ClearCache_ShouldRemoveAllEntries()
    {
        SetCacheEntry("a", MakeTenant("a"), DateTime.UtcNow.AddMinutes(5));
        SetCacheEntry("b", MakeTenant("b"), DateTime.UtcNow.AddMinutes(5));

        TenantResolver.ClearCache();

        GetCache().Count.Should().Be(0);
    }

    private static TenantResolver CreateResolver()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"ConnectionStrings:{ConfigKeys.ConnectionStringControlPlane}"] =
                    "Host=localhost;Database=control_plane;Username=postgres;Password=postgres"
            })
            .Build();

        return new TenantResolver(config);
    }

    private static TenantContext MakeTenant(string slug)
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

    private static void SetCacheEntry(string slug, TenantContext? context, DateTime expiresAtUtc)
    {
        GetCache()[slug] = new TenantResolverCacheEntry(context, expiresAtUtc);
    }

    private static ConcurrentDictionary<string, TenantResolverCacheEntry> GetCache()
    {
        var field = typeof(TenantResolver).GetField("_cache", BindingFlags.NonPublic | BindingFlags.Static);
        field.Should().NotBeNull();

        return (ConcurrentDictionary<string, TenantResolverCacheEntry>)field!.GetValue(null)!;
    }
}
