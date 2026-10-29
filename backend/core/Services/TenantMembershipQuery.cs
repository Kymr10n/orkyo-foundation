using Api.Models;
using Api.Repositories;
using Api.Security;

namespace Api.Services;

/// <summary>
/// The caller's workspace memberships. Products supply it: SaaS registers
/// <see cref="ControlPlaneTenantMembershipQuery"/>; Community answers with its one configured
/// tenant, which has no control-plane membership rows.
/// </summary>
public interface ITenantMembershipQuery
{
    Task<IReadOnlyList<TenantMembershipResponse>> GetCurrentUserMembershipsAsync(CancellationToken ct = default);
}

/// <summary>Memberships from the control plane's <c>tenant_memberships</c>, newest first, any status.</summary>
public sealed class ControlPlaneTenantMembershipQuery(ICurrentPrincipal principal, ITenantControlPlaneRepository controlPlane)
    : ITenantMembershipQuery
{
    public async Task<IReadOnlyList<TenantMembershipResponse>> GetCurrentUserMembershipsAsync(CancellationToken ct = default)
    {
        var userId = principal.RequireUserId();
        var rows = await controlPlane.GetUserMembershipsAsync(userId, ct);
        return rows.Select(row => new TenantMembershipResponse(
            row.TenantId,
            row.TenantSlug,
            row.TenantDisplayName,
            row.TenantStatus,
            row.Role,
            row.MembershipStatus,
            IsOwner: row.OwnerUserId == userId,
            JoinedAt: row.JoinedAt)).ToList();
    }
}
