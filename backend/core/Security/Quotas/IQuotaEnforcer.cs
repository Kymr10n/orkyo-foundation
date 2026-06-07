using Api.Helpers;

namespace Api.Security.Quotas;

/// <summary>
/// Centralized numeric-quota enforcement. Implementations decide HOW limits are
/// determined (tier-based with per-tenant overrides, or unlimited). Domain code
/// calls <see cref="EnsureWithinLimitAsync"/> without knowing about tiers.
/// Feature entitlements (booleans) are handled separately by IFeatureGate.
/// </summary>
public interface IQuotaEnforcer
{
    /// <summary>
    /// Throws <see cref="QuotaExceededException"/> if applying <paramref name="requestedIncrement"/>
    /// to <paramref name="currentValue"/> would exceed the effective limit for the quota key.
    /// Values are <see cref="long"/> so byte-scale quotas (storage) are representable.
    /// </summary>
    Task EnsureWithinLimitAsync(
        string quotaKey,
        long currentValue,
        long requestedIncrement,
        CancellationToken ct = default);

    /// <summary>Effective limit for the quota key in the current context; -1 = unlimited.</summary>
    Task<long> GetLimitAsync(string quotaKey, CancellationToken ct = default);
}

/// <summary>Well-known numeric quota keys (canonical; match subscription_tier_quotas.quota_key).</summary>
public static class QuotaResourceTypes
{
    public const string ActiveSeats = "active_seats";
    public const string ProductionSites = "production_sites";
    public const string Spaces = "spaces";
    public const string StorageBytes = "storage_bytes";
}

/// <summary>
/// No-op implementation that allows everything. Used in Community (single-tenant) mode
/// and as the foundation fallback so foundation builds standalone.
/// </summary>
public sealed class NoOpQuotaEnforcer : IQuotaEnforcer
{
    public Task EnsureWithinLimitAsync(string quotaKey, long currentValue, long requestedIncrement, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<long> GetLimitAsync(string quotaKey, CancellationToken ct = default) => Task.FromResult(-1L);
}
