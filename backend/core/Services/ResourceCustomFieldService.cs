using System.Globalization;
using System.Text.Json;
using Api.Models;
using Api.Repositories;

namespace Api.Services;

/// <summary>
/// Manages the descriptive fields a tenant defines on a resource type, and validates the values
/// resources carry for them. Definitions are governance (Admin); values are ordinary resource
/// editing (Editor) — the endpoints draw that line, this service serves both sides of it.
/// </summary>
public interface IResourceCustomFieldService
{
    /// <summary>Every definition for a type, in form order — or null when the type is unknown.</summary>
    Task<List<ResourceCustomFieldInfo>?> GetByResourceTypeAsync(Guid resourceTypeId, CancellationToken ct = default);
    /// <summary>One definition, or null when it does not exist or belongs to another type.</summary>
    Task<ResourceCustomFieldInfo?> GetByIdAsync(Guid resourceTypeId, Guid fieldId, CancellationToken ct = default);
    /// <summary>Defines a field on the type, or returns null when the type is unknown.</summary>
    Task<ResourceCustomFieldInfo?> CreateAsync(Guid resourceTypeId, CreateResourceCustomFieldRequest request, CancellationToken ct = default);
    Task<ResourceCustomFieldInfo?> UpdateAsync(Guid resourceTypeId, Guid fieldId, UpdateResourceCustomFieldRequest request, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid resourceTypeId, Guid fieldId, CancellationToken ct = default);

    /// <summary>
    /// Checks a resource's custom-field values against the definitions for its type. Throws
    /// <see cref="ArgumentException"/> — a 400 — on an unknown key, a value of the wrong shape,
    /// or a missing required field.
    /// </summary>
    /// <param name="values">
    /// The complete value document being written — it replaces what the resource holds, so a
    /// required field missing from it is a required field missing from the resource. Callers
    /// that are not writing custom fields at all skip this call rather than passing null.
    /// </param>
    /// <remarks>
    /// What counts as "no value", which a client has to mirror to pre-empt a rejection: JSON
    /// null, and a string that is empty or only whitespace. <c>false</c> and <c>0</c> are
    /// values — a required checkbox is satisfied by answering "no".
    /// </remarks>
    Task ValidateValuesAsync(Guid resourceTypeId, IReadOnlyDictionary<string, JsonElement> values, CancellationToken ct = default);
}

public class ResourceCustomFieldService(
    IResourceCustomFieldRepository repository,
    IResourceTypeRepository resourceTypeRepository) : IResourceCustomFieldService
{
    /// <summary>
    /// Rejects a required field on the one type whose resources cannot carry one: the built-in
    /// space. Spaces are created by POST /api/sites/{id}/spaces, whose request has no
    /// custom-field document at all, so a required field there would make every space
    /// uncreatable through the only endpoint that creates one. Lifting this is tracked by
    /// foundation#110, which teaches that endpoint to carry values.
    /// </summary>
    /// <remarks>
    /// Scoped to the system space type on purpose, not to placeable types in general: a
    /// tenant-defined placeable type is created through /api/resources like everything else,
    /// and that carries values fine. Nor is it extended to a type some *dialog* happens not to
    /// ask on — that is a gap in the client, and the API would be the wrong place to encode it.
    ///
    /// The check runs on create and update rather than being a true invariant, but the one type
    /// it covers cannot escape it: a system type's behaviour flags are immutable
    /// (<see cref="ResourceTypeService"/>), so `space` can never stop being placeable, and no
    /// other type can become the space endpoint's target.
    /// </remarks>
    private static void EnsureRequirable(ResourceTypeInfo resourceType, bool isRequired)
    {
        if (!isRequired) return;

        if (resourceType is { IsSystem: true, HasGeometry: true })
        {
            throw new ArgumentException(
                $"'{resourceType.DisplayName}' resources are created from the floorplan, on a form "
                + "that does not ask for custom fields, so a field on this type cannot be required. "
                + "Add it as optional instead.");
        }
    }

    public async Task<List<ResourceCustomFieldInfo>?> GetByResourceTypeAsync(
        Guid resourceTypeId, CancellationToken ct = default)
    {
        if (await resourceTypeRepository.GetByIdAsync(resourceTypeId, ct) is null) return null;
        return await repository.GetByResourceTypeAsync(resourceTypeId, ct);
    }

    public async Task<ResourceCustomFieldInfo?> GetByIdAsync(
        Guid resourceTypeId, Guid fieldId, CancellationToken ct = default)
    {
        var field = await repository.GetByIdAsync(fieldId, ct);
        return field?.ResourceTypeId == resourceTypeId ? field : null;
    }

    public async Task<ResourceCustomFieldInfo?> CreateAsync(
        Guid resourceTypeId, CreateResourceCustomFieldRequest request, CancellationToken ct = default)
    {
        var resourceType = await resourceTypeRepository.GetByIdAsync(resourceTypeId, ct);
        if (resourceType is null) return null;

        EnsureRequirable(resourceType, request.IsRequired);

        // The duplicate key is caught by the unique constraint rather than a read-then-insert,
        // which two concurrent creates would slip through anyway.
        return await repository.CreateAsync(resourceTypeId, request, ct);
    }

    public async Task<ResourceCustomFieldInfo?> UpdateAsync(
        Guid resourceTypeId, Guid fieldId, UpdateResourceCustomFieldRequest request, CancellationToken ct = default)
    {
        var existing = await repository.GetByIdAsync(fieldId, ct);
        if (existing is null || existing.ResourceTypeId != resourceTypeId) return null;

        if (request.IsRequired == true
            && await resourceTypeRepository.GetByIdAsync(resourceTypeId, ct) is { } resourceType)
        {
            EnsureRequirable(resourceType, isRequired: true);
        }

        return await repository.UpdateAsync(fieldId, request, ct);
    }

    public async Task<bool> DeleteAsync(Guid resourceTypeId, Guid fieldId, CancellationToken ct = default)
    {
        var existing = await repository.GetByIdAsync(fieldId, ct);
        if (existing is null || existing.ResourceTypeId != resourceTypeId) return false;

        return await repository.DeleteAsync(fieldId, ct);
    }

    public async Task ValidateValuesAsync(
        Guid resourceTypeId, IReadOnlyDictionary<string, JsonElement> values, CancellationToken ct = default)
    {
        var definitions = await repository.GetByResourceTypeAsync(resourceTypeId, ct);
        var byKey = definitions.ToDictionary(f => f.Key, StringComparer.Ordinal);

        foreach (var (key, value) in values)
        {
            if (!byKey.TryGetValue(key, out var field))
                throw new ArgumentException($"'{key}' is not a custom field of this resource type");

            // An empty value is an unfilled optional field, whatever its type. Whether it is
            // allowed to be empty is the required check below, not a type question.
            if (CustomFieldValueRules.IsEmpty(value)) continue;

            CustomFieldValueRules.Validate($"Custom field '{field.Label}'", field.DataType, value);
        }

        // Only active fields can be required: a field retired while resources still lack a value
        // for it must not make those resources unsaveable.
        foreach (var field in definitions.Where(f => f is { IsActive: true, IsRequired: true }))
        {
            if (!values.TryGetValue(field.Key, out var value) || CustomFieldValueRules.IsEmpty(value))
                throw new ArgumentException($"Custom field '{field.Label}' is required");
        }
    }

}
