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
    /// <summary>Bulk settings for many sites in one query, keyed by site id; sites without settings are omitted.</summary>
    Task<Dictionary<Guid, SchedulingSettingsInfo>> GetSettingsBySitesAsync(IReadOnlyList<Guid> siteIds, CancellationToken ct = default);
    Task<SchedulingSettingsInfo> UpsertSettingsAsync(Guid siteId, UpsertSchedulingSettingsRequest request, CancellationToken ct = default);
    Task<bool> DeleteSettingsAsync(Guid siteId, CancellationToken ct = default);

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>Returns the anchoring site for the given resource (spaces.site_id for spaces,
    /// home_site_id for people/tools), or <c>null</c> if the resource has no site.</summary>
    Task<Guid?> GetSiteIdForResourceAsync(Guid resourceId, CancellationToken ct = default);

    /// <summary>Bulk resource→anchoring-site map (spaces.site_id or home_site_id) in one query — for batch validation.</summary>
    Task<Dictionary<Guid, Guid>> GetSiteIdsForResourcesAsync(IReadOnlyList<Guid> resourceIds, CancellationToken ct = default);

    /// <summary>Returns resource type IDs keyed by resource ID.</summary>
    Task<Dictionary<Guid, Guid>> GetResourceTypeIdsAsync(IReadOnlyList<Guid> resourceIds, CancellationToken ct = default);
}
