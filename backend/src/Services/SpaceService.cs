using Api.Models;
using Api.Repositories;
using Api.Security.Quotas;

namespace Api.Services;

public interface ISpaceService
{
    Task<List<SpaceInfo>> GetAllAsync(Guid siteId);
    Task<PagedResult<SpaceInfo>> GetAllAsync(Guid siteId, PageRequest page);
    Task<SpaceInfo?> GetByIdAsync(Guid siteId, Guid spaceId);
    Task<SpaceInfo> CreateAsync(Guid siteId, string name, string? code, string? description, bool isPhysical, SpaceGeometry? geometry, Dictionary<string, object>? properties, int capacity = 1);
    Task<SpaceInfo?> UpdateAsync(Guid siteId, Guid spaceId, string? name, string? code, string? description, SpaceGeometry? geometry, Dictionary<string, object>? properties, Guid? groupId = null, int? capacity = null);
    Task<bool> DeleteAsync(Guid siteId, Guid spaceId);
}

public class SpaceService : ISpaceService
{
    private readonly ISpaceRepository _repository;
    private readonly IQuotaEnforcer _quotaEnforcer;

    public SpaceService(ISpaceRepository repository, IQuotaEnforcer quotaEnforcer)
    {
        _repository = repository;
        _quotaEnforcer = quotaEnforcer;
    }

    public Task<List<SpaceInfo>> GetAllAsync(Guid siteId) => _repository.GetAllAsync(siteId);

    public Task<PagedResult<SpaceInfo>> GetAllAsync(Guid siteId, PageRequest page) => _repository.GetAllAsync(siteId, page);

    public Task<SpaceInfo?> GetByIdAsync(Guid siteId, Guid spaceId) => _repository.GetByIdAsync(siteId, spaceId);

    public async Task<SpaceInfo> CreateAsync(Guid siteId, string name, string? code, string? description, bool isPhysical, SpaceGeometry? geometry, Dictionary<string, object>? properties, int capacity = 1)
    {
        var currentCount = await _repository.GetEstimatedCountAsync();
        _quotaEnforcer.EnforceLimit(QuotaResourceTypes.Spaces, currentCount);
        return await _repository.CreateAsync(siteId, name, code, description, isPhysical, geometry, properties, capacity);
    }

    public Task<SpaceInfo?> UpdateAsync(Guid siteId, Guid spaceId, string? name, string? code, string? description, SpaceGeometry? geometry, Dictionary<string, object>? properties, Guid? groupId = null, int? capacity = null)
        => _repository.UpdateAsync(siteId, spaceId, name, code, description, geometry, properties, groupId, capacity);

    public Task<bool> DeleteAsync(Guid siteId, Guid spaceId) => _repository.DeleteAsync(siteId, spaceId);
}
