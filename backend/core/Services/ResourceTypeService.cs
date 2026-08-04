using System.Text.Json;
using Api.Constants;
using Api.Models;
using Api.Repositories;

namespace Api.Services;

/// <summary>
/// Manages resource types. System types (seeded by
/// migration 1300: space, person, tool) are protected: their identity and lifecycle are
/// immutable through the API, but criteria can still be made applicable to them.
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

        // A system type's naming is the tenant's to change — "Space" may be "Room" or "Salle"
        // in their vocabulary, and 1750 added the plural precisely so those labels are theirs.
        // Its behaviour is not: the product's own Spaces and People pages are built on
        // has_geometry and has_directory_profile, so flipping those would break pages the
        // tenant cannot repair. Same for the key (URLs, stored data) and is_active.
        if (existing.IsSystem)
        {
            if (request.IsActive.HasValue)
                throw new ArgumentException(
                    $"System resource type '{existing.Key}' cannot be deactivated — the product's own pages depend on it");

            if (request.HasGeometry.HasValue || request.HasDirectoryProfile.HasValue
                || request.SingleGroupMembership.HasValue)
            {
                throw new ArgumentException(
                    $"System resource type '{existing.Key}' has fixed behaviour; only its name, "
                    + "description and icon can be changed");
            }
        }

        return await typeRepository.UpdateAsync(id, request, ct);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var existing = await typeRepository.GetByIdAsync(id, ct);
        if (existing is null) return false;

        if (existing.IsSystem)
            throw new ArgumentException($"System resource type '{existing.Key}' cannot be deleted");

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
