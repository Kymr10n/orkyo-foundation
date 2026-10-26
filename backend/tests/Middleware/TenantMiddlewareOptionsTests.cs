using Api.Middleware;

namespace Orkyo.Foundation.Tests.Middleware;

public class TenantMiddlewareOptionsTests
{
    [Fact]
    public void BuildTenantHostname_ReturnsNull_WhenBaseDomainMissing()
    {
        var options = new TenantMiddlewareOptions
        {
            BaseDomain = null
        };

        var hostname = options.BuildTenantHostname("acme");

        hostname.Should().BeNull();
    }

    [Fact]
    public void BuildTenantHostname_IncludesPrefix_WhenConfigured()
    {
        var options = new TenantMiddlewareOptions
        {
            BaseDomain = "orkyo.com",
            SubdomainPrefix = "staging-"
        };

        var hostname = options.BuildTenantHostname("acme");

        hostname.Should().Be("staging-acme.orkyo.com");
    }

    [Fact]
    public void ExtractSlugFromHost_ReturnsSlug_ForValidSubdomain()
    {
        var options = new TenantMiddlewareOptions
        {
            BaseDomain = "orkyo.com"
        };

        var slug = options.ExtractSlugFromHost("acme.orkyo.com");

        slug.Should().Be("acme");
    }

    [Fact]
    public void ExtractSlugFromHost_ReturnsNull_ForApexDomain()
    {
        var options = new TenantMiddlewareOptions
        {
            BaseDomain = "orkyo.com"
        };

        var slug = options.ExtractSlugFromHost("orkyo.com");

        slug.Should().BeNull();
    }

    [Fact]
    public void ExtractSlugFromHost_ReturnsNull_ForNestedSubdomain()
    {
        var options = new TenantMiddlewareOptions
        {
            BaseDomain = "orkyo.com"
        };

        var slug = options.ExtractSlugFromHost("a.b.orkyo.com");

        slug.Should().BeNull();
    }

    [Fact]
    public void ExtractSlugFromHost_StripsPrefix_WhenMatched()
    {
        var options = new TenantMiddlewareOptions
        {
            BaseDomain = "orkyo.com",
            SubdomainPrefix = "staging-"
        };

        var slug = options.ExtractSlugFromHost("staging-acme.orkyo.com");

        slug.Should().Be("acme");
    }

    [Fact]
    public void ExtractSlugFromHost_ReturnsNull_WhenPrefixDoesNotMatch()
    {
        var options = new TenantMiddlewareOptions
        {
            BaseDomain = "orkyo.com",
            SubdomainPrefix = "staging-"
        };

        var slug = options.ExtractSlugFromHost("acme.orkyo.com");

        slug.Should().BeNull();
    }

    [Fact]
    public void ExtractSlugFromHost_IsCaseInsensitive_ForHostAndBaseDomain()
    {
        var options = new TenantMiddlewareOptions
        {
            BaseDomain = "OrKyO.CoM"
        };

        var slug = options.ExtractSlugFromHost("AcMe.OrKyO.CoM");

        slug.Should().Be("acme");
    }

    [Fact]
    public void ExtractSlugFromHost_IgnoresPortSuffix()
    {
        var options = new TenantMiddlewareOptions
        {
            BaseDomain = "orkyo.com"
        };

        var slug = options.ExtractSlugFromHost("acme.orkyo.com:8080");

        slug.Should().Be("acme");
    }

    [Fact]
    public void ExtractSlugFromHost_ReturnsNull_WhenTheHostIsOnlyThePrefix()
    {
        // "staging-.orkyo.com" starts with the prefix and nothing follows: no slug, not an empty one.
        var options = new TenantMiddlewareOptions { BaseDomain = "orkyo.com", SubdomainPrefix = "staging-" };

        options.ExtractSlugFromHost("staging-.orkyo.com").Should().BeNull();
    }

    [Theory]
    [InlineData("orkyo.com.evil.test")]
    [InlineData("notorkyo.com")]
    [InlineData("acme.orkyo.com.evil.test")]
    public void ExtractSlugFromHost_ReturnsNull_ForALookalikeHost(string host)
    {
        var options = new TenantMiddlewareOptions { BaseDomain = "orkyo.com" };

        options.ExtractSlugFromHost(host).Should().BeNull();
    }
}
