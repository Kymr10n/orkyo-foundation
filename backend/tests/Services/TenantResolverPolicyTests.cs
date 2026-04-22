using Api.Services;
using Microsoft.Extensions.Configuration;
using Orkyo.Shared;

namespace Orkyo.Foundation.Tests.Services;

public class TenantResolverPolicyTests
{
    [Fact]
    public void ResolveControlPlaneConnectionString_ShouldThrow_WhenMissing()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();

        var act = () => TenantResolverPolicy.ResolveControlPlaneConnectionString(configuration);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("ControlPlane connection string not configured");
    }

    [Fact]
    public void ResolveControlPlaneConnectionString_ShouldReturnConfiguredValue()
    {
        const string expected = "Host=localhost;Database=control_plane;Username=postgres;Password=postgres";
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"ConnectionStrings:{ConfigKeys.ConnectionStringControlPlane}"] = expected
            })
            .Build();

        var value = TenantResolverPolicy.ResolveControlPlaneConnectionString(configuration);

        value.Should().Be(expected);
    }
}