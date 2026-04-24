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
            [ConfigKeys.FileStoragePath] = ".local/storage",
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
            [ConfigKeys.FileStoragePath] = ".local/storage",
            [ConfigKeys.SmtpHost] = "localhost",
            [ConfigKeys.SmtpPort] = "1025",
            [ConfigKeys.SmtpUseSsl] = "false",
            [ConfigKeys.SmtpFromEmail] = "noreply@example.com",
            [ConfigKeys.SmtpFromName] = "Orkyo",
            [ConfigKeys.SmtpUsername] = "smtp-user",
            [ConfigKeys.SmtpPassword] = "smtp-password"
        });

        var result = DeploymentConfig.FromConfiguration(config).Redacted();

        result[nameof(DeploymentConfig.KeycloakBackendClientSecret)].Should().Be("***");
        result[nameof(DeploymentConfig.PostgresConnectionString)].Should().Be("***");
        result[nameof(DeploymentConfig.SmtpPassword)].Should().Be("***");
        result[nameof(DeploymentConfig.SmtpUsername)].Should().Be("***");
        result[nameof(DeploymentConfig.AppBaseUrl)].Should().Be("http://localhost:8080");
    }

    private static IConfiguration BuildConfig(Dictionary<string, string?> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }
}
