namespace Api.Security.Quotas;

/// <summary>
/// Incremental usage rollup for cross-tenant dimensions (production_sites, spaces, storage_bytes).
/// Foundation calls this after a successful mutation; SaaS writes to the control-plane rollup table;
/// Community and the foundation standalone build use the no-op.
/// </summary>
public interface IQuotaUsageRollup
{
    /// <summary>
    /// Adjusts the stored rollup value for <paramref name="quotaKey"/> by <paramref name="delta"/>.
    /// Negative deltas are clamped to 0 at the DB level.
    /// </summary>
    Task RecordDeltaAsync(string quotaKey, long delta, CancellationToken ct = default);
}

/// <summary>
/// No-op rollup — used by Community and the foundation standalone build.
/// </summary>
public sealed class NoOpQuotaUsageRollup : IQuotaUsageRollup
{
    public Task RecordDeltaAsync(string quotaKey, long delta, CancellationToken ct = default) => Task.CompletedTask;
}
