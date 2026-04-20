using Api.Models;

namespace Api.Repositories;

public interface ISiteRepository
{
    Task<List<SiteInfo>> GetAllAsync();
    Task<PagedResult<SiteInfo>> GetAllAsync(PageRequest page);
    Task<SiteInfo?> GetByIdAsync(Guid siteId);
    Task<int> GetEstimatedCountAsync();
    Task<SiteInfo> CreateAsync(string code, string name, string? description, string? address);
    Task<SiteInfo?> UpdateAsync(Guid siteId, string code, string name, string? description, string? address);
    Task<bool> DeleteAsync(Guid siteId);
}
