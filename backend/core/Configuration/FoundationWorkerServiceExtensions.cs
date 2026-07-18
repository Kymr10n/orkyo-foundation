using Api.Integrations.Keycloak;
using Api.Repositories;
using Api.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
        services.AddHttpClient();
        services.AddSingleton(KeycloakOptions.FromConfiguration(configuration));
        // UserLifecycleService resolves IKeycloakAdminService per run-cycle for disable/purge.
        services.AddHttpClient<IKeycloakAdminService, KeycloakAdminService>();
        // Org connections resolve through whichever IDbConnectionFactory the product registered.
        services.AddSingleton<IOrgDbConnectionFactory>(sp => sp.GetRequiredService<IDbConnectionFactory>());
        // The worker runs outside any tenant context, so branding falls back to defaults.
        services.AddSingleton<ITenantSettingsService, WorkerTenantSettingsService>();
        services.AddSingleton<IEmailService, EmailService>();
        services.AddSingleton<IAnnouncementRepository, AnnouncementRepository>();
        services.AddSingleton<IAnnouncementBroadcastService, AnnouncementBroadcastService>();
        services.AddSingleton<UserLifecycleService>();
        return services;
    }
}
