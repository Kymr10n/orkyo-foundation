using Api.Configuration;
using Api.Services.BffSession;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orkyo.Shared;

namespace Orkyo.Foundation.Tests.Configuration;

public class BffAuthenticationServiceExtensionsTests
{
    // ── Store registration ────────────────────────────────────────────────────

    [Fact]
    public void AddBffAuthentication_WithoutRedis_RegistersInMemorySessionStore()
    {
        var provider = BuildProvider(redisConnection: null);

        provider.GetRequiredService<IBffSessionStore>().Should().BeOfType<InMemoryBffSessionStore>();
    }

    [Fact]
    public void AddBffAuthentication_WithoutRedis_RegistersInMemoryPkceStore()
    {
        var provider = BuildProvider(redisConnection: null);

        provider.GetRequiredService<IBffPkceStateStore>().Should().BeOfType<InMemoryBffPkceStateStore>();
    }

    // ── Data Protection ───────────────────────────────────────────────────────

    [Fact]
    public void AddBffAuthentication_SetsApplicationNameToOrkyo()
    {
        var provider = BuildProvider(redisConnection: null);

        var dpOptions = provider.GetRequiredService<IOptions<DataProtectionOptions>>();
        dpOptions.Value.ApplicationDiscriminator.Should().Be("orkyo");
    }

    [Fact]
    public void AddBffAuthentication_WithoutRedis_DataProtectionCanProtectAndUnprotect()
    {
        var provider = BuildProvider(redisConnection: null);

        var dp = provider.GetRequiredService<IDataProtectionProvider>();
        var protector = dp.CreateProtector("BffSession");

        const string sessionId = "test-session-id-abc123";
        var ciphertext = protector.Protect(sessionId);
        var roundTripped = protector.Unprotect(ciphertext);

        roundTripped.Should().Be(sessionId);
    }

    [Fact]
    public void AddBffAuthentication_DataProtectionCiphertextNotEqualToPlaintext()
    {
        var provider = BuildProvider(redisConnection: null);

        var dp = provider.GetRequiredService<IDataProtectionProvider>();
        var protector = dp.CreateProtector("BffSession");

        const string sessionId = "test-session-id-abc123";
        var ciphertext = protector.Protect(sessionId);

        ciphertext.Should().NotBe(sessionId);
    }

    [Fact]
    public void AddBffAuthentication_ApplicationDiscriminatorIsStable()
    {
        // Two independently built containers must resolve the same discriminator
        // so that blue/green slots encrypt with the same app-scoped key ring.
        var p1 = BuildProvider(redisConnection: null);
        var p2 = BuildProvider(redisConnection: null);

        var disc1 = p1.GetRequiredService<IOptions<DataProtectionOptions>>().Value.ApplicationDiscriminator;
        var disc2 = p2.GetRequiredService<IOptions<DataProtectionOptions>>().Value.ApplicationDiscriminator;

        disc1.Should().Be(disc2).And.Be("orkyo");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ServiceProvider BuildProvider(string? redisConnection)
    {
        var values = new Dictionary<string, string?>();
        if (redisConnection != null)
            values[ConfigKeys.RedisConnection] = redisConnection;

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddConsole());
        services.AddBffAuthentication(config);

        return services.BuildServiceProvider();
    }
}
