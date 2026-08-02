using System.Text.Json;
using Api.Constants;
using Api.Models;
using Api.Repositories;

namespace Api.Services;

/// <summary>
/// Manages resource types and their custom field definitions. System types (seeded by
/// migration 1300: space, person, tool) are protected: their identity and lifecycle are
/// immutable through the API, but they may still gain custom field definitions.
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

        if (existing.IsSystem)
            throw new ArgumentException($"System resource type '{existing.Key}' cannot be modified");

        return await typeRepository.UpdateAsync(id, request, ct);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var existing = await typeRepository.GetByIdAsync(id, ct);
        if (existing is null) return false;

        if (existing.IsSystem)
            throw new ArgumentException($"System resource type '{existing.Key}' cannot be deleted");

        // Deleting a populated type would orphan its resources, so retire it instead.
        if (await typeRepository.CountResourcesAsync(id, ct) > 0)
        {
            await typeRepository.UpdateAsync(id, new UpdateResourceTypeRequest { IsActive = false }, ct);
            return true;
        }

        return await typeRepository.DeleteAsync(id, ct);
    }

}
