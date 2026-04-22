using Api.Services;

namespace Orkyo.Foundation.Tests.Services;

public class TenantCachePolicyTests
{
    [Fact]
    public void IsFresh_ShouldReturnTrue_WhenExpiryIsInFuture()
    {
        var now = DateTime.UtcNow;

        var isFresh = TenantCachePolicy.IsFresh(now.AddSeconds(1), now);

        isFresh.Should().BeTrue();
    }

    [Fact]
    public void IsFresh_ShouldReturnFalse_WhenExpiryEqualsNow()
    {
        var now = DateTime.UtcNow;

        var isFresh = TenantCachePolicy.IsFresh(now, now);

        isFresh.Should().BeFalse();
    }

    [Fact]
    public void IsFresh_ShouldReturnFalse_WhenExpiryIsInPast()
    {
        var now = DateTime.UtcNow;

        var isFresh = TenantCachePolicy.IsFresh(now.AddSeconds(-1), now);

        isFresh.Should().BeFalse();
    }
}