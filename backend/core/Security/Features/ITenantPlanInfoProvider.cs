namespace Api.Security.Features;

/// <summary>
/// Display-only plan label for a tenant, surfaced in /me and admin views.
/// Neutral: foundation does not know what a "tier" is. SaaS resolves this from
/// tenant_subscriptions; Community returns its single unlimited plan.
/// </summary>
public sealed record TenantPlanInfo(string PlanCode, string PlanLabel);

public interface ITenantPlanInfoProvider
{
    /// <summary>Resolve plan labels for the given tenants. Missing tenants may be omitted.</summary>
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
