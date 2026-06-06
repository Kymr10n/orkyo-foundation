namespace Api.Services;

// Neutral tenant identity. Foundation carries NO commercial/tier concept here —
// tier/quota/entitlement resolution lives behind IQuotaEnforcer / IFeatureGate,
// which the SaaS edition implements and Community satisfies with allow-all.
public class TenantContext
{
    public required Guid TenantId { get; init; }
    public required string TenantSlug { get; init; }
    public required string TenantDbConnectionString { get; init; }
    public required string Status { get; init; }

    public bool IsSuspended => TenantStatusPolicy.IsSuspended(Status);
    public bool IsActive => TenantStatusPolicy.IsActive(Status);
}

public interface ITenantResolver
{
    Task<TenantContext?> ResolveTenantAsync(string? subdomain, string? tenantHeader, CancellationToken ct = default);

    /// <summary>Evict a cached tenant entry (e.g. after status change).</summary>
    void InvalidateCache(string slug);
}
