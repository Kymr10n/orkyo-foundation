namespace Api.Security.Features;

/// <summary>
/// Resolves the server-computed feature entitlements for tenants, so the session bootstrap
/// can tell clients which features to present.
/// <para>
/// This exists so clients never re-derive entitlements from the plan code: the server owns
/// the plan → feature mapping and enforces it (<see cref="IFeatureGate"/>), and /me reports
/// the result of that same mapping. Per-tenant overrides therefore reach the UI for free.
/// </para>
/// <para>
/// Batched by tenant rather than scoped to "the current tenant" because /me carries
/// <c>SkipTenantResolution</c> — it answers for every membership the user has, on the apex
/// domain where no tenant is resolved at all.
/// </para>
/// Neutral: foundation does not know what a plan is. SaaS resolves this from the tenant's
/// subscription; Community enables everything.
/// </summary>
public interface ITenantEntitlementProvider
{
    /// <summary>
    /// Resolve <see cref="FeatureKeys.Enforced"/> entitlements per tenant. Missing tenants may
    /// be omitted; callers treat an absent tenant or key as not entitled.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlyDictionary<string, bool>>> GetEntitlementsAsync(
        IReadOnlyCollection<Guid> tenantIds,
        CancellationToken ct = default);
}

/// <summary>
/// Default provider that reports every enforced feature as enabled, matching
/// <see cref="AllFeaturesEnabledGate"/>. Used by Community (single-tenant, no commercial
/// gating) and as the foundation fallback so foundation builds standalone.
/// </summary>
public sealed class AllFeaturesEntitlementProvider : ITenantEntitlementProvider
{
    public Task<IReadOnlyDictionary<Guid, IReadOnlyDictionary<string, bool>>> GetEntitlementsAsync(
        IReadOnlyCollection<Guid> tenantIds,
        CancellationToken ct = default)
    {
        IReadOnlyDictionary<string, bool> allEnabled =
            FeatureKeys.Enforced.ToDictionary(key => key, _ => true);

        IReadOnlyDictionary<Guid, IReadOnlyDictionary<string, bool>> result =
            tenantIds.ToDictionary(id => id, _ => allEnabled);

        return Task.FromResult(result);
    }
}
