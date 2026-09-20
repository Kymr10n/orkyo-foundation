using Api.Integrations.Keycloak;
using Api.Repositories;
using Api.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orkyo.Shared.Keycloak;

namespace Api.Configuration;

/// <summary>
/// Registers the worker services every edition's background host shares. Each product's
/// worker composes its own graph (explicit-registration rule) — it registers its
/// edition-specific <see cref="IDbConnectionFactory"/>, lifecycle service, and hosted
/// <c>WorkerService</c>, then calls this for the shared eight. Mirrors how the API composes
/// the same services via <c>AddFoundationServices</c>.
/// </summary>
public static class FoundationWorkerServiceExtensions
{
    public static IServiceCollection AddFoundationWorkerServices(
        this IServiceCollection services, IConfiguration configuration)
    {
        // The clock every foundation service reads. TryAdd so a product (or a test host) can
        // register a FakeTimeProvider instead. Reading the clock directly is ratcheted out of core/src.
        services.TryAddSingleton(TimeProvider.System);
        services.AddHttpClient();
        // Same shared cache the API registers, so a core service that takes it resolves in the
        // worker host too. AddMemoryCache is TryAdd: a product's own IMemoryCache wins.
        services.AddMemoryCache();
        services.TryAddSingleton<Api.Services.Caching.SingleFlightCache>();
        services.AddSingleton(KeycloakOptions.FromConfiguration(configuration));
        // UserLifecycleService resolves IKeycloakAdminService per run-cycle for disable/purge.
        services.AddHttpClient<IKeycloakAdminService, KeycloakAdminService>();
        // Org connections resolve through whichever IDbConnectionFactory the product registered.
        services.AddSingleton<IOrgDbConnectionFactory>(sp => sp.GetRequiredService<IDbConnectionFactory>());
        // Cross-instance, restart-safe scheduling guard for the worker loops (journal +
        // per-job advisory lock). Both editions consume it so the semantics stay identical.
        services.AddSingleton<IWorkerJobCoordinator, WorkerJobCoordinator>();
        // The worker runs outside any tenant context, so branding falls back to defaults.
        services.AddSingleton<ITenantSettingsService, WorkerTenantSettingsService>();
        services.AddSingleton<IEmailService, EmailService>();
        services.AddSingleton<IAnnouncementRepository, AnnouncementRepository>();
        services.AddSingleton<IAnnouncementBroadcastService, AnnouncementBroadcastService>();
        services.AddSingleton<UserLifecycleService>();
        return services;
    }

    /// <summary>
    /// Registers the shared <see cref="FoundationWorkerLoop"/> with the product's job list.
    /// The product's hosted service resolves the loop and awaits <see cref="FoundationWorkerLoop.RunAsync"/>;
    /// the jobs are built from the provider so they can take the product's own services.
    /// </summary>
    public static IServiceCollection AddFoundationWorkerLoop(
        this IServiceCollection services,
        Func<IServiceProvider, IEnumerable<WorkerJob>> jobs)
    {
        ArgumentNullException.ThrowIfNull(jobs);
        services.AddSingleton(sp => new FoundationWorkerLoop(
            sp.GetRequiredService<IWorkerJobCoordinator>(),
            jobs(sp),
            sp.GetRequiredService<ILogger<FoundationWorkerLoop>>(),
            sp.GetRequiredService<TimeProvider>()));
        return services;
    }
}
