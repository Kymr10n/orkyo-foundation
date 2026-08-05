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

    [Fact]
    public void BuildHostname_Throws_WhenSlugIsEmpty()
    {
        // ICurrentTenant.TenantSlug is "" when no tenant resolved; ".orkyo.com" must never ship.
        var build = () => TenantHostnamePolicy.BuildHostname("orkyo.com", null, "");
        build.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void BuildHost_UsesTenantSubdomain_NotTheApex()
    {
        TenantHostnamePolicy.BuildHost("https://orkyo.com", "orkyo.com", "staging-", "acme")
            .Should().Be("staging-acme.orkyo.com");
    }

    [Fact]
    public void BuildHost_FallsBackToTheAppBaseUrlHost_WhenBaseDomainUnset()
    {
        TenantHostnamePolicy.BuildHost("http://localhost:5173", null, null, "acme")
            .Should().Be("localhost");
    }

    [Fact]
    public void BuildOrigin_UsesTenantSubdomain_NotTheApex()
    {
        TenantHostnamePolicy.BuildOrigin("https://orkyo.com", "orkyo.com", null, "acme")
            .Should().Be("https://acme.orkyo.com");
    }

    [Fact]
    public void BuildOrigin_IncludesPrefix_WhenConfigured()
    {
        TenantHostnamePolicy.BuildOrigin("https://staging.orkyo.com", "orkyo.com", "staging-", "acme")
            .Should().Be("https://staging-acme.orkyo.com");
    }

    [Fact]
    public void BuildOrigin_FallsBackToAppBaseUrl_WhenBaseDomainUnset()
    {
        TenantHostnamePolicy.BuildOrigin("http://localhost:5173", null, null, "acme")
            .Should().Be("http://localhost:5173");
    }
}
