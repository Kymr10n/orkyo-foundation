using Api.Configuration;
using Api.Security;
using Api.Services.BffSession;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orkyo.Shared;

namespace Orkyo.Foundation.Tests.Configuration;

public class BffAuthenticationServiceExtensionsTests
{
    // ── Store registration ────────────────────────────────────────────────────

    [Fact]
    public void AddBffAuthentication_WithoutValkey_RegistersInMemorySessionStore()
    {
        var provider = BuildProvider(valkeyConnection: null);

        provider.GetRequiredService<IBffSessionStore>().Should().BeOfType<InMemoryBffSessionStore>();
    }

    [Fact]
    public void AddBffAuthentication_WithoutValkey_RegistersInMemoryPkceStore()
    {
        var provider = BuildProvider(valkeyConnection: null);

        provider.GetRequiredService<IBffPkceStateStore>().Should().BeOfType<InMemoryBffPkceStateStore>();
    }

    // ── Data Protection ───────────────────────────────────────────────────────

    [Fact]
    public void AddBffAuthentication_SetsApplicationNameToOrkyo()
    {
        var provider = BuildProvider(valkeyConnection: null);

        var dpOptions = provider.GetRequiredService<IOptions<DataProtectionOptions>>();
        dpOptions.Value.ApplicationDiscriminator.Should().Be("orkyo");
    }

    [Fact]
    public void AddBffAuthentication_WithoutValkey_DataProtectionCanProtectAndUnprotect()
    {
        var provider = BuildProvider(valkeyConnection: null);

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
        var provider = BuildProvider(valkeyConnection: null);

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
        var p1 = BuildProvider(valkeyConnection: null);
        var p2 = BuildProvider(valkeyConnection: null);

        var disc1 = p1.GetRequiredService<IOptions<DataProtectionOptions>>().Value.ApplicationDiscriminator;
        var disc2 = p2.GetRequiredService<IOptions<DataProtectionOptions>>().Value.ApplicationDiscriminator;

        disc1.Should().Be(disc2).And.Be("orkyo");
    }

    // ── BffOptions binding ────────────────────────────────────────────────────

    [Fact]
    public void AddBffAuthentication_BffOptions_DefaultCookieName_WhenNotConfigured()
    {
        var provider = BuildProvider(valkeyConnection: null);

        var opts = provider.GetRequiredService<IOptions<BffOptions>>().Value;

        opts.CookieName.Should().Be(BffOptions.DefaultCookieName);
    }

    [Fact]
    public void AddBffAuthentication_BffOptions_OverridesCookieName_WhenConfigured()
    {
        var provider = BuildProvider(valkeyConnection: null, extra: new()
        {
            [ConfigKeys.BffCookieName] = "my-session"
        });

        var opts = provider.GetRequiredService<IOptions<BffOptions>>().Value;

        opts.CookieName.Should().Be("my-session");
    }

    [Fact]
    public void AddBffAuthentication_BffOptions_SetsCookieDomain_WhenConfigured()
    {
        var provider = BuildProvider(valkeyConnection: null, extra: new()
        {
            [ConfigKeys.BffCookieDomain] = ".orkyo.com"
        });

        var opts = provider.GetRequiredService<IOptions<BffOptions>>().Value;

        opts.CookieDomain.Should().Be(".orkyo.com");
    }

    [Fact]
    public void AddBffAuthentication_BffOptions_CookieDomainIsNull_WhenEmptyString()
    {
        var provider = BuildProvider(valkeyConnection: null, extra: new()
        {
            [ConfigKeys.BffCookieDomain] = ""
        });

        var opts = provider.GetRequiredService<IOptions<BffOptions>>().Value;

        opts.CookieDomain.Should().BeNull();
    }

    [Fact]
    public void AddBffAuthentication_BffOptions_CookieSecureFalse_WhenExplicitlyDisabled()
    {
        var provider = BuildProvider(valkeyConnection: null, extra: new()
        {
            [ConfigKeys.BffCookieSecure] = "false"
        });

        var opts = provider.GetRequiredService<IOptions<BffOptions>>().Value;

        opts.CookieSecure.Should().BeFalse();
    }

    [Fact]
    public void AddBffAuthentication_BffOptions_CookieSecureTrue_WhenExplicitlyEnabled()
    {
        var provider = BuildProvider(valkeyConnection: null, extra: new()
        {
            [ConfigKeys.BffCookieSecure] = "true"
        });

        var opts = provider.GetRequiredService<IOptions<BffOptions>>().Value;

        opts.CookieSecure.Should().BeTrue();
    }

    [Fact]
    public void AddBffAuthentication_BffOptions_CookieSecureDefaultsTrueInProduction()
    {
        // When BFF_COOKIE_SECURE is absent and environment is Production, secure defaults to true
        var provider = BuildProvider(valkeyConnection: null, environmentName: EnvironmentNames.Production);

        var opts = provider.GetRequiredService<IOptions<BffOptions>>().Value;

        opts.CookieSecure.Should().BeTrue();
    }

    [Fact]
    public void AddBffAuthentication_BffOptions_CookieSecureDefaultsFalseInDevelopment()
    {
        var provider = BuildProvider(valkeyConnection: null, environmentName: EnvironmentNames.Development);

        var opts = provider.GetRequiredService<IOptions<BffOptions>>().Value;

        opts.CookieSecure.Should().BeFalse();
    }

    [Fact]
    public void AddBffAuthentication_BffOptions_SetsRedirectUri_WhenConfigured()
    {
        var provider = BuildProvider(valkeyConnection: null, extra: new()
        {
            [ConfigKeys.BffRedirectUri] = "https://orkyo.com/api/auth/bff/callback"
        });

        var opts = provider.GetRequiredService<IOptions<BffOptions>>().Value;

        opts.RedirectUri.Should().Be("https://orkyo.com/api/auth/bff/callback");
    }

    [Fact]
    public void AddBffAuthentication_BffOptions_SetsAppBaseUrl_WhenConfigured()
    {
        var provider = BuildProvider(valkeyConnection: null, extra: new()
        {
            [ConfigKeys.AppBaseUrl] = "http://localhost:5174"
        });

        var opts = provider.GetRequiredService<IOptions<BffOptions>>().Value;

        opts.AppBaseUrl.Should().Be("http://localhost:5174");
    }

    [Fact]
    public void AddBffAuthentication_BffOptions_SetsAllowedHosts_WhenConfigured()
    {
        var provider = BuildProvider(valkeyConnection: null, extra: new()
        {
            [ConfigKeys.BffAllowedHosts] = "orkyo.com,*.orkyo.com"
        });

        var opts = provider.GetRequiredService<IOptions<BffOptions>>().Value;

        opts.AllowedReturnToHosts.Should().BeEquivalentTo(["orkyo.com", "*.orkyo.com"]);
    }

    [Fact]
    public void AddBffAuthentication_BffOptions_SetsSessionDuration_WhenConfigured()
    {
        var provider = BuildProvider(valkeyConnection: null, extra: new()
        {
            [ConfigKeys.BffSessionDuration] = "12:00:00"
        });

        var opts = provider.GetRequiredService<IOptions<BffOptions>>().Value;

        opts.SessionDuration.Should().Be(TimeSpan.FromHours(12));
    }

    [Fact]
    public void AddBffAuthentication_BffOptions_SetsScopes_WhenConfigured()
    {
        var provider = BuildProvider(valkeyConnection: null, extra: new()
        {
            [ConfigKeys.BffScopes] = "openid profile email offline_access"
        });

        var opts = provider.GetRequiredService<IOptions<BffOptions>>().Value;

        opts.Scopes.Should().Be("openid profile email offline_access");
    }

    // ── BFF authentication scheme ─────────────────────────────────────────────

    [Fact]
    public async Task AddBffAuthentication_RegistersBffAuthScheme_WhenBffEnabled()
    {
        var provider = BuildProvider(valkeyConnection: null, extra: new()
        {
            [ConfigKeys.BffEnabled] = "true"
        });

        var schemes = provider.GetRequiredService<IAuthenticationSchemeProvider>();
        var scheme = await schemes.GetSchemeAsync(BffCookieAuthenticationHandler.SchemeName);

        scheme.Should().NotBeNull();
        scheme!.HandlerType.Should().Be(typeof(BffCookieAuthenticationHandler));
    }

    [Fact]
    public async Task AddBffAuthentication_DoesNotRegisterBffAuthScheme_WhenBffDisabled()
    {
        var provider = BuildProvider(valkeyConnection: null);

        // AddAuthentication() is not called when BFF is disabled, so the scheme provider is absent
        var schemes = provider.GetService<IAuthenticationSchemeProvider>();
        if (schemes is not null)
        {
            var scheme = await schemes.GetSchemeAsync(BffCookieAuthenticationHandler.SchemeName);
            scheme.Should().BeNull();
        }
        // If schemes == null, AddAuthentication() was never called — BFF scheme definitely absent
    }

    // ── No Valkey connection → in-memory stores ───────────────────────────────

    [Fact]
    public void AddBffAuthentication_RegistersInMemoryStores_WhenNoValkeyConnection()
    {
        // No VALKEY_CONNECTION → fall back to in-memory
        var provider = BuildProvider(valkeyConnection: null);

        provider.GetRequiredService<IBffSessionStore>().Should().BeOfType<InMemoryBffSessionStore>();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ServiceProvider BuildProvider(
        string? valkeyConnection,
        Dictionary<string, string?>? extra = null,
        string environmentName = EnvironmentNames.Production)
    {
        var values = new Dictionary<string, string?>();
        if (valkeyConnection != null)
            values[ConfigKeys.ValkeyConnection] = valkeyConnection;
        if (extra != null)
            foreach (var (k, v) in extra)
                values[k] = v;

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddConsole());
        services.AddSingleton<IConfiguration>(config);
        services.AddSingleton<IHostEnvironment>(new FakeHostEnvironment(environmentName));
        services.AddBffAuthentication(config);

        return services.BuildServiceProvider();
    }

    private sealed class FakeHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Orkyo.Foundation.Tests";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
