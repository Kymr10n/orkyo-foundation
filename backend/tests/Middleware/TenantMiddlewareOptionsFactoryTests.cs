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
    public void FromConfiguration_ShouldDefaultAllowTenantHeaderFalse_WhenValueInvalid()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            [ConfigKeys.TenantResolutionBaseDomain] = "orkyo.com",
            [ConfigKeys.TenantResolutionAllowTenantHeader] = "not-a-bool",
        });

        var options = TenantMiddlewareOptionsFactory.FromConfiguration(configuration);

        options.BaseDomain.Should().Be("orkyo.com");
        options.AllowTenantHeader.Should().BeFalse();
    }

    private static IConfiguration BuildConfiguration(Dictionary<string, string?> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }
}