using Api.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orkyo.Shared;
using StackExchange.Redis;

namespace Orkyo.Foundation.Tests.Configuration;

public sealed class ValkeyServiceExtensionsTests
{
    [Fact]
    public void RegistersOneSingletonMultiplexer_FromTheConfiguredConnectionString()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [ConfigKeys.ValkeyConnection] = "localhost:6379,abortConnect=false" })
            .Build();
        var services = new ServiceCollection();

        services.AddOrkyoValkey(configuration);

        var descriptor = services.Should().ContainSingle(d => d.ServiceType == typeof(IConnectionMultiplexer)).Subject;
        descriptor.Lifetime.Should().Be(ServiceLifetime.Singleton, "one multiplexer per process");
        descriptor.ImplementationFactory.Should().NotBeNull("the connection opens lazily, on first resolve, not at registration");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void FailsAtRegistration_WhenTheKeyIsMissingOrEmpty(string? value)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [ConfigKeys.ValkeyConnection] = value })
            .Build();

        var act = () => new ServiceCollection().AddOrkyoValkey(configuration);

        act.Should().Throw<InvalidOperationException>().WithMessage($"*{ConfigKeys.ValkeyConnection}*");
    }
}
