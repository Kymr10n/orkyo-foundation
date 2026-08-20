using System.Text.Json;
using Api.Constants;
using Api.Models;
using Api.Repositories;

namespace Api.Services;

/// <summary>
/// Manages resource types. No type is built in: every row is an ordinary tenant type, whether
/// it was created by hand, instantiated from the catalog (see ResourceTypeCatalogService), or
/// survives from the era of migration-seeded built-ins. Only the key is immutable
/// (UpdateResourceTypeRequest has no Key); everything else is the tenant's to change.
/// </summary>
public interface IResourceTypeService
{
    Task<List<ResourceTypeInfo>> GetAllAsync(bool? isActive = null, CancellationToken ct = default);
    Task<ResourceTypeInfo?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<ResourceTypeInfo> CreateAsync(CreateResourceTypeRequest request, CancellationToken ct = default);
    Task<ResourceTypeInfo?> UpdateAsync(Guid id, UpdateResourceTypeRequest request, CancellationToken ct = default);
    /// <summary>
    /// Removes a type: hard-deletes when no resources reference it, otherwise deactivates.
    /// Returns false when the type does not exist.
    /// </summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}

public class ResourceTypeService(IResourceTypeRepository typeRepository) : IResourceTypeService
{
    public async Task<List<ResourceTypeInfo>> GetAllAsync(bool? isActive = null, CancellationToken ct = default)
    {
        var types = await typeRepository.GetAllAsync(ct);
        return isActive.HasValue
            ? types.Where(t => t.IsActive == isActive.Value).ToList()
            : types;
    }

    public Task<ResourceTypeInfo?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => typeRepository.GetByIdAsync(id, ct);

    public async Task<ResourceTypeInfo> CreateAsync(CreateResourceTypeRequest request, CancellationToken ct = default)
    {
        if (await typeRepository.GetByKeyAsync(request.Key, ct) is not null)
            throw new ArgumentException($"A resource type with key '{request.Key}' already exists");

        return await typeRepository.CreateAsync(request, ct);
    }

    public async Task<ResourceTypeInfo?> UpdateAsync(Guid id, UpdateResourceTypeRequest request, CancellationToken ct = default)
    {
        var existing = await typeRepository.GetByIdAsync(id, ct);
        if (existing is null) return null;

        return await typeRepository.UpdateAsync(id, request, ct);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var existing = await typeRepository.GetByIdAsync(id, ct);
        if (existing is null) return false;

        // Deleting a type still in use would orphan its resources, or trip the ON DELETE
        // RESTRICT from request_target_resource_types and surface a raw 23503. Retire it
        // instead — a request that still asks for this type keeps a meaningful target.
        if (await typeRepository.CountResourcesAsync(id, ct) > 0
            || await typeRepository.CountRequestTargetsAsync(id, ct) > 0)
        {
            await typeRepository.UpdateAsync(id, new UpdateResourceTypeRequest { IsActive = false }, ct);
            return true;
        }

        return await typeRepository.DeleteAsync(id, ct);
    }

}
