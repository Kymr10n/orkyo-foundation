using Api.Models;
using Api.Repositories;
using Api.Security.Quotas;

namespace Api.Services;

/// <summary>
/// Service layer for sites. Enforces site-count quotas on creation; otherwise
/// delegates to <see cref="ISiteRepository"/>.
/// </summary>
public interface ISiteService
{
    /// <summary>Returns all sites, ordered by name.</summary>
    Task<List<SiteInfo>> GetAllAsync(CancellationToken ct = default);
    /// <summary>Returns a page of sites.</summary>
    Task<PagedResult<SiteInfo>> GetAllAsync(PageRequest page, CancellationToken ct = default);
    /// <summary>Returns the site with the given ID, or <c>null</c> if not found.</summary>
    Task<SiteInfo?> GetByIdAsync(Guid siteId, CancellationToken ct = default);
    /// <summary>Creates a site. Enforces site quota and throws <see cref="Helpers.ConflictException"/> on duplicate code.</summary>
    Task<SiteInfo> CreateAsync(string code, string name, string? description, string? address, CancellationToken ct = default);
    /// <summary>Updates a site. Returns <c>null</c> if not found.</summary>
    Task<SiteInfo?> UpdateAsync(Guid siteId, string code, string name, string? description, string? address, CancellationToken ct = default);
    /// <summary>Deletes a site. Returns <c>false</c> if not found.</summary>
    Task<bool> DeleteAsync(Guid siteId, CancellationToken ct = default);
}

public class SiteService : ISiteService
{
    private readonly ISiteRepository _repository;
    private readonly IQuotaEnforcer _quotaEnforcer;

    public SiteService(ISiteRepository repository, IQuotaEnforcer quotaEnforcer)
    {
        _repository = repository;
        _quotaEnforcer = quotaEnforcer;
    }

    public Task<List<SiteInfo>> GetAllAsync(CancellationToken ct = default) => _repository.GetAllAsync(ct);

    public Task<PagedResult<SiteInfo>> GetAllAsync(PageRequest page, CancellationToken ct = default) => _repository.GetAllAsync(page, ct);

    public Task<SiteInfo?> GetByIdAsync(Guid siteId, CancellationToken ct = default) => _repository.GetByIdAsync(siteId, ct);

    public async Task<SiteInfo> CreateAsync(string code, string name, string? description, string? address, CancellationToken ct = default)
    {
        var currentCount = await _repository.GetEstimatedCountAsync();
        await _quotaEnforcer.EnsureWithinLimitAsync(QuotaResourceTypes.ProductionSites, currentCount, 1, ct);
        return await _repository.CreateAsync(code, name, description, address);
    }

    public Task<SiteInfo?> UpdateAsync(Guid siteId, string code, string name, string? description, string? address, CancellationToken ct = default)
        => _repository.UpdateAsync(siteId, code, name, description, address, ct);

    public Task<bool> DeleteAsync(Guid siteId, CancellationToken ct = default) => _repository.DeleteAsync(siteId, ct);
}
