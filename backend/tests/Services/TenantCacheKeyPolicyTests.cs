using Api.Services;

namespace Orkyo.Foundation.Tests.Services;

public class TenantCacheKeyPolicyTests
{
    [Fact]
    public void Comparer_ShouldBeCaseInsensitive()
    {
        TenantCacheKeyPolicy.Comparer.Equals("AcMe", "acme").Should().BeTrue();
    }

    [Fact]
    public void Canonicalize_ShouldPreserveInput_ForCurrentPolicy()
    {
        var key = TenantCacheKeyPolicy.Canonicalize("AcMe-Tenant");

        key.Should().Be("AcMe-Tenant");
    }
}