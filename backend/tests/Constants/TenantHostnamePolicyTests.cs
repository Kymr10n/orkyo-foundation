using Orkyo.Shared;

namespace Orkyo.Foundation.Tests.Constants;

public class TenantHostnamePolicyTests
{
    [Fact]
    public void BuildHostname_ReturnsNull_WhenBaseDomainMissing()
    {
        TenantHostnamePolicy.BuildHostname(null, null, "acme").Should().BeNull();
        TenantHostnamePolicy.BuildHostname("", "staging-", "acme").Should().BeNull();
    }

    [Fact]
    public void BuildHostname_BuildsPlainSubdomain()
    {
        TenantHostnamePolicy.BuildHostname("orkyo.com", null, "acme")
            .Should().Be("acme.orkyo.com");
    }

    [Fact]
    public void BuildHostname_IncludesPrefix_WhenConfigured()
    {
        TenantHostnamePolicy.BuildHostname("orkyo.com", "staging-", "acme")
            .Should().Be("staging-acme.orkyo.com");
    }
}
