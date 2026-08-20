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

    private static (IServiceCollection services, IConfiguration configuration) BuildServices()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [ConfigKeys.OidcAuthority] = "https://auth.example.com/realms/orkyo",
                [ConfigKeys.KeycloakBackendClientId] = "orkyo-backend",
                [ConfigKeys.KeycloakUrl] = "https://auth.example.com",
                [ConfigKeys.KeycloakRealm] = "orkyo",
                [ConfigKeys.KeycloakBackendClientSecret] = "test-secret",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();

        services.AddFoundationServices(config);

        return (services, config);
    }

    private static bool IsScoped<TService, TImpl>(ServiceDescriptor sd) =>
        sd.ServiceType == typeof(TService) &&
        sd.ImplementationType == typeof(TImpl) &&
        sd.Lifetime == ServiceLifetime.Scoped;

    private static bool IsSingleton<TService, TImpl>(ServiceDescriptor sd) =>
        sd.ServiceType == typeof(TService) &&
        sd.ImplementationType == typeof(TImpl) &&
        sd.Lifetime == ServiceLifetime.Singleton;

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
            })
            .Build();

        var returned = services.AddFoundationServices(config);

        returned.Should().BeSameAs(services);
    }

    // ── Security context ──────────────────────────────────────────────────────

    [Fact]
    public void AddFoundationServices_RegistersCurrentPrincipalAsScoped()
    {
        var (services, _) = BuildServices();
        services.Should().Contain(sd => IsScoped<CurrentPrincipal, CurrentPrincipal>(sd));
    }

    [Fact]
    public void AddFoundationServices_RegistersICurrentPrincipalMappedToCurrentPrincipal()
    {
        var (services, _) = BuildServices();
        services.Should().Contain(sd =>
            sd.ServiceType == typeof(ICurrentPrincipal) &&
            sd.Lifetime == ServiceLifetime.Scoped);
    }

    [Fact]
    public void AddFoundationServices_RegistersCurrentTenant()
    {
        var (services, _) = BuildServices();
        services.Should().Contain(sd => IsScoped<CurrentTenant, CurrentTenant>(sd));
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

    // ── Repositories ──────────────────────────────────────────────────────────

    [Fact]
    public void AddFoundationServices_RegistersResourceRepository()
    {
        var (services, _) = BuildServices();
        services.Should().Contain(sd => IsScoped<IResourceRepository, ResourceRepository>(sd));
    }


    [Fact]
    public void AddFoundationServices_RegistersSiteRepository()
    {
        var (services, _) = BuildServices();
        services.Should().Contain(sd => IsScoped<ISiteRepository, SiteRepository>(sd));
    }

    [Fact]
    public void AddFoundationServices_RegistersRequestRepository()
    {
        var (services, _) = BuildServices();
        services.Should().Contain(sd => IsScoped<IRequestRepository, RequestRepository>(sd));
    }


    [Fact]
    public void AddFoundationServices_RegistersSchedulingRepository()
    {
        var (services, _) = BuildServices();
        services.Should().Contain(sd => IsScoped<ISchedulingRepository, SchedulingRepository>(sd));
    }

    // ── Domain services ───────────────────────────────────────────────────────

    [Fact]
    public void AddFoundationServices_RegistersResourceService()
    {
        var (services, _) = BuildServices();
        services.Should().Contain(sd => IsScoped<IResourceService, ResourceService>(sd));
    }

    [Fact]
    public void AddFoundationServices_RegistersResourceTypeService()
    {
        var (services, _) = BuildServices();
        services.Should().Contain(sd => IsScoped<IResourceTypeService, ResourceTypeService>(sd));
    }


    [Fact]
    public void AddFoundationServices_RegistersSiteService()
    {
        var (services, _) = BuildServices();
        services.Should().Contain(sd => IsScoped<ISiteService, SiteService>(sd));
    }

    [Fact]
    public void AddFoundationServices_RegistersSchedulingService()
    {
        var (services, _) = BuildServices();
        services.Should().Contain(sd => IsScoped<ISchedulingService, SchedulingService>(sd));
    }

    [Fact]
    public void AddFoundationServices_RegistersSessionService()
    {
        var (services, _) = BuildServices();
        services.Should().Contain(sd => IsScoped<ISessionService, SessionService>(sd));
    }

    // ── Reporting ─────────────────────────────────────────────────────────────

    [Fact]
    public void AddFoundationServices_RegistersReportingTokenService()
    {
        var (services, _) = BuildServices();
        services.Should().Contain(sd => IsScoped<IReportingTokenService, ReportingTokenService>(sd));
    }

    [Fact]
    public void AddFoundationServices_RegistersReportingQueryService()
    {
        var (services, _) = BuildServices();
        services.Should().Contain(sd => IsScoped<IReportingQueryService, ReportingQueryService>(sd));
    }

    // ── Scheduling solver (singleton) ─────────────────────────────────────────

    [Fact]
    public void AddFoundationServices_RegistersAutoScheduleServiceAsScoped()
    {
        var (services, _) = BuildServices();
        services.Should().Contain(sd => IsScoped<IAutoScheduleService, AutoScheduleService>(sd));
    }

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
                [ConfigKeys.TurnstileSecretKey] = "0x-turnstile-secret",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        // The host registers IConfiguration in a real app; the provider is constructed from it.
        services.AddSingleton<IConfiguration>(config);
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
}
