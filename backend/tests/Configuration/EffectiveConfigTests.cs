using Api.Configuration;
using Api.Services;

namespace Orkyo.Foundation.Tests.Configuration;

public class EffectiveConfigTests
{
    [Fact]
    public void Deployment_ReturnsInjectedConfig()
    {
        var deployment = CreateDeploymentConfig();
        var mockSiteSettings = new Mock<ISiteSettingsService>();

        var effective = new EffectiveConfig(deployment, mockSiteSettings.Object);

        effective.Deployment.Should().BeSameAs(deployment);
    }

    [Fact]
    public async Task GetRuntimeAsync_DelegatesTo_SiteSettingsService()
    {
        var deployment = CreateDeploymentConfig();
        var expected = new RuntimeConfig { DefaultTimezone = "Europe/Berlin" };
        var mockSiteSettings = new Mock<ISiteSettingsService>();
        mockSiteSettings.Setup(s => s.GetRuntimeConfigAsync())
            .ReturnsAsync(expected);

        var effective = new EffectiveConfig(deployment, mockSiteSettings.Object);
        var runtime = await effective.GetRuntimeAsync();

        runtime.Should().BeSameAs(expected);
        mockSiteSettings.Verify(s => s.GetRuntimeConfigAsync(), Times.Once);
    }

    private static DeploymentConfig CreateDeploymentConfig() => new()
    {
        PublicUrl = "https://orkyo.example.com",
        AuthPublicUrl = "https://auth.example.com/realms/orkyo",
        AppBaseUrl = "https://orkyo.example.com",
        CorsAllowedOrigins = "https://orkyo.example.com",
        SmtpHost = "smtp.example.com",
        SmtpPort = 587,
        SmtpUseSsl = true,
        SmtpFromEmail = "noreply@example.com",
        SmtpFromName = "Orkyo",
        FileStoragePath = "/app/storage",
        OidcAuthority = "https://auth.example.com/realms/orkyo",
        KeycloakUrl = "https://auth.example.com",
        KeycloakRealm = "orkyo",
        KeycloakBackendClientId = "orkyo-backend",
        KeycloakBackendClientSecret = "test-secret",
        PostgresConnectionString = "Host=localhost;Database=test;Username=test;Password=test",
    };
}
