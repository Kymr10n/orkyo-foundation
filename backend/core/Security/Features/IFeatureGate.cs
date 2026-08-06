using Api.Helpers;

namespace Api.Security.Features;

/// <summary>
/// Neutral feature-entitlement gate. Foundation domain code asks whether a named
/// feature is available without knowing about tiers, plans, or editions.
/// SaaS backs this with tier/subscription entitlements; Community allows everything.
/// </summary>
public interface IFeatureGate
{
    /// <summary>True if the feature is enabled for the current tenant/context.</summary>
    Task<bool> IsEnabledAsync(string featureKey, CancellationToken ct = default);

    /// <summary>Throws <see cref="FeatureNotAvailableException"/> if the feature is not enabled.</summary>
    Task EnsureEnabledAsync(string featureKey, CancellationToken ct = default);
}

/// <summary>Well-known feature keys gated across the application.</summary>
public static class FeatureKeys
{
    public const string AutoSchedule = "auto_schedule";
    public const string ApiAccess = "api_access_enabled";
    public const string AuditLog = "audit_log_enabled";
    public const string DataExport = "data_export_enabled";
    public const string CalendarFeed = "calendar_feed_enabled";

    /// <summary>
    /// The keys this application actually enforces server-side, and therefore the ones
    /// reported to clients so they can present the feature or its upsell. Clients must not
    /// re-derive these from the plan code — that duplicates the entitlement table and drifts.
    /// <para>
    /// <see cref="AutoSchedule"/> is deliberately absent: it has no entitlement row and no
    /// endpoint gate, so reporting it would hand clients a default-denied <c>false</c>.
    /// </para>
    /// </summary>
    public static readonly IReadOnlyList<string> Enforced = new[]
    {
        ApiAccess,
        AuditLog,
        DataExport,
        CalendarFeed,
    };
}

/// <summary>
/// Default implementation that enables every feature. Used by Community (single-tenant,
/// no commercial gating) and as the foundation fallback so foundation builds standalone.
/// </summary>
public sealed class AllFeaturesEnabledGate : IFeatureGate
{
    public Task<bool> IsEnabledAsync(string featureKey, CancellationToken ct = default) => Task.FromResult(true);
    public Task EnsureEnabledAsync(string featureKey, CancellationToken ct = default) => Task.CompletedTask;
}
