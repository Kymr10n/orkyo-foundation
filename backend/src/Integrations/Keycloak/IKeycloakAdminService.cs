namespace Api.Integrations.Keycloak;

// Models are in KeycloakModels.cs

/// <summary>
/// Keycloak Admin API client. All methods throw <see cref="KeycloakAdminException"/>
/// on failure; the exception carries a caller-safe message and a suggested HTTP status.
/// </summary>
public interface IKeycloakAdminService
{
    /// <summary>
    /// Change password for a user identified by Keycloak subject ID.
    /// </summary>
    Task ChangePasswordAsync(string keycloakSub, string currentPassword, string newPassword);

    /// <summary>
    /// Get all active sessions for a user.
    /// </summary>
    Task<List<KeycloakSession>> GetUserSessionsAsync(string keycloakSub);

    /// <summary>
    /// Revoke a specific session.
    /// </summary>
    Task RevokeSessionAsync(string sessionId);

    /// <summary>
    /// Logout user from all sessions.
    /// </summary>
    Task LogoutAllSessionsAsync(string keycloakSub);

    /// <summary>
    /// Check whether the user is federated (external IdP) and return the provider if so.
    /// Returns <c>(false, null)</c> on any failure — this is a best-effort check used to
    /// decide whether to show the "change password" UI.
    /// </summary>
    Task<FederationStatus> GetUserFederationStatusAsync(string keycloakSub);

    /// <summary>
    /// Create a new user in Keycloak. If <paramref name="emailVerified"/> is false, sends verification email.
    /// </summary>
    Task CreateUserAsync(string email, string password, string? firstName = null, string? lastName = null, bool emailVerified = false);

    /// <summary>
    /// Check if a user with the given email already exists.
    /// </summary>
    Task<bool> UserExistsAsync(string email);

    /// <summary>
    /// Disable a user account (sets enabled=false). Used when moving to dormant state.
    /// </summary>
    Task DisableUserAsync(string keycloakId);

    /// <summary>
    /// Re-enable a user account (sets enabled=true). Used when user confirms activity.
    /// </summary>
    Task EnableUserAsync(string keycloakId);

    /// <summary>
    /// Permanently delete a user from Keycloak. Used during GDPR purge.
    /// </summary>
    Task DeleteUserAsync(string keycloakId);

    /// <summary>
    /// Get the MFA status for a user (TOTP configured, recovery codes, etc.).
    /// </summary>
    Task<MfaStatus> GetMfaStatusAsync(string keycloakSub);

    /// <summary>
    /// Delete a specific credential for a user (e.g., remove TOTP to allow re-enrollment).
    /// </summary>
    Task DeleteUserCredentialAsync(string keycloakSub, string credentialId);

    /// <summary>
    /// Get the user's profile (first name, last name, email).
    /// </summary>
    Task<UserProfile> GetUserProfileAsync(string keycloakSub);

    /// <summary>
    /// Update the user's name in Keycloak.
    /// </summary>
    Task UpdateUserProfileAsync(string keycloakSub, string firstName, string lastName);

    /// <summary>
    /// Add CONFIGURE_TOTP as a required action so the user is prompted to set up TOTP on next login.
    /// </summary>
    Task EnableMfaAsync(string keycloakSub);

    /// <summary>
    /// Check whether a user has a specific realm role.
    /// </summary>
    Task<bool> HasRealmRoleAsync(string keycloakId, string roleName);

    /// <summary>
    /// Assign a realm role to a user.
    /// </summary>
    Task AssignRealmRoleAsync(string keycloakId, string roleName);

    /// <summary>
    /// Revoke a realm role from a user.
    /// </summary>
    Task RevokeRealmRoleAsync(string keycloakId, string roleName);

    /// <summary>
    /// Count users who have a specific realm role.
    /// </summary>
    Task<int> CountRealmRoleMembersAsync(string roleName);
}

/// <summary>
/// Result of a federation status check — whether the user is federated and, if so, via which IdP.
/// </summary>
public record FederationStatus(bool IsFederated, string? IdentityProvider);
