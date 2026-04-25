using Api.Models;

namespace Api.Services;

public interface IInvitationService
{
    Task<(Invitation invitation, string token)?> InviteUserAsync(
        TenantContext tenant, Guid invitedBy, string email, UserRole role);
    Task<(User? user, string? error)> AcceptInvitationAsync(
        string token, string displayName, string password);
    Task<(string? email, DateTime? expiresAt, string? tenantName, string? error)> ValidateInvitationAsync(string token);
    Task<List<Invitation>> GetPendingInvitationsAsync(TenantContext tenant);
    Task<bool> RevokeInvitationAsync(TenantContext tenant, Guid invitationId, Guid revokedBy);
}
