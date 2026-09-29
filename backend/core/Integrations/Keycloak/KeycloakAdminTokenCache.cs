namespace Api.Integrations.Keycloak;

/// <summary>
/// The Keycloak admin (client-credentials) token, shared by every
/// <see cref="KeycloakAdminService"/>. Registered as a singleton: the typed HttpClient makes the
/// service transient, so a cache held per instance was empty on every request and each admin
/// call fetched a new token.
/// </summary>
public sealed class KeycloakAdminTokenCache
{
    internal SemaphoreSlim Lock { get; } = new(1, 1);
    internal string? AccessToken { get; set; }
    internal DateTime Expiry { get; set; } = DateTime.MinValue;
}
