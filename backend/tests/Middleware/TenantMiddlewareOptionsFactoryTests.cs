using Api.Middleware;
using Microsoft.Extensions.Configuration;
using Orkyo.Shared;

namespace Orkyo.Foundation.Tests.Middleware;

public class TenantMiddlewareOptionsFactoryTests
{
    [Fact]
    public void FromConfiguration_ShouldMapBaseDomain_AndAllowTenantHeaderTrue()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            [ConfigKeys.TenantResolutionBaseDomain] = "orkyo.com",
            [ConfigKeys.TenantResolutionAllowTenantHeader] = "true",
        });

        var options = TenantMiddlewareOptionsFactory.FromConfiguration(configuration);

        options.BaseDomain.Should().Be("orkyo.com");
        options.AllowTenantHeader.Should().BeTrue();
    }

    [Fact]
    public void FromConfiguration_ShouldThrow_WhenAllowTenantHeaderInvalid()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            [ConfigKeys.TenantResolutionBaseDomain] = "orkyo.com",
            [ConfigKeys.TenantResolutionAllowTenantHeader] = "not-a-bool",
        });

        var act = () => TenantMiddlewareOptionsFactory.FromConfiguration(configuration);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void FromConfiguration_ShouldMapSubdomainPrefix()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            [ConfigKeys.TenantResolutionBaseDomain] = "orkyo.com",
            [ConfigKeys.TenantResolutionSubdomainPrefix] = "staging-",
        });

        var options = TenantMiddlewareOptionsFactory.FromConfiguration(configuration);

        options.SubdomainPrefix.Should().Be("staging-");
    }

    [Fact]
    public void FromConfiguration_SubdomainPrefix_EnablesSlugExtraction()
    {
        // Regression: SubdomainPrefix was never read from configuration, causing
        // staging-googleloo.orkyo.com to resolve as slug "staging-googleloo" instead of "googleloo".
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            [ConfigKeys.TenantResolutionBaseDomain] = "orkyo.com",
            [ConfigKeys.TenantResolutionSubdomainPrefix] = "staging-",
        });

        var options = TenantMiddlewareOptionsFactory.FromConfiguration(configuration);

        options.ExtractSlugFromHost("staging-googleloo.orkyo.com").Should().Be("googleloo");
    }

    [Fact]
    public void FromConfiguration_WithoutSubdomainPrefix_ReturnsRawSubdomain()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            [ConfigKeys.TenantResolutionBaseDomain] = "orkyo.com",
        });

        var options = TenantMiddlewareOptionsFactory.FromConfiguration(configuration);

        options.ExtractSlugFromHost("acme.orkyo.com").Should().Be("acme");
    }

    private static IConfiguration BuildConfiguration(Dictionary<string, string?> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }
}