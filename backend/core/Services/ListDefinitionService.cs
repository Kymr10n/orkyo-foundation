using Api.Models;
using Api.Repositories;

namespace Api.Services;

/// <summary>
/// Governance over list shapes: what columns a list has, what they hold, and which shared
/// instances exist. Reshaping a list changes what every editor must fill in, so this whole
/// surface is admin-write — the same line resource types draw. Row data is the other side of
/// that line and lives in <see cref="IListRowService"/>.
/// </summary>
public interface IListDefinitionService
{
    Task<List<ListDefinitionInfo>> GetAllAsync(bool includeInactive = false, string? scope = null, CancellationToken ct = default);
    Task<ListDefinitionInfo?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<ListDefinitionInfo> CreateAsync(CreateListDefinitionRequest request, CancellationToken ct = default);
    Task<ListDefinitionInfo?> UpdateAsync(Guid id, UpdateListDefinitionRequest request, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>Adds a column, or returns null when the definition does not exist.</summary>
    Task<ListColumnInfo?> CreateColumnAsync(Guid definitionId, CreateListColumnRequest request, CancellationToken ct = default);
    Task<ListColumnInfo?> UpdateColumnAsync(Guid definitionId, Guid columnId, UpdateListColumnRequest request, CancellationToken ct = default);
    Task<bool> DeleteColumnAsync(Guid definitionId, Guid columnId, CancellationToken ct = default);

    Task<List<ListInstanceInfo>?> GetSharedInstancesAsync(Guid definitionId, CancellationToken ct = default);
    Task<ListInstanceInfo?> CreateSharedInstanceAsync(Guid definitionId, CreateListInstanceRequest request, CancellationToken ct = default);
    Task<ListInstanceInfo?> UpdateSharedInstanceAsync(Guid definitionId, Guid instanceId, UpdateListInstanceRequest request, CancellationToken ct = default);
    Task<bool> DeleteSharedInstanceAsync(Guid definitionId, Guid instanceId, CancellationToken ct = default);
}

public class ListDefinitionService(
    IListDefinitionRepository repository,
    IListInstanceRepository instanceRepository,
    IResourceTypeRepository resourceTypeRepository) : IListDefinitionService
{
    public Task<List<ListDefinitionInfo>> GetAllAsync(bool includeInactive = false, string? scope = null, CancellationToken ct = default)
        => repository.GetAllAsync(includeInactive, scope, ct);

    public Task<ListDefinitionInfo?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => repository.GetByIdAsync(id, ct);

    public async Task<ListDefinitionInfo> CreateAsync(CreateListDefinitionRequest request, CancellationToken ct = default)
    {
        // Shape is the validator's job (CreateListDefinitionRequestValidator); existence is not,
        // because it needs the database.
        if (request.ResourceTypeId is { } typeId
            && await resourceTypeRepository.GetByIdAsync(typeId, ct) is null)
        {
            throw new ArgumentException("Resource type not found");
        }

        return await repository.CreateAsync(request, ct);
    }

    public async Task<ListDefinitionInfo?> UpdateAsync(
        Guid id, UpdateListDefinitionRequest request, CancellationToken ct = default)
    {
        // A display column from another definition would name a cell these rows do not have. The
        // FK cannot express "belongs to this definition", so it is checked here.
        if (!request.ClearDisplayColumn && request.DisplayColumnId is { } columnId)
        {
            var column = await repository.GetColumnAsync(columnId, ct);
            if (column is null || column.ListDefinitionId != id)
                throw new ArgumentException("The display column must be a column of this list definition");
        }

        return await repository.UpdateAsync(id, request, ct);
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
        => repository.DeleteAsync(id, ct);

    public async Task<ListColumnInfo?> CreateColumnAsync(
        Guid definitionId, CreateListColumnRequest request, CancellationToken ct = default)
    {
        if (await repository.GetByIdAsync(definitionId, ct) is null) return null;

        EnsureSelectHasOptions(request.DataType, request.Options);

        return await repository.CreateColumnAsync(definitionId, request, ct);
    }

    public async Task<ListColumnInfo?> UpdateColumnAsync(
        Guid definitionId, Guid columnId, UpdateListColumnRequest request, CancellationToken ct = default)
    {
        var existing = await repository.GetColumnAsync(columnId, ct);
        if (existing is null || existing.ListDefinitionId != definitionId) return null;

        // Options belong to select and nowhere else. The request validator cannot check this — it
        // sees no data type, because the type is immutable and therefore absent from the update.
        if (request.Options is not null && existing.DataType != ListColumnDataTypes.Select)
            throw new ArgumentException($"Column '{existing.Label}' is not a select column, so it has no options");

        if (existing.DataType == ListColumnDataTypes.Select && request.Options is not null)
            EnsureSelectHasOptions(existing.DataType, request.Options);

        return await repository.UpdateColumnAsync(columnId, request, ct);
    }

    public async Task<bool> DeleteColumnAsync(Guid definitionId, Guid columnId, CancellationToken ct = default)
    {
        var existing = await repository.GetColumnAsync(columnId, ct);
        if (existing is null || existing.ListDefinitionId != definitionId) return false;

        return await repository.DeleteColumnAsync(columnId, ct);
    }

    public async Task<List<ListInstanceInfo>?> GetSharedInstancesAsync(Guid definitionId, CancellationToken ct = default)
    {
        if (await repository.GetByIdAsync(definitionId, ct) is null) return null;
        return await instanceRepository.GetSharedByDefinitionAsync(definitionId, ct);
    }

    public async Task<ListInstanceInfo?> CreateSharedInstanceAsync(
        Guid definitionId, CreateListInstanceRequest request, CancellationToken ct = default)
    {
        var definition = await repository.GetByIdAsync(definitionId, ct);
        if (definition is null) return null;

        // A retired definition can still hold data, but it stops accepting new holders of that
        // shape — the same reason an inactive field disappears from the form.
        if (!definition.IsActive)
            throw new ArgumentException($"List definition '{definition.Name}' is inactive, so no new instance can be created from it");

        return await instanceRepository.CreateSharedAsync(definitionId, request, ct);
    }

    public async Task<ListInstanceInfo?> UpdateSharedInstanceAsync(
        Guid definitionId, Guid instanceId, UpdateListInstanceRequest request, CancellationToken ct = default)
    {
        var existing = await instanceRepository.GetByIdAsync(instanceId, ct);
        if (!IsSharedInstanceOf(existing, definitionId)) return null;

        return await instanceRepository.UpdateSharedAsync(instanceId, request, ct);
    }

    public async Task<bool> DeleteSharedInstanceAsync(Guid definitionId, Guid instanceId, CancellationToken ct = default)
    {
        var existing = await instanceRepository.GetByIdAsync(instanceId, ct);
        if (!IsSharedInstanceOf(existing, definitionId)) return false;

        return await instanceRepository.DeleteSharedAsync(instanceId, ct);
    }

    /// <summary>
    /// A shared instance is addressed through its definition, so one addressed through the wrong
    /// definition — or a per-resource instance addressed here at all — is a 404, not someone
    /// else's row.
    /// </summary>
    private static bool IsSharedInstanceOf(ListInstanceInfo? instance, Guid definitionId) =>
        instance is not null
        && instance.ListDefinitionId == definitionId
        && instance.Kind == ListInstanceKinds.Shared;

    /// <summary>
    /// A select column with no options is a menu with nothing in it — every value would be
    /// rejected, so the column could never be filled in. Rejected at definition time rather than
    /// discovered on the first row.
    /// </summary>
    private static void EnsureSelectHasOptions(string dataType, IReadOnlyList<string>? options)
    {
        if (dataType != ListColumnDataTypes.Select) return;

        if (options is null || options.Count == 0)
            throw new ArgumentException("A select column needs at least one option");

        if (options.Distinct(StringComparer.Ordinal).Count() != options.Count)
            throw new ArgumentException("Select options must be unique");
    }
}
