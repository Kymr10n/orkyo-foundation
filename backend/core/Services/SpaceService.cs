using Api.Constants;
using Api.Models;
using Api.Repositories;
using Api.Security.Quotas;

namespace Api.Services;

/// <summary>
/// Service layer for spaces. Creates the underlying resource record (type=space)
/// alongside the space record in a single transaction; enforces space quotas.
/// </summary>
public interface ISpaceService
{
    /// <summary>Returns all spaces for the given site.</summary>
    Task<List<SpaceInfo>> GetAllAsync(Guid siteId, CancellationToken ct = default);
    /// <summary>Returns a page of spaces for the given site.</summary>
    Task<PagedResult<SpaceInfo>> GetAllAsync(Guid siteId, PageRequest page, CancellationToken ct = default);
    /// <summary>Returns a space by its resource ID within the site, or <c>null</c> if not found.</summary>
    Task<SpaceInfo?> GetByIdAsync(Guid siteId, Guid resourceId, CancellationToken ct = default);
    /// <summary>Creates a space. Enforces space quota; throws <see cref="Helpers.ConflictException"/> on duplicate code.</summary>
    Task<SpaceInfo> CreateAsync(Guid siteId, string name, string? code, string? description, bool isPhysical, SpaceGeometry? geometry, Dictionary<string, object>? properties, int capacity = 1, CancellationToken ct = default);
    /// <summary>Updates a space. Returns <c>null</c> if not found.</summary>
    Task<SpaceInfo?> UpdateAsync(Guid siteId, Guid resourceId, string? name, string? code, string? description, SpaceGeometry? geometry, Dictionary<string, object>? properties, Guid? groupId = null, int? capacity = null, CancellationToken ct = default);
    /// <summary>Deletes a space and its underlying resource record. Returns <c>false</c> if not found.</summary>
    Task<bool> DeleteAsync(Guid siteId, Guid resourceId, CancellationToken ct = default);
}

public class SpaceService : ISpaceService
{
    private readonly ISpaceRepository _repository;
    private readonly IResourceRepository _resourceRepository;
    private readonly IResourceTypeRepository _resourceTypeRepository;
    private readonly IQuotaEnforcer _quotaEnforcer;
    private readonly IQuotaUsageRollup _rollup;

    public SpaceService(
        ISpaceRepository repository,
        IResourceRepository resourceRepository,
        IResourceTypeRepository resourceTypeRepository,
        IQuotaEnforcer quotaEnforcer,
        IQuotaUsageRollup rollup)
    {
        _repository = repository;
        _resourceRepository = resourceRepository;
        _resourceTypeRepository = resourceTypeRepository;
        _quotaEnforcer = quotaEnforcer;
        _rollup = rollup;
    }

    public Task<List<SpaceInfo>> GetAllAsync(Guid siteId, CancellationToken ct = default) => _repository.GetAllAsync(siteId, ct);

    public Task<PagedResult<SpaceInfo>> GetAllAsync(Guid siteId, PageRequest page, CancellationToken ct = default) => _repository.GetAllAsync(siteId, page, ct);

    public Task<SpaceInfo?> GetByIdAsync(Guid siteId, Guid resourceId, CancellationToken ct = default) => _repository.GetByIdAsync(siteId, resourceId, ct);

    public async Task<SpaceInfo> CreateAsync(
        Guid siteId, string name, string? code, string? description,
        bool isPhysical, SpaceGeometry? geometry, Dictionary<string, object>? properties, int capacity = 1, CancellationToken ct = default)
    {
        var currentCount = await _repository.GetEstimatedCountAsync();
        await _quotaEnforcer.EnsureWithinLimitAsync(QuotaResourceTypes.Spaces, currentCount, 1, ct);

        // Create the resources row first (spaces.id FK → resources.id).
        var spaceType = await _resourceTypeRepository.GetByKeyAsync(ResourceTypeKeys.Space)
            ?? throw new InvalidOperationException("Space resource type not found");

        var resourceId = Guid.NewGuid();
        await _resourceRepository.CreateAsync(
            spaceType.Id, spaceType.Key, name, description,
            externalReference: null, AllocationModes.Exclusive, 100, id: resourceId);

        var space = await _repository.CreateAsync(resourceId, siteId, code, isPhysical, geometry, properties, capacity);
        await _rollup.RecordDeltaAsync(QuotaResourceTypes.Spaces, 1, ct);
        return space;
    }

    public async Task<SpaceInfo?> UpdateAsync(
        Guid siteId, Guid resourceId, string? name, string? code, string? description,
        SpaceGeometry? geometry, Dictionary<string, object>? properties, Guid? groupId = null, int? capacity = null, CancellationToken ct = default)
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

    public async Task<bool> DeleteAsync(Guid siteId, Guid resourceId, CancellationToken ct = default)
    {
        var deleted = await _repository.DeleteAsync(siteId, resourceId);
        if (deleted)
        {
            await _resourceRepository.DeactivateAsync(resourceId);
            await _rollup.RecordDeltaAsync(QuotaResourceTypes.Spaces, -1, ct);
        }
        return deleted;
    }
}
