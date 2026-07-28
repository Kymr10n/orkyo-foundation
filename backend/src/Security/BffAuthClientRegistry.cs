using Orkyo.Shared.Keycloak;

namespace Api.Security;

/// <summary>
/// Resolves token-endpoint credentials for the OAuth client a BFF session was
/// established with (<see cref="Api.Services.BffSession.BffSessionRecord.AuthClient"/>).
/// The refresh_token grant must present the issuing client's credentials, so a
/// session established through a secondary client (e.g. the SaaS demo client)
/// must also refresh through it. Editions that add such clients replace this
/// registration; the default knows only the primary backend client.
/// </summary>
public interface IBffAuthClientRegistry
{
    /// <summary>Credentials for <paramref name="authClient"/>; null or unknown resolves to the primary backend client.</summary>
    (string ClientId, string ClientSecret) Resolve(string? authClient);
}

public sealed class DefaultBffAuthClientRegistry : IBffAuthClientRegistry
{
    private readonly KeycloakOptions _keycloakOptions;

    public DefaultBffAuthClientRegistry(KeycloakOptions keycloakOptions) => _keycloakOptions = keycloakOptions;

    public (string ClientId, string ClientSecret) Resolve(string? authClient) =>
        (_keycloakOptions.BackendClientId, _keycloakOptions.BackendClientSecret);
}
