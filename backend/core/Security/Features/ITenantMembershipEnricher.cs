using Api.Models;

namespace Api.Security.Features;

/// <summary>
/// Edition hook to decorate session-bootstrap memberships with data foundation
/// cannot know. SaaS populates <c>CanReactivate</c>/<c>SuspensionReason</c> from its
/// control-plane suspension columns; foundation/Community have no suspension
/// metadata and pass the list through unchanged.
/// </summary>
public interface ITenantMembershipEnricher
{
    Task<IReadOnlyList<TenantMembershipInfo>> EnrichAsync(
        IReadOnlyList<TenantMembershipInfo> memberships,
        CancellationToken ct = default);
}

/// <summary>Default enricher: returns the memberships unchanged.</summary>
public sealed class PassThroughTenantMembershipEnricher : ITenantMembershipEnricher
{
    public Task<IReadOnlyList<TenantMembershipInfo>> EnrichAsync(
        IReadOnlyList<TenantMembershipInfo> memberships,
        CancellationToken ct = default)
        => Task.FromResult(memberships);
}
