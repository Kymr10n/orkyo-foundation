using Api.Endpoints;

namespace Api.Services;

public interface ISessionService
{
    /// <summary>
    /// Bootstrap a session after Keycloak login.
    /// Links Keycloak identity to internal user, creates user if first login.
    /// </summary>
    Task<SessionBootstrapResponse> BootstrapSessionAsync(string keycloakSub, string? email, string? displayName);

    /// <summary>
    /// Build a session response for a user by their internal ID.
    /// Used after identity linking to get the full session.
    /// </summary>
    Task<SessionBootstrapResponse?> BuildSessionResponseAsync(Guid userId);

    /// <summary>
    /// Get full session info by Keycloak subject ID (without creating user).
    /// </summary>
    Task<SessionBootstrapResponse?> GetSessionByKeycloakSubAsync(string keycloakSub);

    /// <summary>
    /// Get full session info by internal user ID.
    /// </summary>
    Task<SessionBootstrapResponse?> GetSessionByUserIdAsync(Guid userId);

    /// <summary>
    /// Accept a ToS version for the current user.
    /// </summary>
    Task AcceptTosAsync(Guid userId, string tosVersion, string? ipAddress, string? userAgent);

    /// <summary>
    /// Check if user has accepted the required ToS version.
    /// </summary>
    Task<bool> HasAcceptedTosAsync(Guid userId, string requiredVersion);

    /// <summary>
    /// Get the required ToS version from configuration.
    /// </summary>
    string? GetRequiredTosVersion();

    /// <summary>
    /// Mark the onboarding tour as seen for the current user.
    /// </summary>
    Task MarkTourSeenAsync(Guid userId);

    /// <summary>
    /// Update the display name for a user in the local database.
    /// Called after syncing profile changes to Keycloak.
    /// </summary>
    Task UpdateDisplayNameAsync(Guid userId, string displayName);
}
