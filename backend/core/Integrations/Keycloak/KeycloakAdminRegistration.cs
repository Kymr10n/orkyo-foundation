using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orkyo.Shared.Keycloak;

namespace Api.Integrations.Keycloak;

/// <summary>
/// Registers the Keycloak admin client: its options, the shared token cache, and the typed
/// HttpClient. One place for both hosts, so the API and worker graphs cannot drift — the worker
/// once lacked the cache and <see cref="KeycloakAdminService"/> failed to activate on every run.
/// </summary>
public static class KeycloakAdminRegistration
{
    public static IServiceCollection AddKeycloakAdmin(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(KeycloakOptions.FromConfiguration(configuration));
        // Singleton on purpose: the typed client makes the service transient, and the token
        // must outlive one instance. See the cache's own doc.
        services.AddSingleton<KeycloakAdminTokenCache>();
        services.AddHttpClient<IKeycloakAdminService, KeycloakAdminService>();
        return services;
    }
}
