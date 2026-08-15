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
    IResourceTypeRepository resourceTypeRepository,
    IListDefinitionRepository listDefinitionRepository,
    IListInstanceRepository listInstanceRepository) : IResourceCustomFieldService
{
    /// <summary>
    /// How many rows one lookup value may pick. Bounds a payload that is otherwise unbounded — the
    /// value rides inside the resource document and is read with every resource.
    /// </summary>
    public const int MaxPickedRows = 100;

    /// <summary>
    /// Rejects a required field on the one type whose resources cannot carry one: the built-in
    /// space. Spaces are created by POST /api/sites/{id}/spaces, whose request has no
    /// custom-field document at all, so a required field there would make every space
    /// uncreatable through the only endpoint that creates one.
    ///
    /// Half of foundation#110 has since landed: CreateSpaceRequest and UpdateSpaceRequest carry
    /// values, and EditSpaceDialog renders the fields. The guard stays because that is the *edit*
    /// path — a space is created by drawing it on the floorplan, and that flow still sends no
    /// values, so a required field would still make new spaces unsaveable. Lifting this needs the
    /// drawing flow to carry them too; a test that made a space field required proved the point
    /// by breaking every later test that created a space.
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

    /// <summary>
    /// A list field is defined by what it points at, so the binding is checked here rather than
    /// left to the CHECK constraint: the constraint knows a binding is missing, this knows which
    /// one and whether it names something that exists and is still open for use.
    /// </summary>
    /// <summary>
    /// A lookup value is the set of rows the resource picked out of one shared instance: an array
    /// of row ids, each of which has to still exist in that instance.
    /// </summary>
    private async Task ValidateLookupAsync(ResourceCustomFieldInfo field, JsonElement value, CancellationToken ct)
    {
        if (value.ValueKind != JsonValueKind.Array)
            throw new ArgumentException($"Custom field '{field.Label}' expects a list of selected rows");

        if (value.GetArrayLength() > MaxPickedRows)
            throw new ArgumentException($"Custom field '{field.Label}' accepts at most {MaxPickedRows} selected rows");

        var ids = new List<Guid>(value.GetArrayLength());
        foreach (var element in value.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.String || !Guid.TryParse(element.GetString(), out var id))
                throw new ArgumentException($"Custom field '{field.Label}' expects row ids");

            ids.Add(id);
        }

        if (ids.Distinct().Count() != ids.Count)
            throw new ArgumentException($"Custom field '{field.Label}' has the same row selected twice");

        // One batched existence check rather than a query per id: a hundred picked rows would
        // otherwise be a hundred round trips on every resource save.
        var instanceId = field.ListInstanceId!.Value;
        if (await listInstanceRepository.CountExistingRowsAsync(instanceId, ids, ct) != ids.Count)
            throw new ArgumentException($"Custom field '{field.Label}' selects a row that no longer exists");
    }

    private async Task EnsureBindingAsync(CreateResourceCustomFieldRequest request, CancellationToken ct)
    {
        switch (request.DataType)
        {
            case CustomFieldDataTypes.List:
                // Rows attach after the resource exists, so there is no value for a create form to
                // carry and nothing a required flag could demand. Rejected rather than ignored,
                // because silently dropping it would look like it had been honoured.
                if (request.IsRequired)
                    throw new ArgumentException("A list field cannot be required — its rows are added after the resource is created");

                if (request.ListInstanceId is not null)
                    throw new ArgumentException("A list field binds a list definition, not an instance");

                if (request.ListDefinitionId is not { } definitionId)
                    throw new ArgumentException("A list field needs a list definition");

                var definition = await listDefinitionRepository.GetByIdAsync(definitionId, ct)
                    ?? throw new ArgumentException("The list definition does not exist");

                if (!definition.IsActive)
                    throw new ArgumentException($"List definition '{definition.Name}' is inactive, so no new field can bind it");
                break;

            case CustomFieldDataTypes.ListLookup:
                if (request.ListDefinitionId is not null)
                    throw new ArgumentException("A lookup field binds a shared list instance, not a definition");

                if (request.ListInstanceId is not { } instanceId)
                    throw new ArgumentException("A lookup field needs a shared list instance");

                var instance = await listInstanceRepository.GetByIdAsync(instanceId, ct)
                    ?? throw new ArgumentException("The list instance does not exist");

                if (instance.Kind != ListInstanceKinds.Shared)
                    throw new ArgumentException("A lookup field can only bind a shared list instance");
                break;

            default:
                // A scalar field carrying a binding is a request that half-means something else.
                if (request.ListDefinitionId is not null || request.ListInstanceId is not null)
                    throw new ArgumentException($"A '{request.DataType}' field does not bind a list");
                break;
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
        await EnsureBindingAsync(request, ct);

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

            // A list field's rows live in their own instance, addressed by (resource, field). It
            // holds no value here at all, so a document carrying one is writing into a slot that
            // does not exist — and a whole-document replace would then look like it had cleared
            // rows it never touched.
            if (field.DataType == CustomFieldDataTypes.List)
                throw new ArgumentException($"Custom field '{field.Label}' holds its rows separately and takes no value here");

            // An empty value is an unfilled optional field, whatever its type. Whether it is
            // allowed to be empty is the required check below, not a type question.
            if (CustomFieldValueRules.IsEmpty(value)) continue;

            if (field.DataType == CustomFieldDataTypes.ListLookup)
            {
                await ValidateLookupAsync(field, value, ct);
                continue;
            }

            CustomFieldValueRules.Validate($"Custom field '{field.Label}'", field.DataType, value);
        }

        // Only active fields can be required: a field retired while resources still lack a value
        // for it must not make those resources unsaveable.
        // List fields are never required (EnsureBindingAsync rejects it), so they cannot appear
        // here — but skipping them explicitly keeps that from depending on a rule enforced elsewhere.
        foreach (var field in definitions.Where(f =>
                     f is { IsActive: true, IsRequired: true } && f.DataType != CustomFieldDataTypes.List))
        {
            var present = values.TryGetValue(field.Key, out var value)
                          && !CustomFieldValueRules.IsEmpty(value)
                          // An empty array is an unfilled lookup, the same as an empty string is an
                          // unfilled text field.
                          && !(value.ValueKind == JsonValueKind.Array && value.GetArrayLength() == 0);

            if (!present) throw new ArgumentException($"Custom field '{field.Label}' is required");
        }
    }

}
