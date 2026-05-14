using Api.Constants;
using Api.Models;
using Api.Repositories;

namespace Api.Services;

public interface IResourceService
{
    Task<List<ResourceInfo>> GetAllAsync(ResourceListFilter filter);
    Task<ResourceInfo?> GetByIdAsync(Guid id);
    Task<ResourceInfo> CreateAsync(CreateResourceRequest request);
    Task<ResourceInfo?> UpdateAsync(Guid id, UpdateResourceRequest request);
    Task<bool> DeactivateAsync(Guid id);
}

public class ResourceService(
    IResourceRepository resourceRepository,
    IResourceTypeRepository resourceTypeRepository) : IResourceService
{
    public Task<List<ResourceInfo>> GetAllAsync(ResourceListFilter filter)
        => resourceRepository.GetAllAsync(filter);

    public Task<ResourceInfo?> GetByIdAsync(Guid id)
        => resourceRepository.GetByIdAsync(id);

    public async Task<ResourceInfo> CreateAsync(CreateResourceRequest request)
    {
        Validate(request.AllocationMode, request.BaseAvailabilityPercent, request.Name);

        var resourceType = await resourceTypeRepository.GetByKeyAsync(request.ResourceTypeKey)
            ?? throw new ArgumentException($"Resource type '{request.ResourceTypeKey}' not found");

        return await resourceRepository.CreateAsync(
            resourceType.Id,
            resourceType.Key,
            request.Name,
            request.Description,
            request.ExternalReference,
            request.AllocationMode,
            request.BaseAvailabilityPercent);
    }

    public async Task<ResourceInfo?> UpdateAsync(Guid id, UpdateResourceRequest request)
    {
        if (request.AllocationMode is not null)
            ValidateAllocationMode(request.AllocationMode);
        if (request.BaseAvailabilityPercent.HasValue)
            ValidateAvailabilityPercent(request.BaseAvailabilityPercent.Value);
        if (request.Name is not null && string.IsNullOrWhiteSpace(request.Name))
            throw new ArgumentException("Name cannot be blank");

        var existing = await resourceRepository.GetByIdAsync(id)
            ?? throw new KeyNotFoundException($"Resource {id} not found");

        // System resource types cannot be deactivated via the generic update.
        // (Space deactivation flows through SpaceService.)
        _ = existing;

        return await resourceRepository.UpdateAsync(id, request);
    }

    public Task<bool> DeactivateAsync(Guid id)
        => resourceRepository.DeactivateAsync(id);

    private static void Validate(string allocationMode, int baseAvailabilityPercent, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is required");
        ValidateAllocationMode(allocationMode);
        ValidateAvailabilityPercent(baseAvailabilityPercent);
    }

    private static void ValidateAllocationMode(string mode)
    {
        if (mode is not (AllocationModes.Exclusive or AllocationModes.Fractional or AllocationModes.ConcurrentCapacity))
            throw new ArgumentException($"Invalid allocation mode '{mode}'");
    }

    private static void ValidateAvailabilityPercent(int pct)
    {
        if (pct is < 0 or > 100)
            throw new ArgumentException("BaseAvailabilityPercent must be between 0 and 100");
    }
}
