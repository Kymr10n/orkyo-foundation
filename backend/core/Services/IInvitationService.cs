using Api.Models;

namespace Api.Services;

/// <summary>
/// Manages email-based user invitations. An invitation creates a single-use token
/// sent via email; accepting it creates a Keycloak account and tenant membership.
/// </summary>
public interface IInvitationService
{
    /// <summary>
    /// Invites <paramref name="email"/> to the tenant. A person with no account gets an invitation
    /// email (<see cref="InviteUserResult.Invited"/>); an existing account is made a member at once
    /// (<see cref="InviteUserResult.AddedDirectly"/>); a current member is left as is
    /// (<see cref="InviteUserResult.AlreadyMember"/>).
    /// </summary>
    Task<InviteUserResult> InviteAsync(
        TenantContext tenant, Guid invitedBy, string email, UserRole role, CancellationToken ct = default);

    /// <summary>
    /// <see cref="InviteAsync"/> flattened to the pre-2026-09 shape: <c>null</c> for both
    /// "added directly" and "already a member". Kept for orkyo-saas's integration tests.
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

    /// <summary>
    /// Re-sends a pending invitation with a freshly generated token and a reset expiry, invalidating
    /// the previous link. Returns <c>false</c> if no pending invitation with that id exists in the
    /// tenant. The stored token is only a hash, so the original can never be re-sent as-is.
    /// </summary>
    Task<bool> ResendInvitationAsync(TenantContext tenant, Guid invitationId, Guid resentBy, CancellationToken ct = default);
}

/// <summary>The outcome of <see cref="IInvitationService.InviteAsync"/>.</summary>
public abstract record InviteUserResult
{
    private InviteUserResult() { }

    /// <summary>No account yet: an invitation was recorded and mailed.</summary>
    public sealed record Invited(Invitation Invitation, string Token) : InviteUserResult;

    /// <summary>The account existed: it is now an active member with the requested role.</summary>
    public sealed record AddedDirectly(Guid UserId, string Email, UserRole Role) : InviteUserResult;

    /// <summary>The account is already a member of the tenant; nothing changed.</summary>
    public sealed record AlreadyMember : InviteUserResult;
}
