using Api.Constants;
using Api.Models;
using Api.Repositories;

namespace Api.Services;

/// <summary>
/// Service layer for generic resources (people, tools). Validates allocation mode and
/// availability constraints before persistence. Space-specific operations go through
/// <see cref="ISpaceService"/>.
/// </summary>
public interface IResourceService
{
    /// <summary>Returns all resources matching the given filter.</summary>
    Task<List<ResourceInfo>> GetAllAsync(ResourceListFilter filter, CancellationToken ct = default);
    /// <summary>Returns the resource with the given ID, or <c>null</c> if not found.</summary>
    Task<ResourceInfo?> GetByIdAsync(Guid id, CancellationToken ct = default);
    /// <summary>Creates a new resource. Validates allocation mode and availability percent.</summary>
    Task<ResourceInfo> CreateAsync(CreateResourceRequest request, CancellationToken ct = default);
    /// <summary>
    /// Updates a resource. Returns <c>null</c> if not found.
    /// Throws <see cref="System.Collections.Generic.KeyNotFoundException"/> if the resource was deleted between the existence check and the update.
    /// </summary>
    Task<ResourceInfo?> UpdateAsync(Guid id, UpdateResourceRequest request, CancellationToken ct = default);
    /// <summary>Deactivates a resource. Returns <c>false</c> if not found.</summary>
    Task<bool> DeactivateAsync(Guid id, CancellationToken ct = default);
}

public class ResourceService(
    IResourceRepository resourceRepository,
    IResourceTypeRepository resourceTypeRepository) : IResourceService
{
    public Task<List<ResourceInfo>> GetAllAsync(ResourceListFilter filter, CancellationToken ct = default)
        => resourceRepository.GetAllAsync(filter, ct);

    public Task<ResourceInfo?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => resourceRepository.GetByIdAsync(id, ct);

    public async Task<ResourceInfo> CreateAsync(CreateResourceRequest request, CancellationToken ct = default)
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
            request.BaseAvailabilityPercent,
            homeSiteId: request.HomeSiteId,
            // Current site defaults to the home site when not supplied.
            currentSiteId: request.CurrentSiteId ?? request.HomeSiteId,
            crossSiteAllowed: request.CrossSiteAllowed);
    }

    public async Task<ResourceInfo?> UpdateAsync(Guid id, UpdateResourceRequest request, CancellationToken ct = default)
    {
        if (request.AllocationMode is not null)
            ValidateAllocationMode(request.AllocationMode);
        if (request.BaseAvailabilityPercent.HasValue)
            ValidateAvailabilityPercent(request.BaseAvailabilityPercent.Value);
        if (request.Name is not null && string.IsNullOrWhiteSpace(request.Name))
            throw new ArgumentException("Name cannot be blank");

        // Verify existence; Space deactivation flows through SpaceService, not here.
        _ = await resourceRepository.GetByIdAsync(id)
            ?? throw new KeyNotFoundException($"Resource {id} not found");

        return await resourceRepository.UpdateAsync(id, request);
    }

    public Task<bool> DeactivateAsync(Guid id, CancellationToken ct = default)
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
