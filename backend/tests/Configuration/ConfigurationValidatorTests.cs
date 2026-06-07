using Api.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Orkyo.Shared;

namespace Orkyo.Foundation.Tests.Configuration;

public class ConfigurationValidatorTests
{
    // ── Validate ─────────────────────────────────────────────────────────────

    [Fact]
    public void Validate_ReturnsNoErrors_WhenAllRequiredKeysPresent()
    {
        var config = BuildValidConfig();

        var errors = ConfigurationValidator.Validate(config);

        errors.Should().BeEmpty();
    }

    [Fact]
    public void Validate_ReturnsError_WhenRequiredKeyMissing()
    {
        var values = ValidConfigValues();
        values.Remove(ConfigKeys.SmtpHost);
        var config = BuildConfig(values);

        var errors = ConfigurationValidator.Validate(config);

        errors.Should().ContainSingle(e => e.Contains(ConfigKeys.SmtpHost));
    }

    [Fact]
    public void Validate_ReturnsMultipleErrors_WhenSeveralRequiredKeysMissing()
    {
        var values = ValidConfigValues();
        values.Remove(ConfigKeys.SmtpHost);
        values.Remove(ConfigKeys.KeycloakUrl);
        var config = BuildConfig(values);

        var errors = ConfigurationValidator.Validate(config);

        errors.Should().HaveCountGreaterThanOrEqualTo(2);
    }

    [Fact]
    public void Validate_ReturnsError_WhenAllowTenantHeaderTrueInProduction()
    {
        var values = ValidConfigValues();
        values[ConfigKeys.TenantResolutionAllowTenantHeader] = "true";
        var config = BuildConfig(values);

        var errors = ConfigurationValidator.Validate(config, EnvironmentNames.Production);

        errors.Should().ContainSingle(e => e.Contains("AllowTenantHeader"));
    }

    [Fact]
    public void Validate_ReturnsNoError_WhenAllowTenantHeaderTrueInDevelopment()
    {
        var values = ValidConfigValues();
        values[ConfigKeys.TenantResolutionAllowTenantHeader] = "true";
        var config = BuildConfig(values);

        var errors = ConfigurationValidator.Validate(config, EnvironmentNames.Development);

        errors.Should().NotContain(e => e.Contains("AllowTenantHeader"));
    }

    [Fact]
    public void Validate_UsesEnvironmentFromConfig_WhenNotPassedExplicitly()
    {
        var values = ValidConfigValues();
        values[ConfigKeys.TenantResolutionAllowTenantHeader] = "true";
        values[ConfigKeys.AspNetCoreEnvironment] = EnvironmentNames.Production;
        var config = BuildConfig(values);

        // environmentName parameter is null — should read from config
        var errors = ConfigurationValidator.Validate(config, environmentName: null);

        errors.Should().ContainSingle(e => e.Contains("AllowTenantHeader"));
    }

    // ── ValidateOrThrow ───────────────────────────────────────────────────────

    [Fact]
    public void ValidateOrThrow_DoesNotThrow_WhenConfigurationIsValid()
    {
        var config = BuildValidConfig();

        var act = () => ConfigurationValidator.ValidateOrThrow(config);

        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateOrThrow_Throws_WhenConfigurationIsInvalid()
    {
        var values = ValidConfigValues();
        values.Remove(ConfigKeys.SmtpHost);
        var config = BuildConfig(values);

        var act = () => ConfigurationValidator.ValidateOrThrow(config);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Configuration validation failed*");
    }

    [Fact]
    public void ValidateOrThrow_ExceptionMessage_ContainsAllErrors()
    {
        var values = ValidConfigValues();
        values.Remove(ConfigKeys.SmtpHost);
        values.Remove(ConfigKeys.KeycloakUrl);
        var config = BuildConfig(values);

        var act = () => ConfigurationValidator.ValidateOrThrow(config);

        var ex = act.Should().Throw<InvalidOperationException>().Which;
        ex.Message.Should().Contain(ConfigKeys.SmtpHost);
        ex.Message.Should().Contain(ConfigKeys.KeycloakUrl);
    }

    // ── LogConfigurationStatus ─────────────────────────────────────────────

    [Fact]
    public void LogConfigurationStatus_DoesNotThrow_WithValidConfig()
    {
        var config = BuildValidConfig();
        var logger = Mock.Of<ILogger>();

        var act = () => ConfigurationValidator.LogConfigurationStatus(config, logger);

        act.Should().NotThrow();
    }

    [Fact]
    public void LogConfigurationStatus_DoesNotThrow_WithMissingKeys()
    {
        var config = BuildConfig(new Dictionary<string, string?>());
        var logger = Mock.Of<ILogger>();

        var act = () => ConfigurationValidator.LogConfigurationStatus(config, logger);

        act.Should().NotThrow();
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static IConfiguration BuildValidConfig() => BuildConfig(ValidConfigValues());

    private static IConfiguration BuildConfig(Dictionary<string, string?> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }

    private static Dictionary<string, string?> ValidConfigValues() => new()
    {
        [ConfigKeys.OidcAuthority] = "http://localhost:8180/realms/orkyo",
        [ConfigKeys.KeycloakUrl] = "http://localhost:8180",
        [ConfigKeys.KeycloakRealm] = "orkyo",
        [ConfigKeys.KeycloakBackendClientId] = "orkyo-backend",
        [ConfigKeys.KeycloakBackendClientSecret] = "secret",
        ["ConnectionStrings:Postgres"] = "Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=changeme",
        [ConfigKeys.AppBaseUrl] = "http://localhost:8080",
        [ConfigKeys.SmtpHost] = "localhost",
        [ConfigKeys.SmtpPort] = "1025",
        [ConfigKeys.SmtpUseSsl] = "false",
        [ConfigKeys.SmtpFromEmail] = "noreply@example.com",
        [ConfigKeys.SmtpFromName] = "Orkyo",
        [ConfigKeys.MasterEncryptionKey] = TestConstants.MasterEncryptionKey,
    };
}
