using Api.Models;

namespace Api.Repositories;

/// <summary>
/// Persistence layer for sites — the top-level physical locations that contain spaces.
/// Site codes must be unique within the deployment.
/// </summary>
public interface ISiteRepository
{
    /// <summary>Returns all sites, ordered by name. At most 200 results (full-set query).</summary>
    Task<List<SiteInfo>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Returns a page of sites, ordered by name.</summary>
    Task<PagedResult<SiteInfo>> GetAllAsync(PageRequest page, CancellationToken ct = default);

    /// <summary>Returns the site with the given ID, or <c>null</c> if not found.</summary>
    Task<SiteInfo?> GetByIdAsync(Guid siteId, CancellationToken ct = default);

    /// <summary>Returns an estimated count from the Postgres statistics table. Fast but not exact.</summary>
    Task<int> GetEstimatedCountAsync(CancellationToken ct = default);

    /// <summary>Creates a new site. Throws <see cref="Helpers.ConflictException"/> if the code already exists.</summary>
    Task<SiteInfo> CreateAsync(string code, string name, string? description, string? address, CancellationToken ct = default);

    /// <summary>
    /// Updates a site. Returns <c>null</c> if not found.
    /// Throws <see cref="Helpers.ConflictException"/> if the new code conflicts with another site.
    /// </summary>
    Task<SiteInfo?> UpdateAsync(Guid siteId, string code, string name, string? description, string? address, CancellationToken ct = default);

    /// <summary>Deletes a site. Returns <c>false</c> if not found.</summary>
    Task<bool> DeleteAsync(Guid siteId, CancellationToken ct = default);
}
