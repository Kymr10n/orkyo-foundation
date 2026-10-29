namespace Api.Models;

/// <summary>
/// One of the caller's workspace memberships, as <c>GET /api/tenants/memberships</c> returns it.
/// The frontend mirror is <c>AccountMembership</c> (frontend/src/lib/api/tenant-account-api.ts).
/// </summary>
public sealed record TenantMembershipResponse(
    Guid TenantId,
    string TenantSlug,
    string TenantDisplayName,
    string TenantStatus,
    string Role,
    string Status,
    bool IsOwner,
    DateTime? JoinedAt);
