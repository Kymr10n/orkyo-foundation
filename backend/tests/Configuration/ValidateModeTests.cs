using Api.Configuration;
using Microsoft.Extensions.Configuration;

namespace Orkyo.Foundation.Tests.Configuration;

public class ValidateModeTests
{
    private static IConfiguration BuildConfig(Dictionary<string, string?> values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

    private static Dictionary<string, string?> ValidConfig() => new()
    {
        ["APP_BASE_URL"] = "https://orkyo.example.com",
        ["CORS_ALLOWED_ORIGINS"] = "https://orkyo.example.com",
        ["SMTP_HOST"] = "smtp.example.com",
        ["SMTP_PORT"] = "587",
        ["SMTP_USE_SSL"] = "true",
        ["SMTP_FROM_EMAIL"] = "noreply@example.com",
        ["SMTP_FROM_NAME"] = "Orkyo",
        ["FILE_STORAGE_PATH"] = "/app/storage",
        ["OIDC_AUTHORITY"] = "https://auth.example.com/realms/orkyo",
        ["KEYCLOAK_URL"] = "https://auth.example.com",
        ["KEYCLOAK_REALM"] = "orkyo",
        ["KEYCLOAK_BACKEND_CLIENT_ID"] = "orkyo-backend",
        ["KEYCLOAK_BACKEND_CLIENT_SECRET"] = "test-secret",
        ["ConnectionStrings:Postgres"] = "Host=localhost;Database=test;Username=test;Password=test",
    };

    [Fact]
    public void Run_AllKeysPresent_ReturnsZero()
    {
        var config = BuildConfig(ValidConfig());

        var exitCode = ValidateMode.Run(config, "Test");

        exitCode.Should().Be(0);
    }

    [Fact]
    public void Run_MissingKey_ReturnsOne()
    {
        var values = ValidConfig();
        values.Remove("SMTP_HOST");
        var config = BuildConfig(values);

        var exitCode = ValidateMode.Run(config, "Test");

        exitCode.Should().Be(1);
    }

    [Fact]
    public void Run_InvalidParse_ReturnsOne()
    {
        var values = ValidConfig();
        values["SMTP_PORT"] = "not-a-number";
        var config = BuildConfig(values);

        var exitCode = ValidateMode.Run(config, "Test");

        exitCode.Should().Be(1);
    }

    [Fact]
    public void Run_AllowTenantHeaderInProduction_ReturnsOne()
    {
        var values = ValidConfig();
        values["TenantResolution:AllowTenantHeader"] = "true";
        var config = BuildConfig(values);

        var exitCode = ValidateMode.Run(config, "Production");

        exitCode.Should().Be(1);
    }
}
