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

    Task<List<ResourceTypeFieldInfo>> GetFieldsAsync(Guid resourceTypeId, bool includeInactive = false, CancellationToken ct = default);
    Task<ResourceTypeFieldInfo> AddFieldAsync(Guid resourceTypeId, CreateResourceTypeFieldRequest request, CancellationToken ct = default);
    Task<ResourceTypeFieldInfo?> UpdateFieldAsync(Guid resourceTypeId, Guid fieldId, UpdateResourceTypeFieldRequest request, CancellationToken ct = default);
    Task<bool> DeactivateFieldAsync(Guid resourceTypeId, Guid fieldId, CancellationToken ct = default);
}

public class ResourceTypeService(
    IResourceTypeRepository typeRepository,
    IResourceTypeFieldRepository fieldRepository) : IResourceTypeService
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

    public async Task<List<ResourceTypeFieldInfo>> GetFieldsAsync(
        Guid resourceTypeId, bool includeInactive = false, CancellationToken ct = default)
    {
        await RequireTypeAsync(resourceTypeId, ct);
        return await fieldRepository.GetByTypeAsync(resourceTypeId, includeInactive, ct);
    }

    public async Task<ResourceTypeFieldInfo> AddFieldAsync(
        Guid resourceTypeId, CreateResourceTypeFieldRequest request, CancellationToken ct = default)
    {
        // Field definitions are allowed on system types too — `tool` in particular has no
        // profile side-table, so custom fields are its only per-type data.
        await RequireTypeAsync(resourceTypeId, ct);

        var existing = await fieldRepository.GetByTypeAsync(resourceTypeId, includeInactive: true, ct);
        if (existing.Any(f => string.Equals(f.Key, request.Key, StringComparison.Ordinal)))
            throw new ArgumentException($"Field '{request.Key}' already exists on this resource type");

        if (request.DataType == ResourceFieldDataTypes.Select && !HasSelectOptions(request.Options))
            throw new ArgumentException("A select field requires at least one option");

        return await fieldRepository.CreateAsync(resourceTypeId, request, ct);
    }

    public async Task<ResourceTypeFieldInfo?> UpdateFieldAsync(
        Guid resourceTypeId, Guid fieldId, UpdateResourceTypeFieldRequest request, CancellationToken ct = default)
    {
        var field = await RequireFieldAsync(resourceTypeId, fieldId, ct);
        if (field is null) return null;

        if (field.DataType == ResourceFieldDataTypes.Select
            && request.Options.HasValue
            && !HasSelectOptions(request.Options))
            throw new ArgumentException("A select field requires at least one option");

        return await fieldRepository.UpdateAsync(fieldId, request, ct);
    }

    public async Task<bool> DeactivateFieldAsync(Guid resourceTypeId, Guid fieldId, CancellationToken ct = default)
    {
        var field = await RequireFieldAsync(resourceTypeId, fieldId, ct);
        if (field is null) return false;

        return await fieldRepository.DeactivateAsync(fieldId, ct);
    }

    private async Task<ResourceTypeInfo> RequireTypeAsync(Guid resourceTypeId, CancellationToken ct)
        => await typeRepository.GetByIdAsync(resourceTypeId, ct)
           ?? throw new KeyNotFoundException($"ResourceType {resourceTypeId} not found");

    /// <summary>Returns the field when it exists and belongs to the type, else null.</summary>
    private async Task<ResourceTypeFieldInfo?> RequireFieldAsync(Guid resourceTypeId, Guid fieldId, CancellationToken ct)
    {
        await RequireTypeAsync(resourceTypeId, ct);
        var field = await fieldRepository.GetByIdAsync(fieldId, ct);
        return field is null || field.ResourceTypeId != resourceTypeId ? null : field;
    }

    private static bool HasSelectOptions(JsonElement? options)
    {
        if (options is not { } o
            || o.ValueKind != JsonValueKind.Object
            || !o.TryGetProperty("values", out var values)
            || values.ValueKind != JsonValueKind.Array)
            return false;

        return values.EnumerateArray().Any(v => v.ValueKind == JsonValueKind.String);
    }
}
