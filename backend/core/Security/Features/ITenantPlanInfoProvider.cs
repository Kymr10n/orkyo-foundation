namespace Api.Security.Features;

/// <summary>
/// A tenant's plan identity, in both representations.
/// <para>
/// <c>PlanCode</c> is the machine-readable identifier (always lowercase; SaaS
/// <c>subscription_tiers.code</c>). This is what goes on the wire — /me and the admin user
/// list — and what clients compare against to decide feature presentation.
/// </para>
/// <para>
/// <c>PlanLabel</c> is human-facing display text and must never be sent where a code is
/// expected: clients match literal codes, so "Enterprise" silently reads as "not entitled".
/// </para>
/// Neutral: foundation does not know what a "tier" is. SaaS resolves this from
/// tenant_subscriptions; Community returns its single unlimited plan.
/// </summary>
public sealed record TenantPlanInfo(string PlanCode, string PlanLabel);

public interface ITenantPlanInfoProvider
{
    /// <summary>Resolve plan code + label for the given tenants. Missing tenants may be omitted.</summary>
    Task<IReadOnlyDictionary<Guid, TenantPlanInfo>> GetPlanInfoAsync(
        IReadOnlyCollection<Guid> tenantIds,
        CancellationToken ct = default);
}

/// <summary>
/// Default provider that reports a single unlimited plan for every tenant. Used by
/// Community and as the foundation fallback so foundation builds standalone.
/// </summary>
public sealed class SinglePlanInfoProvider : ITenantPlanInfoProvider
{
    public const string PlanCode = "community";
    public const string PlanLabel = "Community";

    public Task<IReadOnlyDictionary<Guid, TenantPlanInfo>> GetPlanInfoAsync(
        IReadOnlyCollection<Guid> tenantIds,
        CancellationToken ct = default)
    {
        IReadOnlyDictionary<Guid, TenantPlanInfo> result =
            tenantIds.ToDictionary(id => id, _ => new TenantPlanInfo(PlanCode, PlanLabel));
        return Task.FromResult(result);
    }
}
