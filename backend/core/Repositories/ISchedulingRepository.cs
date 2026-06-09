using Api.Models;

namespace Api.Repositories;

/// <summary>
/// Persistence layer for site scheduling configuration.
/// Off-time / absence data has been moved to
/// <see cref="IAvailabilityEventRepository"/> and <see cref="IResourceAbsenceRepository"/>.
/// </summary>
public interface ISchedulingRepository
{
    // ── Settings ─────────────────────────────────────────────────────────────

    Task<SchedulingSettingsInfo?> GetSettingsAsync(Guid siteId, CancellationToken ct = default);
    Task<SchedulingSettingsInfo> UpsertSettingsAsync(Guid siteId, UpsertSchedulingSettingsRequest request, CancellationToken ct = default);
    Task<bool> DeleteSettingsAsync(Guid siteId, CancellationToken ct = default);

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>Returns the site ID that owns the given resource, or <c>null</c> if the resource is not site-bound (e.g. a person).</summary>
    Task<Guid?> GetSiteIdForResourceAsync(Guid resourceId, CancellationToken ct = default);

    /// <summary>Bulk resource→site map (spaces only) in one query — for batch validation.</summary>
    Task<Dictionary<Guid, Guid>> GetSiteIdsForResourcesAsync(IReadOnlyList<Guid> resourceIds, CancellationToken ct = default);

    /// <summary>Returns resource type IDs keyed by resource ID.</summary>
    Task<Dictionary<Guid, Guid>> GetResourceTypeIdsAsync(IReadOnlyList<Guid> resourceIds, CancellationToken ct = default);
}
