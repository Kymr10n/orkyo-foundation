using Api.Models;

namespace Api.Repositories;

/// <summary>
/// Persistence layer for spaces — physical areas within a site (rooms, desks, zones).
/// A space is stored as a join between the <c>resources</c> table (name, type) and
/// the <c>spaces</c> table (geometry, site assignment, capacity).
/// </summary>
public interface ISpaceRepository
{
    /// <summary>Returns all spaces for the given site, ordered by name.</summary>
    Task<List<SpaceInfo>> GetAllAsync(Guid siteId, CancellationToken ct = default);

    /// <summary>Bulk fetch spaces for many sites in one query, keyed by site id — for export.
    /// Per-site lists carry the same code/name ordering as <see cref="GetAllAsync(Guid, CancellationToken)"/>.</summary>
    Task<Dictionary<Guid, List<SpaceInfo>>> GetBySitesAsync(IReadOnlyList<Guid> siteIds, CancellationToken ct = default);

    /// <summary>Returns a page of spaces for the given site.</summary>
    Task<PagedResult<SpaceInfo>> GetAllAsync(Guid siteId, PageRequest page, CancellationToken ct = default);

    /// <summary>Returns the space with the given resource ID within the site, or <c>null</c> if not found.</summary>
    Task<SpaceInfo?> GetByIdAsync(Guid siteId, Guid resourceId, CancellationToken ct = default);

    /// <summary>Returns an estimated row count from Postgres statistics. Fast but not exact.</summary>
    Task<int> GetEstimatedCountAsync(CancellationToken ct = default);

    /// <summary>
    /// Creates a new space. The <paramref name="resourceId"/> must refer to an existing resource.
    /// Throws <see cref="Helpers.ConflictException"/> if the code already exists in the site.
    /// </summary>
    Task<SpaceInfo> CreateAsync(Guid resourceId, Guid siteId, string? code, bool isPhysical, SpaceGeometry? geometry, Dictionary<string, object>? properties, int capacity = 1, CancellationToken ct = default);

    /// <summary>Updates a space. Returns <c>null</c> if not found.</summary>
    Task<SpaceInfo?> UpdateAsync(Guid siteId, Guid resourceId, string? code, SpaceGeometry? geometry, Dictionary<string, object>? properties, int? capacity = null, CancellationToken ct = default);

    /// <summary>Deletes a space. Returns <c>false</c> if not found.</summary>
    Task<bool> DeleteAsync(Guid siteId, Guid resourceId, CancellationToken ct = default);
}
