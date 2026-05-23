using Api.Models;

namespace Api.Services;

/// <summary>
/// Manages email-based user invitations. An invitation creates a single-use token
/// sent via email; accepting it creates a Keycloak account and tenant membership.
/// </summary>
public interface IInvitationService
{
    /// <summary>
    /// Sends an invitation email to <paramref name="email"/> and records it.
    /// Returns <c>null</c> if the email already has an active account.
    /// </summary>
    Task<(Invitation invitation, string token)?> InviteUserAsync(
        TenantContext tenant, Guid invitedBy, string email, UserRole role, CancellationToken ct = default);

    /// <summary>
    /// Accepts an invitation, creating a Keycloak account and tenant membership.
    /// Returns the created <see cref="User"/> on success, or an error string on failure.
    /// </summary>
    Task<(User? user, string? error)> AcceptInvitationAsync(
        string token, string displayName, string password, CancellationToken ct = default);

    /// <summary>
    /// Validates an invitation token without consuming it.
    /// Returns the recipient email, expiry, and tenant name if valid, or an error string if invalid.
    /// </summary>
    Task<(string? email, DateTime? expiresAt, string? tenantName, string? error)> ValidateInvitationAsync(string token, CancellationToken ct = default);

    /// <summary>Returns all pending (not accepted, not revoked) invitations for the tenant.</summary>
    Task<List<Invitation>> GetPendingInvitationsAsync(TenantContext tenant, CancellationToken ct = default);

    /// <summary>Revokes an invitation so it can no longer be accepted. Returns <c>false</c> if not found.</summary>
    Task<bool> RevokeInvitationAsync(TenantContext tenant, Guid invitationId, Guid revokedBy, CancellationToken ct = default);
}
