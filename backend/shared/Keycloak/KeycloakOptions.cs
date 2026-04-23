using Microsoft.Extensions.Configuration;

namespace Orkyo.Shared.Keycloak;

/// <summary>
/// Single source of truth for every Keycloak coordinate used by the backend
/// and worker.  Built from flat environment variables so it works unchanged
/// with Docker Compose <c>.env</c> files, CI secrets, and local dev.
/// </summary>
public sealed class KeycloakOptions
{
    // ── Public URL (used for OIDC discovery, JWT validation, issuer checks) ──

    /// <summary>Keycloak base URL (e.g. https://auth.orkyo.com).</summary>
    public required string BaseUrl { get; init; }

    /// <summary>OIDC authority (e.g. https://auth.orkyo.com/realms/orkyo).</summary>
    public string Authority => $"{BaseUrl}/realms/{Realm}";

    // ── Internal URL (container-to-container, bypasses nginx IP allowlist) ───

    /// <summary>
    /// Optional internal base URL (e.g. http://keycloak:8080).
    /// Falls back to <see cref="BaseUrl"/> when unset (local dev).
    /// </summary>
    public string? InternalBaseUrl { get; init; }

    /// <summary>Effective base URL for server-to-server calls.</summary>
    public string EffectiveInternalBaseUrl => InternalBaseUrl ?? BaseUrl;

    /// <summary>Internal OIDC authority (for backchannel token requests).</summary>
    public string InternalAuthority => $"{EffectiveInternalBaseUrl}/realms/{Realm}";

    // ── Realm & clients ──────────────────────────────────────────────────────

    /// <summary>Keycloak realm name.</summary>
    public required string Realm { get; init; }

    // ── Backend service-account credentials (server-side only) ──────────────

    /// <summary>
    /// Confidential client ID for server-to-server operations
    /// (BFF OIDC, client_credentials for Admin API, ROPC for password verification).
    /// </summary>
    public required string BackendClientId { get; init; }

    /// <summary>Client secret for <see cref="BackendClientId"/>.</summary>
    public required string BackendClientSecret { get; init; }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>Whether Keycloak is configured at all.</summary>
    public bool IsEnabled => !string.IsNullOrEmpty(BaseUrl);

    /// <summary>
    /// Build a <see cref="KeycloakOptions"/> from flat environment variables.
    /// </summary>
    public static KeycloakOptions FromConfiguration(IConfiguration configuration)
    {
        string Require(string key) =>
            configuration[key]
            ?? throw new InvalidOperationException($"{key} is not configured");

        return new KeycloakOptions
        {
            BaseUrl = Require(ConfigKeys.KeycloakUrl),
            InternalBaseUrl = configuration[ConfigKeys.KeycloakInternalUrl],
            Realm = Require(ConfigKeys.KeycloakRealm),
            BackendClientId = Require(ConfigKeys.KeycloakBackendClientId),
            BackendClientSecret = Require(ConfigKeys.KeycloakBackendClientSecret),
        };
    }
}
