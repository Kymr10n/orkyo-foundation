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
        PasswordCheckClientId = TestConstants.CheckClientId,
        PasswordCheckClientSecret = TestConstants.CheckClientCredential,
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
    public void FromConfiguration_ReadsAllConfigKeys()
    {
        var config = BuildConfig(new()
        {
            [ConfigKeys.KeycloakUrl] = "https://auth.example.com",
            [ConfigKeys.KeycloakInternalUrl] = "http://keycloak:8080",
            [ConfigKeys.KeycloakRealm] = "orkyo",
            [ConfigKeys.KeycloakBackendClientId] = "backend",
            [ConfigKeys.KeycloakBackendClientSecret] = "secret",
            [ConfigKeys.KeycloakPasswordCheckClientId] = TestConstants.CheckClientId,
            [ConfigKeys.KeycloakPasswordCheckClientSecret] = TestConstants.CheckClientCredential,
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
            [ConfigKeys.KeycloakPasswordCheckClientId] = TestConstants.CheckClientId,
            [ConfigKeys.KeycloakPasswordCheckClientSecret] = TestConstants.CheckClientCredential,
        });

        KeycloakOptions.FromConfiguration(config).InternalBaseUrl.Should().BeNull();
    }

    [Theory]
    [InlineData(nameof(ConfigKeys.KeycloakUrl))]
    [InlineData(nameof(ConfigKeys.KeycloakRealm))]
    [InlineData(nameof(ConfigKeys.KeycloakBackendClientId))]
    [InlineData(nameof(ConfigKeys.KeycloakBackendClientSecret))]
    [InlineData(nameof(ConfigKeys.KeycloakPasswordCheckClientId))]
    [InlineData(nameof(ConfigKeys.KeycloakPasswordCheckClientSecret))]
    public void FromConfiguration_Throws_WhenRequiredKeyMissing(string keyToOmit)
    {
        var values = new Dictionary<string, string?>
        {
            [ConfigKeys.KeycloakUrl] = "https://auth.example.com",
            [ConfigKeys.KeycloakRealm] = "orkyo",
            [ConfigKeys.KeycloakBackendClientId] = "backend",
            [ConfigKeys.KeycloakBackendClientSecret] = "secret",
            [ConfigKeys.KeycloakPasswordCheckClientId] = TestConstants.CheckClientId,
            [ConfigKeys.KeycloakPasswordCheckClientSecret] = TestConstants.CheckClientCredential,
        };
        var configKeyValue = (string)typeof(ConfigKeys).GetField(keyToOmit)!.GetRawConstantValue()!;
        values.Remove(configKeyValue);

        var config = BuildConfig(values);
        var act = () => KeycloakOptions.FromConfiguration(config);
        act.Should().Throw<InvalidOperationException>().WithMessage($"*{configKeyValue}*");
    }

    [Theory]
    [InlineData(nameof(ConfigKeys.KeycloakUrl))]
    [InlineData(nameof(ConfigKeys.KeycloakRealm))]
    [InlineData(nameof(ConfigKeys.KeycloakBackendClientId))]
    [InlineData(nameof(ConfigKeys.KeycloakBackendClientSecret))]
    [InlineData(nameof(ConfigKeys.KeycloakPasswordCheckClientId))]
    [InlineData(nameof(ConfigKeys.KeycloakPasswordCheckClientSecret))]
    public void FromConfiguration_Throws_WhenRequiredKeyIsEmpty(string keyToEmpty)
    {
        // The deploy pipeline writes KEY= for an unset key: empty must fail like absent.
        var values = new Dictionary<string, string?>
        {
            [ConfigKeys.KeycloakUrl] = "https://auth.example.com",
            [ConfigKeys.KeycloakRealm] = "orkyo",
            [ConfigKeys.KeycloakBackendClientId] = "backend",
            [ConfigKeys.KeycloakBackendClientSecret] = "secret",
            [ConfigKeys.KeycloakPasswordCheckClientId] = TestConstants.CheckClientId,
            [ConfigKeys.KeycloakPasswordCheckClientSecret] = TestConstants.CheckClientCredential,
        };
        var configKeyValue = (string)typeof(ConfigKeys).GetField(keyToEmpty)!.GetRawConstantValue()!;
        values[configKeyValue] = "";

        var act = () => KeycloakOptions.FromConfiguration(BuildConfig(values));

        act.Should().Throw<InvalidOperationException>().WithMessage($"*{configKeyValue}*");
    }

    [Fact]
    public void FromConfiguration_TreatsAnEmptyInternalUrlAsUnset()
    {
        // An empty KEYCLOAK_INTERNAL_URL used to become "" and turn every token and admin
        // URL into a relative one.
        var opts = KeycloakOptions.FromConfiguration(BuildConfig(new()
        {
            [ConfigKeys.KeycloakUrl] = "https://auth.example.com",
            [ConfigKeys.KeycloakInternalUrl] = "",
            [ConfigKeys.KeycloakRealm] = "orkyo",
            [ConfigKeys.KeycloakBackendClientId] = "backend",
            [ConfigKeys.KeycloakBackendClientSecret] = "secret",
            [ConfigKeys.KeycloakPasswordCheckClientId] = TestConstants.CheckClientId,
            [ConfigKeys.KeycloakPasswordCheckClientSecret] = TestConstants.CheckClientCredential,
        }));

        opts.InternalBaseUrl.Should().BeNull();
        opts.InternalAuthority.Should().Be("https://auth.example.com/realms/orkyo");
    }

    [Fact]
    public void EffectiveInternalBaseUrl_FallsBackToBaseUrl_WhenInternalEmpty()
    {
        BuildOptions(internalUrl: "").EffectiveInternalBaseUrl.Should().Be("https://auth.example.com");
    }
}
