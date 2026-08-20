using Api.Constants;
using Api.Models;
using Api.Repositories;

namespace Api.Services;

/// <summary>
/// The switches under Configuration → Type catalog. Activation instantiates a catalog entry as
/// an ordinary, fully editable tenant type — or adopts the tenant's existing row of the same
/// key, which is how types from the built-in era appear as already activated. Deactivation is
/// the reversible off; purge is the destructive one.
/// </summary>
public interface IResourceTypeCatalogService
{
    Task<List<CatalogEntryInfo>> GetCatalogAsync(CancellationToken ct = default);

    /// <summary>
    /// Activates an entry: adopts the existing row by key (reactivating it, leaving the
    /// tenant's names and flags alone) or creates one from the spec, then adds whichever of
    /// the entry's fields the type does not have yet. Idempotent.
    /// Throws <see cref="ArgumentException"/> for a key the catalog does not know.
    /// </summary>
    Task<ResourceTypeInfo> ActivateAsync(string key, CancellationToken ct = default);

    /// <summary>Deactivates the entry's type, keeping every row of data. False when no row exists.</summary>
    Task<bool> DeactivateAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// Deletes the entry's type together with its resources, their assignments, its groups and
    /// request targets. Null when no row exists.
    /// </summary>
    Task<ResourceTypePurgeResult?> PurgeAsync(string key, CancellationToken ct = default);
}

public class ResourceTypeCatalogService(
    IResourceTypeRepository typeRepository,
    IResourceCustomFieldService customFieldService,
    IListDefinitionRepository listDefinitionRepository,
    IListInstanceRepository listInstanceRepository) : IResourceTypeCatalogService
{
    public async Task<List<CatalogEntryInfo>> GetCatalogAsync(CancellationToken ct = default)
    {
        var types = await typeRepository.GetAllAsync(ct);
        var byKey = types.ToDictionary(t => t.Key, StringComparer.Ordinal);
        var usage = await typeRepository.GetUsageCountsAsync(ct);

        return ResourceTypeCatalog.Types.Select(spec =>
        {
            byKey.TryGetValue(spec.Key, out var row);
            var counts = row is not null ? usage.GetValueOrDefault(row.Id) : null;
            return new CatalogEntryInfo
            {
                Key = spec.Key,
                DisplayName = spec.DisplayName,
                DisplayNamePlural = spec.DisplayNamePlural,
                Description = spec.Description,
                Icon = spec.Icon,
                Category = spec.Category,
                HasGeometry = spec.HasGeometry,
                HasDirectoryProfile = spec.HasDirectoryProfile,
                SingleGroupMembership = spec.SingleGroupMembership,
                FieldLabels = spec.Fields.OrderBy(f => f.SortOrder).Select(f => f.Label).ToList(),
                State = row is null
                    ? CatalogEntryStates.Absent
                    : row.IsActive ? CatalogEntryStates.Active : CatalogEntryStates.Inactive,
                ResourceTypeId = row?.Id,
                TenantDisplayName = row?.DisplayName,
                ResourceCount = counts?.Resources ?? 0,
                RequestTargetCount = counts?.RequestTargets ?? 0,
            };
        }).ToList();
    }

    public async Task<ResourceTypeInfo> ActivateAsync(string key, CancellationToken ct = default)
    {
        var spec = ResourceTypeCatalog.Find(key)
            ?? throw new ArgumentException($"'{key}' is not a catalog resource type");

        var type = await typeRepository.GetByKeyAsync(key, ct);
        if (type is null)
        {
            try
            {
                type = await typeRepository.CreateAsync(new CreateResourceTypeRequest
                {
                    Key = spec.Key,
                    DisplayName = spec.DisplayName,
                    DisplayNamePlural = spec.DisplayNamePlural,
                    Description = spec.Description,
                    Icon = spec.Icon,
                    HasGeometry = spec.HasGeometry,
                    HasDirectoryProfile = spec.HasDirectoryProfile,
                    SingleGroupMembership = spec.SingleGroupMembership,
                }, ct);
            }
            catch (Npgsql.PostgresException pg) when (pg.SqlState == Npgsql.PostgresErrorCodes.UniqueViolation)
            {
                // Two switches flipped at once: the other call created the row — adopt it.
                type = (await typeRepository.GetByKeyAsync(key, ct))!;
            }
        }
        if (!type.IsActive)
        {
            // Adoption reactivates and changes nothing else: the row is the tenant's, renames
            // and flag edits included.
            type = (await typeRepository.UpdateAsync(type.Id,
                new UpdateResourceTypeRequest { IsActive = true }, ct))!;
        }

        // Add only the fields the type does not have yet, so re-activation never duplicates.
        // A field the tenant deleted does come back: a missing field is indistinguishable
        // from a never-created one, and shipping the advertised set wins that tie.
        var existing = await customFieldService.GetByResourceTypeAsync(type.Id, ct) ?? [];
        var existingKeys = existing.Select(f => f.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var field in spec.Fields.Where(f => !existingKeys.Contains(f.Key)))
        {
            await customFieldService.CreateAsync(type.Id, new CreateResourceCustomFieldRequest
            {
                Key = field.Key,
                Label = field.Label,
                Description = field.Description,
                DataType = field.DataType,
                IsRequired = false,
                SortOrder = field.SortOrder,
            }, ct);
        }

        if (spec.HasDirectoryProfile)
            await EnsureDirectoryLookupFieldsAsync(type.Id, existingKeys, ct);

        return type;
    }

    public async Task<bool> DeactivateAsync(string key, CancellationToken ct = default)
    {
        if (ResourceTypeCatalog.Find(key) is null)
            throw new ArgumentException($"'{key}' is not a catalog resource type");

        var type = await typeRepository.GetByKeyAsync(key, ct);
        if (type is null) return false;

        await typeRepository.UpdateAsync(type.Id, new UpdateResourceTypeRequest { IsActive = false }, ct);
        return true;
    }

    public async Task<ResourceTypePurgeResult?> PurgeAsync(string key, CancellationToken ct = default)
    {
        if (ResourceTypeCatalog.Find(key) is null)
            throw new ArgumentException($"'{key}' is not a catalog resource type");

        var type = await typeRepository.GetByKeyAsync(key, ct);
        if (type is null) return null;

        return await typeRepository.PurgeAsync(type.Id, ct);
    }

    /// <summary>
    /// The two lookup fields migration 1820 gave the directory type — department and
    /// job_title — bound to the organization lists it created. A directory type activated
    /// later (1820 found no such type, or 1880 removed it) gets them here, resolved by the
    /// same identity 1820 used. A tenant who renamed or deleted those lists gets neither:
    /// there is nothing to bind, and the lists are theirs.
    /// </summary>
    private async Task EnsureDirectoryLookupFieldsAsync(
        Guid typeId, IReadOnlySet<string> existingFieldKeys, CancellationToken ct)
    {
        var wanted = new (string FieldKey, string Label, string ListName, int SortOrder)[]
        {
            ("department", "Department", "Departments", 100),
            ("job_title", "Job title", "Job Titles", 101),
        };
        if (wanted.All(w => existingFieldKeys.Contains(w.FieldKey))) return;

        var orgDefinitions = await listDefinitionRepository.GetAllAsync(
            includeInactive: false, scope: ListDefinitionScopes.Organization, ct: ct);
        var instancesByDefinition = await listInstanceRepository.GetSharedByDefinitionsAsync(
            orgDefinitions.Select(d => d.Id).ToList(), ct);

        foreach (var (fieldKey, label, listName, sortOrder) in wanted)
        {
            if (existingFieldKeys.Contains(fieldKey)) continue;

            var definition = orgDefinitions.FirstOrDefault(d =>
                string.Equals(d.Name, listName, StringComparison.Ordinal));
            if (definition is null) continue;
            var instance = instancesByDefinition.GetValueOrDefault(definition.Id)?
                .FirstOrDefault(i => string.Equals(i.Name, listName, StringComparison.Ordinal));
            if (instance is null) continue;

            await customFieldService.CreateAsync(typeId, new CreateResourceCustomFieldRequest
            {
                Key = fieldKey,
                Label = label,
                DataType = CustomFieldDataTypes.ListLookup,
                IsRequired = false,
                SortOrder = sortOrder,
                ListInstanceId = instance.Id,
            }, ct);
        }
    }
}
