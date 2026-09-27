using Api.Services;

namespace Orkyo.Foundation.Tests.Services;

public class TenantCacheKeyPolicyTests
{
    [Fact]
    public void Comparer_ShouldBeCaseInsensitive()
    {
        TenantCacheKeyPolicy.Comparer.Equals("AcMe", "acme").Should().BeTrue();
    }
}
