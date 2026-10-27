using Api.Configuration;
using Api.Integrations.Keycloak;
using Api.Repositories;
using Api.Security;
using Api.Security.Challenge;
using Api.Services;
using Api.Services.AutoSchedule;
using Api.Services.Reporting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orkyo.Shared;
using Orkyo.Shared.Keycloak;

namespace Orkyo.Foundation.Tests.Configuration;

/// <summary>
/// Verifies that <see cref="FoundationServiceExtensions.AddFoundationServices"/> registers
/// every service, repository, and infrastructure component that foundation-owned code
/// depends on.  Tests inspect the <see cref="ServiceCollection"/> descriptors directly
/// so they remain fast and require no external infrastructure.
/// </summary>
public class FoundationServiceExtensionsTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static (IServiceCollection services, IConfiguration configuration) BuildServices(
        Dictionary<string, string?>? extra = null)
    {
        var values = new Dictionary<string, string?>
        {
            [ConfigKeys.OidcAuthority] = "https://auth.example.com/realms/orkyo",
            [ConfigKeys.KeycloakBackendClientId] = "orkyo-backend",
            [ConfigKeys.KeycloakUrl] = "https://auth.example.com",
            [ConfigKeys.KeycloakRealm] = "orkyo",
            [ConfigKeys.KeycloakBackendClientSecret] = "test-secret",
            [ConfigKeys.KeycloakPasswordCheckClientId] = TestConstants.CheckClientId,
            [ConfigKeys.KeycloakPasswordCheckClientSecret] = TestConstants.CheckClientCredential,
        };
        if (extra != null)
            foreach (var (k, v) in extra)
                values[k] = v;

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();

        services.AddFoundationServices(config);

        return (services, config);
    }

    // ── Return value ──────────────────────────────────────────────────────────

    [Fact]
    public void AddFoundationServices_ReturnsTheSameServiceCollection()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [ConfigKeys.OidcAuthority] = "https://auth.example.com/realms/orkyo",
                [ConfigKeys.KeycloakBackendClientId] = "orkyo-backend",
                [ConfigKeys.KeycloakUrl] = "https://auth.example.com",
                [ConfigKeys.KeycloakRealm] = "orkyo",
                [ConfigKeys.KeycloakBackendClientSecret] = "secret",
                [ConfigKeys.KeycloakPasswordCheckClientId] = TestConstants.CheckClientId,
                [ConfigKeys.KeycloakPasswordCheckClientSecret] = TestConstants.CheckClientCredential,
            })
            .Build();

        var returned = services.AddFoundationServices(config);

        returned.Should().BeSameAs(services);
    }

    // ── Scoped registrations ──────────────────────────────────────────────────

    [Theory]
    [InlineData(typeof(CurrentPrincipal), typeof(CurrentPrincipal))]
    [InlineData(typeof(CurrentTenant), typeof(CurrentTenant))]
    [InlineData(typeof(IResourceRepository), typeof(ResourceRepository))]
    [InlineData(typeof(ISiteRepository), typeof(SiteRepository))]
    [InlineData(typeof(IRequestRepository), typeof(RequestRepository))]
    [InlineData(typeof(IRequestTreeRepository), typeof(RequestTreeRepository))]
    [InlineData(typeof(IRequestScheduleReadRepository), typeof(RequestScheduleReadRepository))]
    [InlineData(typeof(ISchedulingRepository), typeof(SchedulingRepository))]
    [InlineData(typeof(IResourceService), typeof(ResourceService))]
    [InlineData(typeof(IResourceTypeService), typeof(ResourceTypeService))]
    [InlineData(typeof(ISiteService), typeof(SiteService))]
    [InlineData(typeof(ISchedulingService), typeof(SchedulingService))]
    [InlineData(typeof(ISessionService), typeof(SessionService))]
    [InlineData(typeof(IReportingTokenService), typeof(ReportingTokenService))]
    [InlineData(typeof(IReportingQueryService), typeof(ReportingQueryService))]
    [InlineData(typeof(IAutoScheduleService), typeof(AutoScheduleService))]
    public void AddFoundationServices_RegistersTheScopedService(Type service, Type implementation)
    {
        var (services, _) = BuildServices();
        services.Should().Contain(sd =>
            sd.ServiceType == service &&
            sd.ImplementationType == implementation &&
            sd.Lifetime == ServiceLifetime.Scoped);
    }

    [Fact]
    public void AddFoundationServices_RegistersICurrentPrincipalAsScoped()
    {
        var (services, _) = BuildServices();
        services.Should().Contain(sd =>
            sd.ServiceType == typeof(ICurrentPrincipal) &&
            sd.Lifetime == ServiceLifetime.Scoped);
    }

    // ── Keycloak ──────────────────────────────────────────────────────────────

    [Fact]
    public void AddFoundationServices_RegistersKeycloakAdminService()
    {
        var (services, _) = BuildServices();
        // AddHttpClient registers via factory rather than ImplementationType
        services.Should().Contain(sd => sd.ServiceType == typeof(IKeycloakAdminService));
    }

    [Fact]
    public void AddFoundationServices_RegistersKeycloakOptionsAsSingleton()
    {
        var (services, _) = BuildServices();
        services.Should().Contain(sd =>
            sd.ServiceType == typeof(KeycloakOptions) &&
            sd.Lifetime == ServiceLifetime.Singleton);
    }

    [Fact]
    public void AddFoundationServices_KeycloakOptionsBuiltFromConfiguration()
    {
        var (services, _) = BuildServices();
        var provider = services.BuildServiceProvider();
        var opts = provider.GetRequiredService<KeycloakOptions>();

        opts.BaseUrl.Should().Be("https://auth.example.com");
        opts.Realm.Should().Be("orkyo");
        opts.BackendClientId.Should().Be("orkyo-backend");
    }

    // ── Scheduling solver (singleton) ─────────────────────────────────────────

    [Fact]
    public void AddFoundationServices_RegistersSchedulingFeasibilityAnalyzerAsSingleton()
    {
        var (services, _) = BuildServices();
        services.Should().Contain(sd =>
            sd.ServiceType == typeof(SchedulingFeasibilityAnalyzer) &&
            sd.Lifetime == ServiceLifetime.Singleton);
    }

    [Fact]
    public void AddFoundationServices_RegistersTwoISchedulingSolverSingletons()
    {
        var (services, _) = BuildServices();
        var solvers = services
            .Where(sd =>
                sd.ServiceType == typeof(ISchedulingSolver) &&
                sd.Lifetime == ServiceLifetime.Singleton)
            .ToList();

        solvers.Should().HaveCount(2);
        solvers.Should().Contain(sd => sd.ImplementationType == typeof(OrToolsSchedulingSolver));
        solvers.Should().Contain(sd => sd.ImplementationType == typeof(GreedySchedulingSolver));
    }

    // ── Bot-challenge provider: key-gated ─────────────────────────────────────

    [Fact]
    public void AddFoundationServices_WithNoTurnstileSecret_RegistersTheFailOpenProvider()
    {
        // Dev, Community, and any unconfigured environment must get NoOp. This is also a
        // construction guard: CloudflareTurnstileProvider reads the key with GetRequired
        // and throws without one, so registering it here would fail at first resolution.
        var (services, _) = BuildServices();

        services.Should().ContainSingle(sd =>
            sd.ServiceType == typeof(IChallengeProvider) &&
            sd.ImplementationType == typeof(NoOpChallengeProvider));
    }

    [Fact]
    public void AddFoundationServices_WithATurnstileSecret_RegistersTheTurnstileProvider()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [ConfigKeys.OidcAuthority] = "https://auth.example.com/realms/orkyo",
                [ConfigKeys.KeycloakBackendClientId] = "orkyo-backend",
                [ConfigKeys.KeycloakUrl] = "https://auth.example.com",
                [ConfigKeys.KeycloakRealm] = "orkyo",
                [ConfigKeys.KeycloakBackendClientSecret] = "test-secret",
                [ConfigKeys.KeycloakPasswordCheckClientId] = TestConstants.CheckClientId,
                [ConfigKeys.KeycloakPasswordCheckClientSecret] = TestConstants.CheckClientCredential,
                [ConfigKeys.TurnstileSecretKey] = "0x-turnstile-secret",
                [ConfigKeys.AppBaseUrl] = "https://app.example.com",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        // The host registers IConfiguration and IHostEnvironment in a real app; the provider
        // is constructed from both.
        services.AddSingleton<IConfiguration>(config);
        services.AddSingleton(Mock.Of<IHostEnvironment>(e => e.EnvironmentName == EnvironmentNames.Production));
        services.AddFoundationServices(config);

        // AddHttpClient registers via factory rather than ImplementationType.
        services.Should().ContainSingle(sd => sd.ServiceType == typeof(IChallengeProvider));
        services.Should().NotContain(sd =>
            sd.ServiceType == typeof(IChallengeProvider) &&
            sd.ImplementationType == typeof(NoOpChallengeProvider));

        // It must actually resolve: the provider throws in its constructor without a key,
        // so a mis-ordered registration would only surface here.
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IChallengeProvider>()
            .Should().BeOfType<CloudflareTurnstileProvider>();
    }

    [Theory]
    [InlineData(null, EnvironmentNames.Production, true)]
    [InlineData("", EnvironmentNames.Production, true)]
    [InlineData(null, null, true)]
    [InlineData(null, EnvironmentNames.Development, false)]
    [InlineData("0x-turnstile-secret", EnvironmentNames.Production, false)]
    public void IsChallengeVerificationDisabledOutsideDevelopment_DecidesTheStartupWarning(
        string? secretKey, string? environment, bool expected)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [ConfigKeys.TurnstileSecretKey] = secretKey,
                [ConfigKeys.AspNetCoreEnvironment] = environment,
            })
            .Build();

        FoundationServiceExtensions.IsChallengeVerificationDisabledOutsideDevelopment(config)
            .Should().Be(expected);
    }

    // ── Email transport: key-gated like the challenge provider ────────────────

    [Fact]
    public void AddFoundationServices_WithNoSmtpHost_RegistersTheLogOnlyTransport()
    {
        // The self-host default. SmtpEmailTransport reads its keys with GetRequired, so
        // registering it without a host would fail at the first send.
        var (services, _) = BuildServices();

        services.Should().ContainSingle(sd =>
            sd.ServiceType == typeof(IEmailTransport) &&
            sd.ImplementationType == typeof(LogOnlyEmailTransport));
    }

    [Fact]
    public void AddFoundationServices_WithAnSmtpHost_RegistersTheSmtpTransport()
    {
        var (services, _) = BuildServices(new() { [ConfigKeys.SmtpHost] = "smtp.example.com" });

        services.Should().ContainSingle(sd =>
            sd.ServiceType == typeof(IEmailTransport) &&
            sd.ImplementationType == typeof(SmtpEmailTransport));
    }
}
