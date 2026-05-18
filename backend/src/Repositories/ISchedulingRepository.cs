using Api.Models;

namespace Api.Repositories;

/// <summary>
/// Persistence layer for site scheduling configuration and off-time blocks.
/// Off-times mark periods when a site or specific resource is unavailable.
/// </summary>
public interface ISchedulingRepository
{
    // ── Settings ─────────────────────────────────────────────────────────────

    /// <summary>Returns the scheduling settings for a site, or <c>null</c> if not configured.</summary>
    Task<SchedulingSettingsInfo?> GetSettingsAsync(Guid siteId, CancellationToken ct = default);

    /// <summary>Creates or replaces the scheduling settings for a site.</summary>
    Task<SchedulingSettingsInfo> UpsertSettingsAsync(Guid siteId, UpsertSchedulingSettingsRequest request, CancellationToken ct = default);

    /// <summary>Removes custom scheduling settings, reverting to defaults. Returns <c>false</c> if no settings existed.</summary>
    Task<bool> DeleteSettingsAsync(Guid siteId, CancellationToken ct = default);

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>Returns the site ID that owns the given resource, or <c>null</c> if the resource is not site-bound.</summary>
    Task<Guid?> GetSiteIdForResourceAsync(Guid resourceId, CancellationToken ct = default);

    // ── Off-Times ────────────────────────────────────────────────────────────

    /// <summary>Returns all off-time blocks for a site, ordered by start time.</summary>
    Task<List<OffTimeInfo>> GetOffTimesAsync(Guid siteId, CancellationToken ct = default);

    /// <summary>Returns off-time blocks that include a specific resource, scoped to the site.</summary>
    Task<List<OffTimeInfo>> GetOffTimesByResourceAsync(Guid resourceId, Guid siteId, CancellationToken ct = default);

    /// <summary>Returns a specific off-time block scoped to its site, or <c>null</c> if not found.</summary>
    Task<OffTimeInfo?> GetOffTimeByIdAsync(Guid siteId, Guid offTimeId, CancellationToken ct = default);

    /// <summary>Returns a specific off-time block by ID regardless of site, or <c>null</c> if not found.</summary>
    Task<OffTimeInfo?> GetOffTimeByIdAsync(Guid offTimeId, CancellationToken ct = default);

    /// <summary>Creates an off-time block for the given site.</summary>
    Task<OffTimeInfo> CreateOffTimeAsync(Guid siteId, CreateOffTimeRequest request, CancellationToken ct = default);

    /// <summary>Updates an off-time block. Returns <c>null</c> if not found.</summary>
    Task<OffTimeInfo?> UpdateOffTimeAsync(Guid siteId, Guid offTimeId, UpdateOffTimeRequest request, CancellationToken ct = default);

    /// <summary>Deletes an off-time block. Returns <c>false</c> if not found.</summary>
    Task<bool> DeleteOffTimeAsync(Guid siteId, Guid offTimeId, CancellationToken ct = default);
}
