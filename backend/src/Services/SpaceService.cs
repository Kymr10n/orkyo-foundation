using Api.Constants;
using Api.Models;
using Api.Repositories;
using Api.Security.Quotas;

namespace Api.Services;

public interface ISpaceService
{
    Task<List<SpaceInfo>> GetAllAsync(Guid siteId);
    Task<PagedResult<SpaceInfo>> GetAllAsync(Guid siteId, PageRequest page);
    Task<SpaceInfo?> GetByIdAsync(Guid siteId, Guid resourceId);
    Task<SpaceInfo> CreateAsync(Guid siteId, string name, string? code, string? description, bool isPhysical, SpaceGeometry? geometry, Dictionary<string, object>? properties, int capacity = 1);
    Task<SpaceInfo?> UpdateAsync(Guid siteId, Guid resourceId, string? name, string? code, string? description, SpaceGeometry? geometry, Dictionary<string, object>? properties, Guid? groupId = null, int? capacity = null);
    Task<bool> DeleteAsync(Guid siteId, Guid resourceId);
}

public class SpaceService : ISpaceService
{
    private readonly ISpaceRepository _repository;
    private readonly IResourceRepository _resourceRepository;
    private readonly IResourceTypeRepository _resourceTypeRepository;
    private readonly IQuotaEnforcer _quotaEnforcer;

    public SpaceService(
        ISpaceRepository repository,
        IResourceRepository resourceRepository,
        IResourceTypeRepository resourceTypeRepository,
        IQuotaEnforcer quotaEnforcer)
    {
        _repository = repository;
        _resourceRepository = resourceRepository;
        _resourceTypeRepository = resourceTypeRepository;
        _quotaEnforcer = quotaEnforcer;
    }

    public Task<List<SpaceInfo>> GetAllAsync(Guid siteId) => _repository.GetAllAsync(siteId);

    public Task<PagedResult<SpaceInfo>> GetAllAsync(Guid siteId, PageRequest page) => _repository.GetAllAsync(siteId, page);

    public Task<SpaceInfo?> GetByIdAsync(Guid siteId, Guid resourceId) => _repository.GetByIdAsync(siteId, resourceId);

    public async Task<SpaceInfo> CreateAsync(
        Guid siteId, string name, string? code, string? description,
        bool isPhysical, SpaceGeometry? geometry, Dictionary<string, object>? properties, int capacity = 1)
    {
        var currentCount = await _repository.GetEstimatedCountAsync();
        _quotaEnforcer.EnforceLimit(QuotaResourceTypes.Spaces, currentCount);

        // Create the resources row first (spaces.id FK → resources.id).
        var spaceType = await _resourceTypeRepository.GetByKeyAsync(ResourceTypeKeys.Space)
            ?? throw new InvalidOperationException("Space resource type not found");

        var resourceId = Guid.NewGuid();
        await _resourceRepository.CreateAsync(
            spaceType.Id, spaceType.Key, name, description,
            externalReference: null, AllocationModes.Exclusive, 100, id: resourceId);

        return await _repository.CreateAsync(resourceId, siteId, code, isPhysical, geometry, properties, capacity);
    }

    public async Task<SpaceInfo?> UpdateAsync(
        Guid siteId, Guid resourceId, string? name, string? code, string? description,
        SpaceGeometry? geometry, Dictionary<string, object>? properties, Guid? groupId = null, int? capacity = null)
    {
        // Mirror name and description to resources.
        if (name != null || description != null)
        {
            await _resourceRepository.UpdateAsync(resourceId, new UpdateResourceRequest
            {
                Name = name,
                Description = description,
            });
        }

        return await _repository.UpdateAsync(siteId, resourceId, code, geometry, properties, groupId, capacity);
    }

    public async Task<bool> DeleteAsync(Guid siteId, Guid resourceId)
    {
        var deleted = await _repository.DeleteAsync(siteId, resourceId);
        if (deleted)
            await _resourceRepository.DeactivateAsync(resourceId);
        return deleted;
    }
}
