using Api.Helpers;

namespace Api.Security.Quotas;

/// <summary>
/// Centralized quota enforcement service.
/// Implementations decide HOW limits are determined (tier-based, config-based, unlimited).
/// Domain code calls <see cref="EnforceLimit"/> without knowing about tiers.
/// </summary>
public interface IQuotaEnforcer
{
    /// <summary>
    /// Check whether the org can create another resource of the given type.
    /// Throws <see cref="QuotaExceededException"/> if the quota is exceeded.
    /// </summary>
    void EnforceLimit(string resourceType, int currentCount);

    /// <summary>
    /// Get the limit for a specific resource type in the current context.
    /// Returns -1 for unlimited.
    /// </summary>
    int GetLimit(string resourceType);
}

/// <summary>
/// Well-known resource type constants used across quota enforcement.
/// </summary>
public static class QuotaResourceTypes
{
    public const string ActiveSeats = "active seats";
    public const string Sites = "sites";
    public const string Spaces = "spaces";
}

/// <summary>
/// No-op implementation that allows everything. Used in community (single-org) mode.
/// </summary>
public sealed class NoOpQuotaEnforcer : IQuotaEnforcer
{
    public void EnforceLimit(string resourceType, int currentCount) { }
    public int GetLimit(string resourceType) => -1;
}
