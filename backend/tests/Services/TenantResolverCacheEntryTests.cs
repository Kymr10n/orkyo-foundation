using Api.Services;
using Orkyo.Shared;

namespace Orkyo.Foundation.Tests.Services;

public class TenantResolverCacheEntryTests
{
    [Fact]
    public void IsFresh_ShouldReturnTrue_WhenExpiryAfterNow()
    {
        var now = DateTime.UtcNow;
        var entry = new TenantResolverCacheEntry(null, now.AddSeconds(1));

        entry.IsFresh(now).Should().BeTrue();
    }

    [Fact]
    public void IsFresh_ShouldReturnFalse_WhenExpiryEqualsNow()
    {
        var now = DateTime.UtcNow;
        var entry = new TenantResolverCacheEntry(null, now);

        entry.IsFresh(now).Should().BeFalse();
    }

    [Fact]
    public void Constructor_ShouldStoreContextAndExpiry()
    {
        var context = new TenantContext
        {
            TenantId = Guid.NewGuid(),
            TenantSlug = "acme",
            TenantDbConnectionString = "Host=localhost;Database=tenant_acme;Username=postgres;Password=postgres",
            Tier = Api.Models.ServiceTier.Free,
            Status = TenantStatusConstants.Active
        };
        var expiresAtUtc = DateTime.UtcNow.AddMinutes(5);

        var entry = new TenantResolverCacheEntry(context, expiresAtUtc);

        entry.Context.Should().BeSameAs(context);
        entry.ExpiresAtUtc.Should().Be(expiresAtUtc);
    }
}