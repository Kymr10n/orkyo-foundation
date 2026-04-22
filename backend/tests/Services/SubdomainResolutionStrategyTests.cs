using Api.Middleware;
using Api.Services;
using Microsoft.AspNetCore.Http;

namespace Orkyo.Foundation.Tests.Services;

public class SubdomainResolutionStrategyTests
{
    [Fact]
    public void ResolveSlug_ReturnsBaseDomainSubdomain_WhenMatched()
    {
        var strategy = CreateStrategy(new TenantMiddlewareOptions
        {
            BaseDomain = "orkyo.com",
            AllowTenantHeader = true
        });

        var context = CreateContext("acme.orkyo.com", headerSlug: "header-tenant", querySlug: "query-tenant");

        var result = strategy.ResolveSlug(context);

        result.Should().Be("acme");
    }

    [Fact]
    public void ResolveSlug_StripsConfiguredSubdomainPrefix()
    {
        var strategy = CreateStrategy(new TenantMiddlewareOptions
        {
            BaseDomain = "orkyo.com",
            SubdomainPrefix = "staging-",
            AllowTenantHeader = false
        });

        var context = CreateContext("staging-acme.orkyo.com");

        var result = strategy.ResolveSlug(context);

        result.Should().Be("acme");
    }

    [Fact]
    public void ResolveSlug_UsesHeaderFallback_WhenNoSubdomainAndHeaderAllowed()
    {
        var strategy = CreateStrategy(new TenantMiddlewareOptions
        {
            BaseDomain = "orkyo.com",
            AllowTenantHeader = true
        });

        var context = CreateContext("orkyo.com", headerSlug: "header-tenant");

        var result = strategy.ResolveSlug(context);

        result.Should().Be("header-tenant");
    }

    [Fact]
    public void ResolveSlug_UsesQueryFallback_WhenNoSubdomainAndNoHeader()
    {
        var strategy = CreateStrategy(new TenantMiddlewareOptions
        {
            BaseDomain = "orkyo.com",
            AllowTenantHeader = true
        });

        var context = CreateContext("orkyo.com", querySlug: "query-tenant");

        var result = strategy.ResolveSlug(context);

        result.Should().Be("query-tenant");
    }

    [Fact]
    public void ResolveSlug_ResolvesLocalhostSubdomain_ForLocalDevHost()
    {
        var strategy = CreateStrategy(new TenantMiddlewareOptions
        {
            BaseDomain = null,
            AllowTenantHeader = false
        });

        var context = CreateContext("demo.localhost");

        var result = strategy.ResolveSlug(context);

        result.Should().Be("demo");
    }

    [Fact]
    public void ResolveSlug_IgnoresHeaderAndQuery_WhenHeaderFallbackDisabled()
    {
        var strategy = CreateStrategy(new TenantMiddlewareOptions
        {
            BaseDomain = "orkyo.com",
            AllowTenantHeader = false
        });

        var context = CreateContext("orkyo.com", headerSlug: "header-tenant", querySlug: "query-tenant");

        var result = strategy.ResolveSlug(context);

        result.Should().BeNull();
    }

    [Fact]
    public void ResolveSlug_UsesConfiguredLocalHosts_WhenProvided()
    {
        var strategy = CreateStrategy(new TenantMiddlewareOptions
        {
            BaseDomain = null,
            LocalHosts = ["dev.local"],
            AllowTenantHeader = false
        });

        var context = CreateContext("acme.dev.local");

        var result = strategy.ResolveSlug(context);

        result.Should().Be("acme");
    }

    [Fact]
    public void ResolveSlug_ReturnsNull_ForNestedLocalSubdomain()
    {
        var strategy = CreateStrategy(new TenantMiddlewareOptions
        {
            BaseDomain = null,
            AllowTenantHeader = false
        });

        var context = CreateContext("a.b.localhost");

        var result = strategy.ResolveSlug(context);

        result.Should().BeNull();
    }

    [Fact]
    public void ResolveSlug_ReturnsNull_WhenLocalSubdomainContainsDot()
    {
        var strategy = CreateStrategy(new TenantMiddlewareOptions
        {
            BaseDomain = "orkyo.com",
            AllowTenantHeader = false
        });

        var context = CreateContext("orkyo.com.localhost");

        var result = strategy.ResolveSlug(context);

        result.Should().BeNull();
    }

    private static SubdomainResolutionStrategy CreateStrategy(TenantMiddlewareOptions options)
        => new(options);

    private static HttpContext CreateContext(
        string host,
        string? headerSlug = null,
        string? querySlug = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Host = new HostString(host);

        if (!string.IsNullOrEmpty(headerSlug))
            context.Request.Headers["X-Tenant-Slug"] = headerSlug;

        if (!string.IsNullOrEmpty(querySlug))
            context.Request.QueryString = new QueryString($"?tenant={querySlug}");

        return context;
    }
}
