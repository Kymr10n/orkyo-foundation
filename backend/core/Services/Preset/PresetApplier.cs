using System.Text.Json;
using Api.Constants;
using Api.Helpers;
using Api.Models;
using Api.Models.Preset;
using Api.Repositories;
using Npgsql;

namespace Api.Services;

/// <summary>
/// Shared, stateless preset-application logic that works against a raw
/// <see cref="NpgsqlConnection"/> + <see cref="NpgsqlTransaction"/>.
/// Used by both <see cref="PresetService"/> (HTTP-scoped) and
/// <see cref="StarterTemplateService"/> (provisioning-time, no TenantContext).
///
/// Every public method here is static so there is no hidden state —
/// callers own the connection lifetime.
///
/// Applying never deletes: the one DELETE in this file refreshes a template's own items.
/// A re-apply on a populated database therefore adds what is missing and leaves the
/// tenant's edits alone, which is what makes it safe to run at first start.
/// </summary>
public static class PresetApplier
{
    // ── Public entry point ─────────────────────────────────────────

    /// <summary>
    /// Applies a full preset (resource types + criteria + groups + templates + resources)
    /// inside the given transaction, in that dependency order.  Does NOT commit or
    /// rollback — the caller controls the transaction boundary.
    /// </summary>
    public static async Task<PresetApplicationStats> ApplyAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        Preset preset,
        Guid? userId = null)
    {
        var stats = new PresetApplicationStats();

        // Get or create preset application record
        var applicationId = await GetOrCreatePresetApplicationAsync(
            conn, tx, preset.PresetId, preset.Version, userId);

        // Load existing mappings for this preset
        var existingMappings = await GetExistingMappingsAsync(conn, tx, applicationId);

        var typeMap = await ApplyResourceTypesAsync(
            conn, tx, applicationId, preset.Contents.ResourceTypes, stats);

        var criterionIdMap = await ApplyCriteriaAsync(
            conn, tx, applicationId, preset.Contents.Criteria, existingMappings, typeMap, stats);

        var groupMap = await ApplySpaceGroupsAsync(
            conn, tx, applicationId, preset.Contents.SpaceGroups, existingMappings, typeMap, stats);

        await ApplyTemplatesAsync(
            conn, tx, applicationId, preset.Contents.Templates,
            existingMappings, criterionIdMap, stats);

        await ApplyResourcesAsync(
            conn, tx, applicationId, preset.Contents.Resources,
            existingMappings, typeMap, criterionIdMap, groupMap, stats);

        // Update application timestamp
        await UpdatePresetApplicationAsync(conn, tx, applicationId);

        return stats;
    }

    // ── Preset application records ─────────────────────────────────

    private static async Task<Guid> GetOrCreatePresetApplicationAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        string presetId, string version, Guid? userId)
    {
        await using var checkCmd = new NpgsqlCommand(
            "SELECT id FROM preset_applications WHERE preset_id = @presetId", conn, tx);
        checkCmd.Parameters.AddWithValue("presetId", presetId);

        var existingId = await checkCmd.ExecuteScalarAsync();
        if (existingId != null) return (Guid)existingId;

        await using var insertCmd = new NpgsqlCommand(@"
            INSERT INTO preset_applications (preset_id, preset_version, applied_by_user_id)
            VALUES (@presetId, @version, @userId)
            RETURNING id", conn, tx);
        insertCmd.Parameters.AddWithValue("presetId", presetId);
        insertCmd.Parameters.AddWithValue("version", version);
        insertCmd.Parameters.AddWithValue("userId", userId.HasValue ? userId.Value : DBNull.Value);

        return (Guid)(await insertCmd.ExecuteScalarAsync())!;
    }

    private static async Task UpdatePresetApplicationAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, Guid applicationId)
    {
        await using var cmd = new NpgsqlCommand(@"
            UPDATE preset_applications
            SET updated_at = CURRENT_TIMESTAMP
            WHERE id = @id", conn, tx);
        cmd.Parameters.AddWithValue("id", applicationId);
        await cmd.ExecuteNonQueryAsync();
    }

    // ── Mappings ───────────────────────────────────────────────────

    private static async Task<Dictionary<string, Guid>> GetExistingMappingsAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, Guid applicationId)
    {
        var mappings = new Dictionary<string, Guid>();
        await using var cmd = new NpgsqlCommand(@"
            SELECT entity_type, logical_key, entity_id
            FROM preset_mappings
            WHERE preset_application_id = @appId", conn, tx);
        cmd.Parameters.AddWithValue("appId", applicationId);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var entityType = reader.GetString("entity_type");
            var logicalKey = reader.GetString("logical_key");
            var entityId = reader.GetGuid("entity_id");
            mappings[$"{entityType}:{logicalKey}"] = entityId;
        }

        return mappings;
    }

    private static async Task SaveMappingAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        Guid applicationId, string entityType, string logicalKey, Guid entityId)
    {
        await using var cmd = new NpgsqlCommand(@"
            INSERT INTO preset_mappings (preset_application_id, entity_type, logical_key, entity_id)
            VALUES (@appId, @entityType, @logicalKey, @entityId)
            ON CONFLICT (preset_application_id, entity_type, logical_key)
            DO UPDATE SET entity_id = @entityId", conn, tx);
        cmd.Parameters.AddWithValue("appId", applicationId);
        cmd.Parameters.AddWithValue("entityType", entityType);
        cmd.Parameters.AddWithValue("logicalKey", logicalKey);
        cmd.Parameters.AddWithValue("entityId", entityId);
        await cmd.ExecuteNonQueryAsync();
    }

    // ── Resource types ─────────────────────────────────────────────

    /// <summary>The tenant's row for a preset type: its id and the flags the row actually carries.</summary>
    private sealed record TypeRef(Guid Id, bool HasGeometry, bool HasDirectoryProfile);

    /// <summary>
    /// Activates each type the preset names. A catalog key follows Configuration → Type catalog:
    /// the row is created from the catalog spec or adopted (reactivated, nothing else changed —
    /// the row is the tenant's, renames included), and the shipped custom fields the type lacks
    /// are added. An ad-hoc key does the same from the entry's own names and flags.
    /// The durable identity is the unique <c>resource_types.key</c>; the mapping row is kept for
    /// the application history.
    /// </summary>
    private static async Task<Dictionary<string, TypeRef>> ApplyResourceTypesAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        Guid applicationId, List<PresetResourceType> types, PresetApplicationStats stats)
    {
        var map = new Dictionary<string, TypeRef>(StringComparer.Ordinal);

        foreach (var type in types)
        {
            var spec = ResourceTypeCatalog.Find(type.Key);
            var typeRef = await UpsertResourceTypeAsync(conn, tx, type, spec);
            map[type.Key] = typeRef;
            await SaveMappingAsync(conn, tx, applicationId, "resource_type", type.Key, typeRef.Id);

            if (spec != null)
            {
                foreach (var field in spec.Fields)
                    await AddCatalogFieldAsync(conn, tx, typeRef.Id, field);
                if (spec.HasDirectoryProfile)
                    await AddDirectoryLookupFieldsAsync(conn, tx, typeRef.Id);
            }

            stats.ResourceTypesActivated++;
        }

        return map;
    }

    private static async Task<TypeRef> UpsertResourceTypeAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, PresetResourceType type, CatalogTypeSpec? spec)
    {
        // Same shape as ResourceTypeSeedHelpers.UpsertResourceTypeAsync and the catalog
        // activation: adoption reactivates and changes nothing else. RETURNING reads the row's
        // real flags, because an adopted row is the tenant's, edits included.
        await using var cmd = new NpgsqlCommand(@"
            INSERT INTO resource_types
                (key, display_name, display_name_plural, description, icon,
                 has_geometry, has_directory_profile, single_group_membership, scan_codes_enabled,
                 is_system, is_active)
            VALUES
                (@key, @displayName, @displayNamePlural, @description, @icon,
                 @hasGeometry, @hasDirectoryProfile, @singleGroupMembership, @scanCodesEnabled,
                 false, true)
            ON CONFLICT (key) DO UPDATE SET is_active = true, updated_at = CURRENT_TIMESTAMP
            RETURNING id, has_geometry, has_directory_profile", conn, tx);
        cmd.Parameters.AddWithValue("key", type.Key);
        cmd.Parameters.AddWithValue("displayName", spec?.DisplayName ?? type.DisplayName!);
        cmd.Parameters.AddWithValue("displayNamePlural", spec?.DisplayNamePlural ?? type.DisplayNamePlural!);
        cmd.Parameters.AddNullable("description", spec?.Description ?? type.Description);
        cmd.Parameters.AddWithValue("icon", spec?.Icon ?? type.Icon ?? "Box");
        var hasDirectoryProfile = spec?.HasDirectoryProfile ?? type.HasDirectoryProfile;
        cmd.Parameters.AddWithValue("hasGeometry", spec?.HasGeometry ?? type.HasGeometry);
        cmd.Parameters.AddWithValue("hasDirectoryProfile", hasDirectoryProfile);
        cmd.Parameters.AddWithValue("singleGroupMembership", spec?.SingleGroupMembership ?? type.SingleGroupMembership);
        // People rarely carry a sticker; the same default the catalog activation uses.
        cmd.Parameters.AddWithValue("scanCodesEnabled", !hasDirectoryProfile);

        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        return new TypeRef(
            reader.GetGuid("id"),
            reader.GetBoolean("has_geometry"),
            reader.GetBoolean("has_directory_profile"));
    }

    private static async Task AddCatalogFieldAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, Guid typeId, CatalogFieldSpec field)
    {
        // Only the fields the type does not have yet, so re-activation never duplicates.
        await using var cmd = new NpgsqlCommand(@"
            INSERT INTO resource_custom_fields
                (resource_type_id, key, label, description, data_type, is_required, sort_order)
            VALUES (@typeId, @key, @label, @description, @dataType, false, @sortOrder)
            ON CONFLICT (resource_type_id, key) DO NOTHING", conn, tx);
        cmd.Parameters.AddWithValue("typeId", typeId);
        cmd.Parameters.AddWithValue("key", field.Key);
        cmd.Parameters.AddWithValue("label", field.Label);
        cmd.Parameters.AddNullable("description", field.Description);
        cmd.Parameters.AddWithValue("dataType", field.DataType);
        cmd.Parameters.AddWithValue("sortOrder", field.SortOrder);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task AddDirectoryLookupFieldsAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, Guid typeId)
    {
        // The two lookup fields migration 1820 gave the directory type, bound to the
        // organization lists it created and resolved by the same identity
        // (ResourceTypeCatalogService.EnsureDirectoryLookupFieldsAsync). A tenant who renamed
        // or deleted those lists gets neither: the SELECT yields no row, and the lists are theirs.
        foreach (var (fieldKey, label, listName, sortOrder) in new[]
                 {
                     ("department", "Department", "Departments", 100),
                     ("job_title", "Job title", "Job Titles", 101),
                 })
        {
            await using var cmd = new NpgsqlCommand(@"
                INSERT INTO resource_custom_fields
                    (resource_type_id, key, label, data_type, is_required, sort_order, list_instance_id)
                SELECT @typeId, @key, @label, @dataType, false, @sortOrder, li.id
                FROM list_definitions ld
                JOIN list_instances li
                  ON li.list_definition_id = ld.id AND li.kind = @kind AND li.name = ld.name
                WHERE ld.scope = @scope AND ld.is_active AND ld.name = @listName
                LIMIT 1
                ON CONFLICT (resource_type_id, key) DO NOTHING", conn, tx);
            cmd.Parameters.AddWithValue("typeId", typeId);
            cmd.Parameters.AddWithValue("key", fieldKey);
            cmd.Parameters.AddWithValue("label", label);
            cmd.Parameters.AddWithValue("dataType", CustomFieldDataTypes.ListLookup);
            cmd.Parameters.AddWithValue("sortOrder", sortOrder);
            cmd.Parameters.AddWithValue("kind", ListInstanceKinds.Shared);
            cmd.Parameters.AddWithValue("scope", ListDefinitionScopes.Organization);
            cmd.Parameters.AddWithValue("listName", listName);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    // ── Criteria ───────────────────────────────────────────────────

    private static async Task<Dictionary<string, Guid>> ApplyCriteriaAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        Guid applicationId, List<PresetCriterion> criteria,
        Dictionary<string, Guid> existingMappings,
        Dictionary<string, TypeRef> typeMap,
        PresetApplicationStats stats)
    {
        var idMap = new Dictionary<string, Guid>();

        foreach (var criterion in criteria)
        {
            var mappingKey = $"criterion:{criterion.Key}";
            Guid criterionId;

            if (existingMappings.TryGetValue(mappingKey, out var existingId))
            {
                await UpdateCriterionAsync(conn, tx, existingId, criterion);
                criterionId = existingId;
                stats.CriteriaUpdated++;
            }
            else
            {
                var existingByName = await FindCriterionByNameAsync(conn, tx, criterion.Name);
                if (existingByName.HasValue)
                {
                    criterionId = existingByName.Value;
                    await SaveMappingAsync(conn, tx, applicationId, "criterion", criterion.Key, criterionId);
                    stats.CriteriaUpdated++;
                }
                else
                {
                    criterionId = await CreateCriterionAsync(conn, tx, criterion);
                    await SaveMappingAsync(conn, tx, applicationId, "criterion", criterion.Key, criterionId);
                    stats.CriteriaCreated++;
                }
            }

            idMap[criterion.Key] = criterionId;

            // Applicability is additive: a row the tenant added stays, one the preset names is
            // ensured. Created or adopted alike — an adopted criterion may have none yet.
            foreach (var typeKey in criterion.ResourceTypeKeys)
                await AddApplicabilityAsync(conn, tx, criterionId, typeMap[typeKey].Id);
        }

        return idMap;
    }

    private static async Task AddApplicabilityAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, Guid criterionId, Guid typeId)
    {
        await using var cmd = new NpgsqlCommand(@"
            INSERT INTO criterion_resource_types (criterion_id, resource_type_id)
            VALUES (@criterionId, @typeId)
            ON CONFLICT DO NOTHING", conn, tx);
        cmd.Parameters.AddWithValue("criterionId", criterionId);
        cmd.Parameters.AddWithValue("typeId", typeId);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<Guid?> FindCriterionByNameAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, string name)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT id FROM criteria WHERE LOWER(name) = LOWER(@name)", conn, tx);
        cmd.Parameters.AddWithValue("name", name);
        var result = await cmd.ExecuteScalarAsync();
        return result as Guid?;
    }

    private static async Task<Guid> CreateCriterionAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, PresetCriterion criterion)
    {
        await using var cmd = new NpgsqlCommand(@"
            INSERT INTO criteria (name, description, data_type, enum_values, unit)
            VALUES (@name, @description, @dataType, @enumValues::jsonb, @unit)
            RETURNING id", conn, tx);
        cmd.Parameters.AddWithValue("name", criterion.Name);
        cmd.Parameters.AddNullable("description", criterion.Description);
        cmd.Parameters.AddWithValue("dataType", criterion.DataType.ToString());
        cmd.Parameters.AddWithValue("enumValues",
            criterion.EnumValues != null ? JsonSerializer.Serialize(criterion.EnumValues) : DBNull.Value);
        cmd.Parameters.AddNullable("unit", criterion.Unit);

        return (Guid)(await cmd.ExecuteScalarAsync())!;
    }

    private static async Task UpdateCriterionAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, Guid id, PresetCriterion criterion)
    {
        await using var cmd = new NpgsqlCommand(@"
            UPDATE criteria
            SET description = @description,
                enum_values = @enumValues::jsonb,
                unit = @unit,
                updated_at = CURRENT_TIMESTAMP
            WHERE id = @id", conn, tx);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddNullable("description", criterion.Description);
        cmd.Parameters.AddWithValue("enumValues",
            criterion.EnumValues != null ? JsonSerializer.Serialize(criterion.EnumValues) : DBNull.Value);
        cmd.Parameters.AddNullable("unit", criterion.Unit);
        await cmd.ExecuteNonQueryAsync();
    }

    // ── Space Groups ───────────────────────────────────────────────

    /// <summary>A group the preset created or adopted: its id and the type it holds.</summary>
    private sealed record GroupRef(Guid Id, Guid TypeId);

    private static async Task<Dictionary<string, GroupRef>> ApplySpaceGroupsAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        Guid applicationId, List<PresetSpaceGroup> groups,
        Dictionary<string, Guid> existingMappings,
        Dictionary<string, TypeRef> typeMap,
        PresetApplicationStats stats)
    {
        var map = new Dictionary<string, GroupRef>(StringComparer.Ordinal);

        foreach (var group in groups)
        {
            var mappingKey = $"space_group:{group.Key}";
            var typeId = group.ResourceTypeKey != null ? typeMap[group.ResourceTypeKey].Id : (Guid?)null;
            Guid groupId;

            if (existingMappings.TryGetValue(mappingKey, out var existingId))
            {
                await UpdateSpaceGroupAsync(conn, tx, existingId, group);
                groupId = existingId;
                stats.SpaceGroupsUpdated++;
            }
            else
            {
                var existingByName = await FindSpaceGroupByNameAsync(conn, tx, group.Name, typeId);
                if (existingByName.HasValue)
                {
                    groupId = existingByName.Value;
                    await SaveMappingAsync(conn, tx, applicationId, "space_group", group.Key, groupId);
                    stats.SpaceGroupsUpdated++;
                }
                else
                {
                    groupId = await CreateSpaceGroupAsync(conn, tx, group, typeId);
                    await SaveMappingAsync(conn, tx, applicationId, "space_group", group.Key, groupId);
                    stats.SpaceGroupsCreated++;
                }
            }

            map[group.Key] = new GroupRef(groupId, await GetGroupTypeIdAsync(conn, tx, groupId));
        }

        return map;
    }

    private static async Task<Guid> GetGroupTypeIdAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, Guid groupId)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT resource_type_id FROM resource_groups WHERE id = @id", conn, tx);
        cmd.Parameters.AddWithValue("id", groupId);
        return (Guid)(await cmd.ExecuteScalarAsync())!;
    }

    private static async Task<Guid?> FindSpaceGroupByNameAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, string name, Guid? typeId)
    {
        // Scoped by type when the preset names one: a "Production" team of people and a
        // "Production" area of rooms are different groups.
        await using var cmd = new NpgsqlCommand(
            "SELECT id FROM resource_groups WHERE LOWER(name) = LOWER(@name) " +
            "AND (@typeId::uuid IS NULL OR resource_type_id = @typeId)", conn, tx);
        cmd.Parameters.AddWithValue("name", name);
        cmd.Parameters.AddNullable("typeId", typeId);
        var result = await cmd.ExecuteScalarAsync();
        return result as Guid?;
    }

    private static async Task<Guid> CreateSpaceGroupAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, PresetSpaceGroup group, Guid? typeId)
    {
        if (typeId.HasValue)
        {
            await using var typed = new NpgsqlCommand(@"
                INSERT INTO resource_groups (name, description, color, display_order, resource_type_id)
                VALUES (@name, @description, @color, @displayOrder, @typeId)
                RETURNING id", conn, tx);
            typed.Parameters.AddWithValue("name", group.Name);
            typed.Parameters.AddNullable("description", group.Description);
            typed.Parameters.AddNullable("color", group.Color);
            typed.Parameters.AddWithValue("displayOrder", group.DisplayOrder);
            typed.Parameters.AddWithValue("typeId", typeId.Value);
            return (Guid)(await typed.ExecuteScalarAsync())!;
        }

        // A 1.0.0 preset's groups are a curated floorplan layout, so they belong to a placeable
        // type. Preferring `space` keeps the historical meaning wherever that type still exists;
        // a tenant who deleted it gets their single remaining placeable type. Several placeable
        // types and no space is genuinely ambiguous, so the LIMIT resolves it deterministically
        // (space first, then key order) rather than failing an otherwise-applicable preset.
        await using var cmd = new NpgsqlCommand(@"
            INSERT INTO resource_groups (name, description, color, display_order, resource_type_id)
            SELECT @name, @description, @color, @displayOrder, id
            FROM resource_types
            WHERE has_geometry AND is_active
            ORDER BY (key = @spaceKey) DESC, key
            LIMIT 1
            RETURNING id", conn, tx);
        cmd.Parameters.AddWithValue("spaceKey", ResourceTypeKeys.Space);
        cmd.Parameters.AddWithValue("name", group.Name);
        cmd.Parameters.AddNullable("description", group.Description);
        cmd.Parameters.AddNullable("color", group.Color);
        cmd.Parameters.AddWithValue("displayOrder", group.DisplayOrder);
        // ArgumentException, not InvalidOperationException: a tenant with nothing activated yet is
        // an ordinary state since the built-in types became a catalog, and it is one the admin can
        // fix. The 500 the other shape maps to says neither. Same wording as RequestRepository,
        // which refuses the same state on the request path.
        return (Guid)(await cmd.ExecuteScalarAsync()
            ?? throw new ArgumentException(
                "No active placeable resource type exists. Activate one under Configuration "
                + "before applying a preset."));
    }

    private static async Task UpdateSpaceGroupAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, Guid id, PresetSpaceGroup group)
    {
        await using var cmd = new NpgsqlCommand(@"
            UPDATE resource_groups
            SET description = @description,
                color = @color,
                display_order = @displayOrder,
                updated_at = CURRENT_TIMESTAMP
            WHERE id = @id", conn, tx);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddNullable("description", group.Description);
        cmd.Parameters.AddNullable("color", group.Color);
        cmd.Parameters.AddWithValue("displayOrder", group.DisplayOrder);
        await cmd.ExecuteNonQueryAsync();
    }

    // ── Templates ──────────────────────────────────────────────────

    private static async Task ApplyTemplatesAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        Guid applicationId, PresetTemplates templates,
        Dictionary<string, Guid> existingMappings,
        Dictionary<string, Guid> criterionIdMap,
        PresetApplicationStats stats)
    {
        await ApplyTemplateListAsync(conn, tx, applicationId, TemplateEntityTypes.Space,
            templates.Space, existingMappings, criterionIdMap, stats);
        await ApplyTemplateListAsync(conn, tx, applicationId, TemplateEntityTypes.Group,
            templates.Group, existingMappings, criterionIdMap, stats);
        await ApplyTemplateListAsync(conn, tx, applicationId, TemplateEntityTypes.Request,
            templates.Request, existingMappings, criterionIdMap, stats);
    }

    private static async Task ApplyTemplateListAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        Guid applicationId, string entityType, List<PresetTemplate> templates,
        Dictionary<string, Guid> existingMappings,
        Dictionary<string, Guid> criterionIdMap,
        PresetApplicationStats stats)
    {
        foreach (var template in templates)
        {
            var mappingKey = $"template_{entityType}:{template.Key}";

            if (existingMappings.TryGetValue(mappingKey, out var existingId))
            {
                await UpdateTemplateAsync(conn, tx, existingId, template, criterionIdMap);
                stats.TemplatesUpdated++;
            }
            else
            {
                var existingByName = await FindTemplateByNameAsync(conn, tx, template.Name, entityType);
                if (existingByName.HasValue)
                {
                    await UpdateTemplateAsync(conn, tx, existingByName.Value, template, criterionIdMap);
                    await SaveMappingAsync(conn, tx, applicationId, $"template_{entityType}", template.Key, existingByName.Value);
                    stats.TemplatesUpdated++;
                }
                else
                {
                    var newId = await CreateTemplateAsync(conn, tx, template, entityType, criterionIdMap);
                    await SaveMappingAsync(conn, tx, applicationId, $"template_{entityType}", template.Key, newId);
                    stats.TemplatesCreated++;
                }
            }
        }
    }

    private static async Task<Guid?> FindTemplateByNameAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, string name, string entityType)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT id FROM templates WHERE LOWER(name) = LOWER(@name) AND entity_type = @entityType",
            conn, tx);
        cmd.Parameters.AddWithValue("name", name);
        cmd.Parameters.AddWithValue("entityType", entityType);
        var result = await cmd.ExecuteScalarAsync();
        return result as Guid?;
    }

    private static async Task<Guid> CreateTemplateAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        PresetTemplate template, string entityType, Dictionary<string, Guid> criterionIdMap)
    {
        await using var cmd = new NpgsqlCommand(@"
            INSERT INTO templates (name, description, entity_type, duration_value, duration_unit,
                                   fixed_start, fixed_end, fixed_duration)
            VALUES (@name, @description, @entityType, @durationValue, @durationUnit,
                    @fixedStart, @fixedEnd, @fixedDuration)
            RETURNING id", conn, tx);
        cmd.Parameters.AddWithValue("name", template.Name);
        cmd.Parameters.AddNullable("description", template.Description);
        cmd.Parameters.AddWithValue("entityType", entityType);
        cmd.Parameters.AddNullable("durationValue", template.DurationValue);
        cmd.Parameters.AddNullable("durationUnit", template.DurationUnit);
        cmd.Parameters.AddWithValue("fixedStart", template.FixedStart);
        cmd.Parameters.AddWithValue("fixedEnd", template.FixedEnd);
        cmd.Parameters.AddWithValue("fixedDuration", template.FixedDuration);

        var templateId = (Guid)(await cmd.ExecuteScalarAsync())!;

        foreach (var item in template.Items)
        {
            if (criterionIdMap.TryGetValue(item.CriterionKey, out var criterionId))
            {
                await CreateTemplateItemAsync(conn, tx, templateId, criterionId, item.Value);
            }
        }

        return templateId;
    }

    private static async Task UpdateTemplateAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        Guid templateId, PresetTemplate template, Dictionary<string, Guid> criterionIdMap)
    {
        await using var cmd = new NpgsqlCommand(@"
            UPDATE templates
            SET description = @description,
                duration_value = @durationValue,
                duration_unit = @durationUnit,
                fixed_start = @fixedStart,
                fixed_end = @fixedEnd,
                fixed_duration = @fixedDuration,
                updated_at = CURRENT_TIMESTAMP
            WHERE id = @id", conn, tx);
        cmd.Parameters.AddWithValue("id", templateId);
        cmd.Parameters.AddNullable("description", template.Description);
        cmd.Parameters.AddNullable("durationValue", template.DurationValue);
        cmd.Parameters.AddNullable("durationUnit", template.DurationUnit);
        cmd.Parameters.AddWithValue("fixedStart", template.FixedStart);
        cmd.Parameters.AddWithValue("fixedEnd", template.FixedEnd);
        cmd.Parameters.AddWithValue("fixedDuration", template.FixedDuration);
        await cmd.ExecuteNonQueryAsync();

        // Delete existing items and recreate
        await using var deleteCmd = new NpgsqlCommand(
            "DELETE FROM template_items WHERE template_id = @templateId", conn, tx);
        deleteCmd.Parameters.AddWithValue("templateId", templateId);
        await deleteCmd.ExecuteNonQueryAsync();

        foreach (var item in template.Items)
        {
            if (criterionIdMap.TryGetValue(item.CriterionKey, out var criterionId))
            {
                await CreateTemplateItemAsync(conn, tx, templateId, criterionId, item.Value);
            }
        }
    }

    private static async Task CreateTemplateItemAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        Guid templateId, Guid criterionId, string value)
    {
        await using var cmd = new NpgsqlCommand(@"
            INSERT INTO template_items (template_id, criterion_id, value)
            VALUES (@templateId, @criterionId, @value::jsonb)", conn, tx);
        cmd.Parameters.AddWithValue("templateId", templateId);
        cmd.Parameters.AddWithValue("criterionId", criterionId);
        cmd.Parameters.AddWithValue("value", ToJsonValue(value));
        await cmd.ExecuteNonQueryAsync();
    }

    // ── Resources ──────────────────────────────────────────────────

    /// <summary>
    /// Creates or adopts each sample resource. A mapped row is updated in place (description,
    /// allocation mode); its name and code are the tenant's after the first apply. An unmapped
    /// one is adopted by code, then by name, within its type — this is what lets a preset that
    /// replaces an earlier seed find the rows that seed left, instead of duplicating them.
    /// Capabilities are upserted, memberships ensured; nothing the tenant added is removed.
    /// </summary>
    private static async Task ApplyResourcesAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        Guid applicationId, List<PresetResource> resources,
        Dictionary<string, Guid> existingMappings,
        Dictionary<string, TypeRef> typeMap,
        Dictionary<string, Guid> criterionIdMap,
        Dictionary<string, GroupRef> groupMap,
        PresetApplicationStats stats)
    {
        foreach (var resource in resources)
        {
            var type = typeMap[resource.TypeKey];
            var mappingKey = $"resource:{resource.Key}";
            var allocationMode = resource.AllocationMode
                ?? (type.HasDirectoryProfile ? AllocationModes.Fractional : AllocationModes.Exclusive);

            Guid? resourceId = existingMappings.TryGetValue(mappingKey, out var mappedId)
                && await ResourceExistsAsync(conn, tx, mappedId)
                    ? mappedId
                    : await FindResourceToAdoptAsync(conn, tx, type.Id, resource.Code, resource.Name);

            if (resourceId.HasValue)
            {
                await UpdateResourceAsync(conn, tx, resourceId.Value, resource.Description, allocationMode);
                stats.ResourcesUpdated++;
            }
            else
            {
                resourceId = await CreateResourceAsync(conn, tx, resource, type, allocationMode);
                stats.ResourcesCreated++;
            }

            await SaveMappingAsync(conn, tx, applicationId, "resource", resource.Key, resourceId.Value);

            foreach (var capability in resource.Capabilities)
                await UpsertCapabilityAsync(conn, tx, resourceId.Value, criterionIdMap[capability.CriterionKey], capability.Value);

            foreach (var groupKey in resource.GroupKeys)
                await AddGroupMemberAsync(conn, tx, groupMap[groupKey].Id, resourceId.Value, type.Id);
        }
    }

    private static async Task<bool> ResourceExistsAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, Guid id)
    {
        await using var cmd = new NpgsqlCommand("SELECT 1 FROM resources WHERE id = @id", conn, tx);
        cmd.Parameters.AddWithValue("id", id);
        return await cmd.ExecuteScalarAsync() != null;
    }

    private static async Task<Guid?> FindResourceToAdoptAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, Guid typeId, string? code, string name)
    {
        await using var cmd = new NpgsqlCommand(@"
            SELECT id FROM resources
            WHERE resource_type_id = @typeId
              AND ((@code::text IS NOT NULL AND code = @code) OR LOWER(name) = LOWER(@name))
            ORDER BY (code = @code) DESC NULLS LAST, created_at
            LIMIT 1", conn, tx);
        cmd.Parameters.AddWithValue("typeId", typeId);
        cmd.Parameters.AddNullable("code", code);
        cmd.Parameters.AddWithValue("name", name);
        var result = await cmd.ExecuteScalarAsync();
        return result as Guid?;
    }

    private static async Task<Guid> CreateResourceAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        PresetResource resource, TypeRef type, string allocationMode)
    {
        // The column set ResourceRepository.CreateAsync writes. Not physical and no geometry:
        // a sample resource is owned but not yet placed on a plan, and the physical-needs-geometry
        // check forbids the other combination. A placeable resource cannot travel between sites
        // (cross_site_allowed), which is what the scheduler reads to know its site is fixed. It is
        // homed on the tenant's oldest site — the default site every tenant starts with.
        await using var cmd = new NpgsqlCommand(@"
            INSERT INTO resources
                (id, resource_type_id, name, description, external_reference,
                 allocation_mode, base_availability_percent,
                 home_site_id, cross_site_allowed,
                 code, is_physical, geometry, properties, capacity, custom_fields,
                 email, notes)
            VALUES
                (@id, @typeId, @name, @description, NULL,
                 @allocationMode, 100,
                 (SELECT id FROM sites ORDER BY created_at, id LIMIT 1), @crossSiteAllowed,
                 @code, false, NULL, '{}'::jsonb, 1, '{}'::jsonb,
                 NULL, NULL)
            RETURNING id", conn, tx);
        cmd.Parameters.AddWithValue("id", Guid.NewGuid());
        cmd.Parameters.AddWithValue("typeId", type.Id);
        cmd.Parameters.AddWithValue("name", resource.Name);
        cmd.Parameters.AddNullable("description", resource.Description);
        cmd.Parameters.AddWithValue("allocationMode", allocationMode);
        cmd.Parameters.AddWithValue("crossSiteAllowed", !type.HasGeometry);
        cmd.Parameters.AddNullable("code", resource.Code);
        return (Guid)(await cmd.ExecuteScalarAsync())!;
    }

    private static async Task UpdateResourceAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, Guid id, string? description, string allocationMode)
    {
        await using var cmd = new NpgsqlCommand(@"
            UPDATE resources
            SET description = @description,
                allocation_mode = @allocationMode,
                updated_at = CURRENT_TIMESTAMP
            WHERE id = @id", conn, tx);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddNullable("description", description);
        cmd.Parameters.AddWithValue("allocationMode", allocationMode);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task UpsertCapabilityAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, Guid resourceId, Guid criterionId, string value)
    {
        await using var cmd = new NpgsqlCommand(@"
            INSERT INTO resource_capabilities (resource_id, criterion_id, value)
            VALUES (@resourceId, @criterionId, @value::jsonb)
            ON CONFLICT (resource_id, criterion_id)
            DO UPDATE SET value = EXCLUDED.value, updated_at = CURRENT_TIMESTAMP", conn, tx);
        cmd.Parameters.AddWithValue("resourceId", resourceId);
        cmd.Parameters.AddWithValue("criterionId", criterionId);
        cmd.Parameters.AddWithValue("value", ToJsonValue(value));
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task AddGroupMemberAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, Guid groupId, Guid resourceId, Guid typeId)
    {
        // The composite FKs from migration 1460 need the type on the member row; the validator
        // guarantees the group holds the resource's type before any of this runs.
        await using var cmd = new NpgsqlCommand(@"
            INSERT INTO resource_group_members (resource_group_id, resource_id, resource_type_id)
            VALUES (@groupId, @resourceId, @typeId)
            ON CONFLICT DO NOTHING", conn, tx);
        cmd.Parameters.AddWithValue("groupId", groupId);
        cmd.Parameters.AddWithValue("resourceId", resourceId);
        cmd.Parameters.AddWithValue("typeId", typeId);
        await cmd.ExecuteNonQueryAsync();
    }

    // ── Helpers ─────────────────────────────────────────────────────

    /// <summary>A preset value is JSON when it parses as JSON; anything else is stored as a JSON string.</summary>
    private static string ToJsonValue(string value) =>
        IsValidJson(value) ? value : JsonSerializer.Serialize(value);

    private static bool IsValidJson(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        value = value.Trim();
        if ((value.StartsWith('{') && value.EndsWith('}')) ||
            (value.StartsWith('[') && value.EndsWith(']')) ||
            (value.StartsWith('"') && value.EndsWith('"')) ||
            value == "true" || value == "false" || value == "null" ||
            double.TryParse(value, out _))
        {
            try
            {
                // Parsed purely to test validity; dispose returns the pooled buffer.
                using var _ = JsonDocument.Parse(value);
                return true;
            }
            catch
            {
                return false;
            }
        }
        return false;
    }
}

/// <summary>
/// Statistics tracking preset application results (created vs updated entities).
/// </summary>
public record PresetApplicationStats
{
    public int ResourceTypesActivated { get; set; }
    public int CriteriaCreated { get; set; }
    public int CriteriaUpdated { get; set; }
    public int SpaceGroupsCreated { get; set; }
    public int SpaceGroupsUpdated { get; set; }
    public int TemplatesCreated { get; set; }
    public int TemplatesUpdated { get; set; }
    public int ResourcesCreated { get; set; }
    public int ResourcesUpdated { get; set; }
}
