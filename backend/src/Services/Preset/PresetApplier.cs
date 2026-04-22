using System.Text.Json;
using Api.Models.Preset;
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
/// </summary>
public static class PresetApplier
{
    // ── Public entry point ─────────────────────────────────────────

    /// <summary>
    /// Applies a full preset (criteria + groups + templates) inside
    /// the given transaction.  Does NOT commit or rollback — the caller
    /// controls the transaction boundary.
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

        // Apply in order: criteria -> space groups -> templates
        var criterionIdMap = await ApplyCriteriaAsync(
            conn, tx, applicationId, preset.Contents.Criteria, existingMappings, stats);

        await ApplySpaceGroupsAsync(
            conn, tx, applicationId, preset.Contents.SpaceGroups, existingMappings, stats);

        await ApplyTemplatesAsync(
            conn, tx, applicationId, preset.Contents.Templates,
            existingMappings, criterionIdMap, stats);

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
            var entityType = reader.GetString(0);
            var logicalKey = reader.GetString(1);
            var entityId = reader.GetGuid(2);
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

    // ── Criteria ───────────────────────────────────────────────────

    private static async Task<Dictionary<string, Guid>> ApplyCriteriaAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        Guid applicationId, List<PresetCriterion> criteria,
        Dictionary<string, Guid> existingMappings, PresetApplicationStats stats)
    {
        var idMap = new Dictionary<string, Guid>();

        foreach (var criterion in criteria)
        {
            var mappingKey = $"criterion:{criterion.Key}";

            if (existingMappings.TryGetValue(mappingKey, out var existingId))
            {
                await UpdateCriterionAsync(conn, tx, existingId, criterion);
                idMap[criterion.Key] = existingId;
                stats.CriteriaUpdated++;
            }
            else
            {
                var existingByName = await FindCriterionByNameAsync(conn, tx, criterion.Name);
                if (existingByName.HasValue)
                {
                    idMap[criterion.Key] = existingByName.Value;
                    await SaveMappingAsync(conn, tx, applicationId, "criterion", criterion.Key, existingByName.Value);
                    stats.CriteriaUpdated++;
                }
                else
                {
                    var newId = await CreateCriterionAsync(conn, tx, criterion);
                    idMap[criterion.Key] = newId;
                    await SaveMappingAsync(conn, tx, applicationId, "criterion", criterion.Key, newId);
                    stats.CriteriaCreated++;
                }
            }
        }

        return idMap;
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
        cmd.Parameters.AddWithValue("description", (object?)criterion.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("dataType", criterion.DataType.ToString());
        cmd.Parameters.AddWithValue("enumValues",
            criterion.EnumValues != null ? JsonSerializer.Serialize(criterion.EnumValues) : DBNull.Value);
        cmd.Parameters.AddWithValue("unit", (object?)criterion.Unit ?? DBNull.Value);

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
        cmd.Parameters.AddWithValue("description", (object?)criterion.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("enumValues",
            criterion.EnumValues != null ? JsonSerializer.Serialize(criterion.EnumValues) : DBNull.Value);
        cmd.Parameters.AddWithValue("unit", (object?)criterion.Unit ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    // ── Space Groups ───────────────────────────────────────────────

    private static async Task ApplySpaceGroupsAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        Guid applicationId, List<PresetSpaceGroup> groups,
        Dictionary<string, Guid> existingMappings, PresetApplicationStats stats)
    {
        foreach (var group in groups)
        {
            var mappingKey = $"space_group:{group.Key}";

            if (existingMappings.TryGetValue(mappingKey, out var existingId))
            {
                await UpdateSpaceGroupAsync(conn, tx, existingId, group);
                stats.SpaceGroupsUpdated++;
            }
            else
            {
                var existingByName = await FindSpaceGroupByNameAsync(conn, tx, group.Name);
                if (existingByName.HasValue)
                {
                    await SaveMappingAsync(conn, tx, applicationId, "space_group", group.Key, existingByName.Value);
                    stats.SpaceGroupsUpdated++;
                }
                else
                {
                    var newId = await CreateSpaceGroupAsync(conn, tx, group);
                    await SaveMappingAsync(conn, tx, applicationId, "space_group", group.Key, newId);
                    stats.SpaceGroupsCreated++;
                }
            }
        }
    }

    private static async Task<Guid?> FindSpaceGroupByNameAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, string name)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT id FROM space_groups WHERE LOWER(name) = LOWER(@name)", conn, tx);
        cmd.Parameters.AddWithValue("name", name);
        var result = await cmd.ExecuteScalarAsync();
        return result as Guid?;
    }

    private static async Task<Guid> CreateSpaceGroupAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, PresetSpaceGroup group)
    {
        await using var cmd = new NpgsqlCommand(@"
            INSERT INTO space_groups (name, description, color, display_order)
            VALUES (@name, @description, @color, @displayOrder)
            RETURNING id", conn, tx);
        cmd.Parameters.AddWithValue("name", group.Name);
        cmd.Parameters.AddWithValue("description", (object?)group.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("color", (object?)group.Color ?? DBNull.Value);
        cmd.Parameters.AddWithValue("displayOrder", group.DisplayOrder);
        return (Guid)(await cmd.ExecuteScalarAsync())!;
    }

    private static async Task UpdateSpaceGroupAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, Guid id, PresetSpaceGroup group)
    {
        await using var cmd = new NpgsqlCommand(@"
            UPDATE space_groups 
            SET description = @description, 
                color = @color, 
                display_order = @displayOrder,
                updated_at = CURRENT_TIMESTAMP
            WHERE id = @id", conn, tx);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("description", (object?)group.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("color", (object?)group.Color ?? DBNull.Value);
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
        await ApplyTemplateListAsync(conn, tx, applicationId, "space",
            templates.Space, existingMappings, criterionIdMap, stats);
        await ApplyTemplateListAsync(conn, tx, applicationId, "group",
            templates.Group, existingMappings, criterionIdMap, stats);
        await ApplyTemplateListAsync(conn, tx, applicationId, "request",
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
        cmd.Parameters.AddWithValue("description", (object?)template.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("entityType", entityType);
        cmd.Parameters.AddWithValue("durationValue", (object?)template.DurationValue ?? DBNull.Value);
        cmd.Parameters.AddWithValue("durationUnit", (object?)template.DurationUnit ?? DBNull.Value);
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
        cmd.Parameters.AddWithValue("description", (object?)template.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("durationValue", (object?)template.DurationValue ?? DBNull.Value);
        cmd.Parameters.AddWithValue("durationUnit", (object?)template.DurationUnit ?? DBNull.Value);
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
        var jsonValue = IsValidJson(value) ? value : JsonSerializer.Serialize(value);
        cmd.Parameters.AddWithValue("value", jsonValue);
        await cmd.ExecuteNonQueryAsync();
    }

    // ── Helpers ─────────────────────────────────────────────────────

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
                JsonDocument.Parse(value);
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
    public int CriteriaCreated { get; set; }
    public int CriteriaUpdated { get; set; }
    public int SpaceGroupsCreated { get; set; }
    public int SpaceGroupsUpdated { get; set; }
    public int TemplatesCreated { get; set; }
    public int TemplatesUpdated { get; set; }
}

