using Api.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Orkyo.Shared;
using StackExchange.Redis;

namespace Orkyo.Foundation.Tests.Configuration;

public sealed class ValkeyServiceExtensionsTests
{
    [Fact]
    public void RegistersAReadinessProbe_SoAnUnreachableValkeyFailsHealth()
    {
        var services = new ServiceCollection();
        services.AddOrkyoValkey(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [ConfigKeys.ValkeyConnection] = "localhost:6379,abortConnect=false" })
            .Build());

        var registrations = services.BuildServiceProvider()
            .GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations;

        var valkey = registrations.Should().ContainSingle(r => r.Name == "valkey").Subject;
        valkey.Tags.Should().Contain("ready", "/health/ready gates the cutover");
        valkey.FailureStatus.Should().Be(HealthStatus.Unhealthy);
    }

    [Fact]
    public async Task TheProbe_ReportsUnhealthy_WhenThePingThrows()
    {
        var db = new Mock<IDatabase>();
        db.Setup(d => d.PingAsync(It.IsAny<CommandFlags>())).ThrowsAsync(new RedisException("down"));
        var valkey = new Mock<IConnectionMultiplexer>();
        valkey.Setup(v => v.GetDatabase(It.IsAny<int>(), It.IsAny<object?>())).Returns(db.Object);
        var registration = new HealthCheckRegistration("valkey", _ => new ValkeyHealthCheck(valkey.Object), HealthStatus.Unhealthy, ["ready"]);

        var result = await new ValkeyHealthCheck(valkey.Object).CheckHealthAsync(new HealthCheckContext { Registration = registration });

        result.Status.Should().Be(HealthStatus.Unhealthy);
    }

    [Fact]
    public async Task TheProbe_ReportsHealthy_WhenThePingAnswers()
    {
        var db = new Mock<IDatabase>();
        db.Setup(d => d.PingAsync(It.IsAny<CommandFlags>())).ReturnsAsync(TimeSpan.FromMilliseconds(1));
        var valkey = new Mock<IConnectionMultiplexer>();
        valkey.Setup(v => v.GetDatabase(It.IsAny<int>(), It.IsAny<object?>())).Returns(db.Object);
        var registration = new HealthCheckRegistration("valkey", _ => new ValkeyHealthCheck(valkey.Object), HealthStatus.Unhealthy, ["ready"]);

        var result = await new ValkeyHealthCheck(valkey.Object).CheckHealthAsync(new HealthCheckContext { Registration = registration });

        result.Status.Should().Be(HealthStatus.Healthy);
    }

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
