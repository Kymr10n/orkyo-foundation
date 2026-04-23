using Microsoft.Extensions.Configuration;
using Orkyo.Shared;
using Orkyo.Shared.Keycloak;

namespace Orkyo.Foundation.Tests.Shared.Keycloak;

public class KeycloakOptionsTests
{
    private static IConfiguration BuildConfig(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static KeycloakOptions BuildOptions(string? internalUrl = null) => new()
    {
        BaseUrl = "https://auth.example.com",
        InternalBaseUrl = internalUrl,
        Realm = "orkyo",
        BackendClientId = "backend",
        BackendClientSecret = "secret",
    };

    [Fact]
    public void Authority_ComposesBaseUrlAndRealm()
    {
        BuildOptions().Authority.Should().Be("https://auth.example.com/realms/orkyo");
    }

    [Fact]
    public void EffectiveInternalBaseUrl_FallsBackToBaseUrl_WhenInternalMissing()
    {
        BuildOptions(internalUrl: null).EffectiveInternalBaseUrl.Should().Be("https://auth.example.com");
    }

    [Fact]
    public void EffectiveInternalBaseUrl_PrefersInternalUrl_WhenSet()
    {
        BuildOptions(internalUrl: "http://keycloak:8080").EffectiveInternalBaseUrl.Should().Be("http://keycloak:8080");
    }

    [Fact]
    public void InternalAuthority_ComposesEffectiveInternalAndRealm()
    {
        BuildOptions(internalUrl: "http://keycloak:8080").InternalAuthority
            .Should().Be("http://keycloak:8080/realms/orkyo");
    }

    [Fact]
    public void IsEnabled_FollowsBaseUrlPresence()
    {
        BuildOptions().IsEnabled.Should().BeTrue();
        var disabled = new KeycloakOptions
        {
            BaseUrl = "",
            Realm = "orkyo",
            BackendClientId = "backend",
            BackendClientSecret = "secret",
        };
        disabled.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void FromConfiguration_ReadsAllConfigKeys()
    {
        var config = BuildConfig(new()
        {
            [ConfigKeys.KeycloakUrl] = "https://auth.example.com",
            [ConfigKeys.KeycloakInternalUrl] = "http://keycloak:8080",
            [ConfigKeys.KeycloakRealm] = "orkyo",
            [ConfigKeys.KeycloakBackendClientId] = "backend",
            [ConfigKeys.KeycloakBackendClientSecret] = "secret",
        });

        var opts = KeycloakOptions.FromConfiguration(config);

        opts.BaseUrl.Should().Be("https://auth.example.com");
        opts.InternalBaseUrl.Should().Be("http://keycloak:8080");
        opts.Realm.Should().Be("orkyo");
        opts.BackendClientId.Should().Be("backend");
        opts.BackendClientSecret.Should().Be("secret");
    }

    [Fact]
    public void FromConfiguration_LeavesInternalBaseUrlNull_WhenKeyMissing()
    {
        var config = BuildConfig(new()
        {
            [ConfigKeys.KeycloakUrl] = "https://auth.example.com",
            [ConfigKeys.KeycloakRealm] = "orkyo",
            [ConfigKeys.KeycloakBackendClientId] = "backend",
            [ConfigKeys.KeycloakBackendClientSecret] = "secret",
        });

        KeycloakOptions.FromConfiguration(config).InternalBaseUrl.Should().BeNull();
    }

    [Theory]
    [InlineData(nameof(ConfigKeys.KeycloakUrl))]
    [InlineData(nameof(ConfigKeys.KeycloakRealm))]
    [InlineData(nameof(ConfigKeys.KeycloakBackendClientId))]
    [InlineData(nameof(ConfigKeys.KeycloakBackendClientSecret))]
    public void FromConfiguration_Throws_WhenRequiredKeyMissing(string keyToOmit)
    {
        var values = new Dictionary<string, string?>
        {
            [ConfigKeys.KeycloakUrl] = "https://auth.example.com",
            [ConfigKeys.KeycloakRealm] = "orkyo",
            [ConfigKeys.KeycloakBackendClientId] = "backend",
            [ConfigKeys.KeycloakBackendClientSecret] = "secret",
        };
        var configKeyValue = (string)typeof(ConfigKeys).GetField(keyToOmit)!.GetRawConstantValue()!;
        values.Remove(configKeyValue);

        var config = BuildConfig(values);
        var act = () => KeycloakOptions.FromConfiguration(config);
        act.Should().Throw<InvalidOperationException>().WithMessage($"*{configKeyValue}*");
    }
}
