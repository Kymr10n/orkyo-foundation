using System.Text.Json;
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
    IResourceTypeRepository resourceTypeRepository,
    IResourceMetadataValidator metadataValidator) : IResourceService
{
    public Task<List<ResourceInfo>> GetAllAsync(ResourceListFilter filter, CancellationToken ct = default)
        => resourceRepository.GetAllAsync(filter, ct);

    public Task<ResourceInfo?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => resourceRepository.GetByIdAsync(id, ct);

    public async Task<ResourceInfo> CreateAsync(CreateResourceRequest request, CancellationToken ct = default)
    {
        Validate(request.AllocationMode, request.BaseAvailabilityPercent, request.Name);

        var resourceType = await resourceTypeRepository.GetByKeyAsync(request.ResourceTypeKey, ct)
            ?? throw new ArgumentException($"Resource type '{request.ResourceTypeKey}' not found");

        if (!resourceType.IsActive)
            throw new ArgumentException($"Resource type '{resourceType.Key}' is not active");

        var metadataJson = await ValidateMetadataAsync(resourceType.Id, request.Metadata, requireComplete: true, ct);

        return await resourceRepository.CreateAsync(resourceType.Id, resourceType.Key, request.Name, request.Description, request.ExternalReference, request.AllocationMode, request.BaseAvailabilityPercent, homeSiteId: request.HomeSiteId, crossSiteAllowed: request.CrossSiteAllowed, metadataJson: metadataJson, ct: ct);
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
        var existing = await resourceRepository.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Resource {id} not found");

        // A supplied Metadata document replaces the stored one wholesale, so it must be complete.
        if (request.Metadata is not null)
            await ValidateMetadataAsync(existing.ResourceTypeId, request.Metadata, requireComplete: true, ct);

        return await resourceRepository.UpdateAsync(id, request, ct);
    }

    /// <summary>
    /// Validates custom field values against the type's definitions and returns the document
    /// to persist (null when there is nothing to store). Blockers surface as
    /// <see cref="ArgumentException"/>, matching this service's other validation failures.
    /// </summary>
    private async Task<string?> ValidateMetadataAsync(
        Guid resourceTypeId, Dictionary<string, JsonElement>? metadata, bool requireComplete, CancellationToken ct)
    {
        var result = await metadataValidator.ValidateAsync(resourceTypeId, metadata, requireComplete, ct);
        if (!result.IsValid)
            throw new ArgumentException(string.Join("; ", result.Blockers.Select(b => b.Message)));

        return metadata is null ? null : JsonSerializer.Serialize(metadata);
    }

    public Task<bool> DeactivateAsync(Guid id, CancellationToken ct = default)
        => resourceRepository.DeactivateAsync(id, ct);

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
