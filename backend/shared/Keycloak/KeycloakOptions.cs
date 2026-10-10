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
    /// Falls back to <see cref="BaseUrl"/> when unset or empty (local dev).
    /// </summary>
    public string? InternalBaseUrl { get; init; }

    /// <summary>
    /// Effective base URL for server-to-server calls. Empty counts as unset: an empty internal
    /// URL would otherwise turn every token and admin URL into a relative one.
    /// </summary>
    public string EffectiveInternalBaseUrl => string.IsNullOrEmpty(InternalBaseUrl) ? BaseUrl : InternalBaseUrl;

    /// <summary>Internal OIDC authority (for backchannel token requests).</summary>
    public string InternalAuthority => $"{EffectiveInternalBaseUrl}/realms/{Realm}";

    // ── Realm & clients ──────────────────────────────────────────────────────

    /// <summary>Keycloak realm name.</summary>
    public required string Realm { get; init; }

    // ── Backend service-account credentials (server-side only) ──────────────

    /// <summary>
    /// Confidential client ID for server-to-server operations (BFF OIDC code flow,
    /// client_credentials for the Admin API). Password grants are off on this client.
    /// </summary>
    public required string BackendClientId { get; init; }

    /// <summary>Client secret for <see cref="BackendClientId"/>.</summary>
    public required string BackendClientSecret { get; init; }

    /// <summary>
    /// Confidential client used only to re-check a user's password with a password grant
    /// (change password, removing MFA or a passkey). Its tokens carry no API audience and are
    /// thrown away; the grant stays off on <see cref="BackendClientId"/>.
    /// </summary>
    public required string PasswordCheckClientId { get; init; }

    /// <summary>Client secret for <see cref="PasswordCheckClientId"/>.</summary>
    public required string PasswordCheckClientSecret { get; init; }

    /// <summary>
    /// Build a <see cref="KeycloakOptions"/> from flat environment variables.
    /// </summary>
    public static KeycloakOptions FromConfiguration(IConfiguration configuration)
    {
        // Empty counts as absent, as in GetRequired/IsSet (which this assembly cannot reference):
        // the deploy pipeline writes `KEY=` for every unset key, and `?? throw` let "" through.
        string Require(string key) =>
            configuration[key] is { Length: > 0 } value
                ? value
                : throw new InvalidOperationException($"{key} is not configured");

        string? Optional(string key) =>
            configuration[key] is { Length: > 0 } value ? value : null;

        return new KeycloakOptions
        {
            BaseUrl = Require(ConfigKeys.KeycloakUrl),
            InternalBaseUrl = Optional(ConfigKeys.KeycloakInternalUrl),
            Realm = Require(ConfigKeys.KeycloakRealm),
            BackendClientId = Require(ConfigKeys.KeycloakBackendClientId),
            BackendClientSecret = Require(ConfigKeys.KeycloakBackendClientSecret),
            PasswordCheckClientId = Require(ConfigKeys.KeycloakPasswordCheckClientId),
            PasswordCheckClientSecret = Require(ConfigKeys.KeycloakPasswordCheckClientSecret),
        };
    }
}
