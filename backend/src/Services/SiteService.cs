using Api.Models;
using Api.Repositories;
using Api.Security.Quotas;

namespace Api.Services;

public interface ISiteService
{
    Task<List<SiteInfo>> GetAllAsync();
    Task<PagedResult<SiteInfo>> GetAllAsync(PageRequest page);
    Task<SiteInfo?> GetByIdAsync(Guid siteId);
    Task<SiteInfo> CreateAsync(string code, string name, string? description, string? address);
    Task<SiteInfo?> UpdateAsync(Guid siteId, string code, string name, string? description, string? address);
    Task<bool> DeleteAsync(Guid siteId);
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

    public Task<List<SiteInfo>> GetAllAsync() => _repository.GetAllAsync();

    public Task<PagedResult<SiteInfo>> GetAllAsync(PageRequest page) => _repository.GetAllAsync(page);

    public Task<SiteInfo?> GetByIdAsync(Guid siteId) => _repository.GetByIdAsync(siteId);

    public async Task<SiteInfo> CreateAsync(string code, string name, string? description, string? address)
    {
        var currentCount = await _repository.GetEstimatedCountAsync();
        _quotaEnforcer.EnforceLimit(QuotaResourceTypes.Sites, currentCount);
        return await _repository.CreateAsync(code, name, description, address);
    }

    public Task<SiteInfo?> UpdateAsync(Guid siteId, string code, string name, string? description, string? address)
        => _repository.UpdateAsync(siteId, code, name, description, address);

    public Task<bool> DeleteAsync(Guid siteId) => _repository.DeleteAsync(siteId);
}
