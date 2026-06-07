using Api.Configuration;
using Microsoft.Extensions.Configuration;
using Orkyo.Shared;

namespace Orkyo.Foundation.Tests.Configuration;

public class DeploymentConfigTests
{
    [Fact]
    public void FromConfiguration_Throws_WhenRequiredKeyMissing()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            [ConfigKeys.AppBaseUrl] = "http://localhost:8080"
            // Intentionally missing OIDC_AUTHORITY and other required keys.
        });

        var act = () => DeploymentConfig.FromConfiguration(config);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*required key*");
    }

    [Fact]
    public void FromConfiguration_BuildsConfig_WhenRequiredValuesPresent()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            [ConfigKeys.AppBaseUrl] = "http://localhost:8080",
            [ConfigKeys.OidcAuthority] = "http://localhost:8180/realms/orkyo",
            [ConfigKeys.KeycloakUrl] = "http://localhost:8180",
            [ConfigKeys.KeycloakRealm] = "orkyo",
            [ConfigKeys.KeycloakBackendClientId] = "orkyo-backend",
            [ConfigKeys.KeycloakBackendClientSecret] = "secret",
            ["ConnectionStrings:Postgres"] = "Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=changeme",
            [ConfigKeys.MasterEncryptionKey] = TestConstants.MasterEncryptionKey,
            [ConfigKeys.SmtpHost] = "localhost",
            [ConfigKeys.SmtpPort] = "1025",
            [ConfigKeys.SmtpUseSsl] = "false",
            [ConfigKeys.SmtpFromEmail] = "noreply@example.com",
            [ConfigKeys.SmtpFromName] = "Orkyo",
            [ConfigKeys.CorsAllowedOrigins] = "http://localhost:5173",
            ["Logging:LogLevel:Default"] = "Debug",
            ["ORKYO_VERSION"] = "test-version"
        });

        var result = DeploymentConfig.FromConfiguration(config);

        result.AppBaseUrl.Should().Be("http://localhost:8080");
        result.OidcAuthority.Should().Be("http://localhost:8180/realms/orkyo");
        result.SmtpPort.Should().Be(1025);
        result.SmtpUseSsl.Should().BeFalse();
        result.LogLevel.Should().Be("Debug");
        result.Version.Should().Be("test-version");
        result.CorsAllowedOrigins.Should().Be("http://localhost:5173");
    }

    [Fact]
    public void FromConfiguration_PopulatesOidcInternalAuthority_WhenKeyIsPresent()
    {
        var config = BuildConfig(RequiredValues(new Dictionary<string, string?>
        {
            [ConfigKeys.OidcInternalAuthority] = "http://keycloak:8080/realms/orkyo",
        }));

        var result = DeploymentConfig.FromConfiguration(config);

        result.OidcInternalAuthority.Should().Be("http://keycloak:8080/realms/orkyo");
    }

    [Fact]
    public void FromConfiguration_LeavesOidcInternalAuthorityNull_WhenKeyIsAbsent()
    {
        var config = BuildConfig(RequiredValues());

        var result = DeploymentConfig.FromConfiguration(config);

        result.OidcInternalAuthority.Should().BeNull();
    }

    [Fact]
    public void OidcInternalAuthority_FallbackPattern_PrefersInternalOverPublic()
    {
        // Guard: the diagnostics probe uses null-coalescing, so this documents
        // the intended fallback behaviour independent of the HTTP call.
        var withInternal = new DeploymentConfig
        {
            PublicUrl = "http://app",
            AuthPublicUrl = "http://kc-public",
            AppBaseUrl = "http://app",
            CorsAllowedOrigins = "",
            SmtpHost = "localhost",
            SmtpPort = 25,
            SmtpUseSsl = false,
            SmtpFromEmail = "a@b.com",
            SmtpFromName = "T",
            OidcAuthority = "http://kc-public/realms/orkyo",
            OidcInternalAuthority = "http://keycloak:8080/realms/orkyo",
            KeycloakUrl = "http://kc-public",
            KeycloakRealm = "orkyo",
            KeycloakBackendClientId = "id",
            KeycloakBackendClientSecret = "s",
            PostgresConnectionString = "cs",
            MasterEncryptionKey = TestConstants.MasterEncryptionKey,
        };

        var withoutInternal = withInternal with { OidcInternalAuthority = null };

        (withInternal.OidcInternalAuthority ?? withInternal.OidcAuthority)
            .Should().Be("http://keycloak:8080/realms/orkyo");

        (withoutInternal.OidcInternalAuthority ?? withoutInternal.OidcAuthority)
            .Should().Be("http://kc-public/realms/orkyo");
    }

    [Fact]
    public void Redacted_MasksSecrets()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            [ConfigKeys.AppBaseUrl] = "http://localhost:8080",
            [ConfigKeys.OidcAuthority] = "http://localhost:8180/realms/orkyo",
            [ConfigKeys.KeycloakUrl] = "http://localhost:8180",
            [ConfigKeys.KeycloakRealm] = "orkyo",
            [ConfigKeys.KeycloakBackendClientId] = "orkyo-backend",
            [ConfigKeys.KeycloakBackendClientSecret] = "super-secret",
            ["ConnectionStrings:Postgres"] = "Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=changeme",
            [ConfigKeys.SmtpHost] = "localhost",
            [ConfigKeys.SmtpPort] = "1025",
            [ConfigKeys.SmtpUseSsl] = "false",
            [ConfigKeys.SmtpFromEmail] = "noreply@example.com",
            [ConfigKeys.SmtpFromName] = "Orkyo",
            [ConfigKeys.SmtpUsername] = "smtp-user",
            [ConfigKeys.SmtpPassword] = "smtp-password",
            [ConfigKeys.MasterEncryptionKey] = TestConstants.MasterEncryptionKey
        });

        var result = DeploymentConfig.FromConfiguration(config).Redacted();

        result[nameof(DeploymentConfig.KeycloakBackendClientSecret)].Should().Be("***");
        result[nameof(DeploymentConfig.PostgresConnectionString)].Should().Be("***");
        result[nameof(DeploymentConfig.SmtpPassword)].Should().Be("***");
        result[nameof(DeploymentConfig.SmtpUsername)].Should().Be("***");
        result[nameof(DeploymentConfig.MasterEncryptionKey)].Should().Be("***");
        result[nameof(DeploymentConfig.AppBaseUrl)].Should().Be("http://localhost:8080");
    }

    [Fact]
    public void DecodeMasterEncryptionKey_ReturnsThirtyTwoBytes_ForValidKey()
    {
        var config = DeploymentConfig.FromConfiguration(BuildConfig(RequiredValues()));
        config.DecodeMasterEncryptionKey().Should().HaveCount(32);
    }

    [Theory]
    [InlineData("not valid base64 !!!")]            // not base64
    [InlineData("AAAAAAAAAAAAAAAAAAAAAA==")]          // valid base64, but only 16 bytes
    public void DecodeMasterEncryptionKey_Throws_ForInvalidKey(string badKey)
    {
        var config = DeploymentConfig.FromConfiguration(
            BuildConfig(RequiredValues(new Dictionary<string, string?>
            {
                [ConfigKeys.MasterEncryptionKey] = badKey,
            })));
        var act = () => config.DecodeMasterEncryptionKey();
        act.Should().Throw<InvalidOperationException>();
    }

    private static IConfiguration BuildConfig(Dictionary<string, string?> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }

    /// <summary>Returns the minimum set of required keys, optionally merged with overrides.</summary>
    private static Dictionary<string, string?> RequiredValues(Dictionary<string, string?>? overrides = null)
    {
        var values = new Dictionary<string, string?>
        {
            [ConfigKeys.AppBaseUrl] = "http://localhost:8080",
            [ConfigKeys.OidcAuthority] = "http://localhost:8180/realms/orkyo",
            [ConfigKeys.KeycloakUrl] = "http://localhost:8180",
            [ConfigKeys.KeycloakRealm] = "orkyo",
            [ConfigKeys.KeycloakBackendClientId] = "orkyo-backend",
            [ConfigKeys.KeycloakBackendClientSecret] = "secret",
            ["ConnectionStrings:Postgres"] = "Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=changeme",
            [ConfigKeys.MasterEncryptionKey] = TestConstants.MasterEncryptionKey,
            [ConfigKeys.SmtpHost] = "localhost",
            [ConfigKeys.SmtpPort] = "1025",
            [ConfigKeys.SmtpUseSsl] = "false",
            [ConfigKeys.SmtpFromEmail] = "noreply@example.com",
            [ConfigKeys.SmtpFromName] = "Orkyo",
        };
        if (overrides is not null)
            foreach (var (k, v) in overrides)
                values[k] = v;
        return values;
    }
}
