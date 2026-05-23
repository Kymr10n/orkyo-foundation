using Api.Models;

namespace Api.Services;

public interface ISessionService
{
    /// <summary>
    /// Bootstrap a session after Keycloak login.
    /// Links Keycloak identity to internal user, creates user if first login.
    /// </summary>
    Task<SessionBootstrapResponse> BootstrapSessionAsync(string keycloakSub, string? email, string? displayName, CancellationToken ct = default);

    /// <summary>
    /// Build a session response for a user by their internal ID.
    /// Used after identity linking to get the full session.
    /// </summary>
    Task<SessionBootstrapResponse?> BuildSessionResponseAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Get full session info by Keycloak subject ID (without creating user).
    /// </summary>
    Task<SessionBootstrapResponse?> GetSessionByKeycloakSubAsync(string keycloakSub, CancellationToken ct = default);

    /// <summary>
    /// Get full session info by internal user ID.
    /// </summary>
    Task<SessionBootstrapResponse?> GetSessionByUserIdAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Accept a ToS version for the current user.
    /// </summary>
    Task AcceptTosAsync(Guid userId, string tosVersion, string? ipAddress, string? userAgent, CancellationToken ct = default);

    /// <summary>
    /// Check if user has accepted the required ToS version.
    /// </summary>
    Task<bool> HasAcceptedTosAsync(Guid userId, string requiredVersion, CancellationToken ct = default);

    /// <summary>
    /// Get the required ToS version from configuration.
    /// </summary>
    string? GetRequiredTosVersion();

    /// <summary>
    /// Mark the onboarding tour as seen for the current user.
    /// </summary>
    Task MarkTourSeenAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Update the display name for a user in the local database.
    /// Called after syncing profile changes to Keycloak.
    /// </summary>
    Task UpdateDisplayNameAsync(Guid userId, string displayName, CancellationToken ct = default);
}
