using Npgsql;

namespace Orkyo.Foundation.Seed;

/// <summary>
/// Wipes a tenant database to a clean slate before seeding. Preserves
/// authentication-adjacent tables (users, memberships, invites,
/// user_preferences) so a developer's local login keeps working.
///
/// CASCADE means listing the most-referenced tables is enough; PG will sweep
/// up everything FK-dependent.
/// </summary>
public static class TenantReset
{
    // Tables seeded by the factories (plus their CASCADE-dependent children).
    // Order is informational — TRUNCATE...CASCADE doesn't require dependency order,
    // but listing parents first reads naturally.
    private static readonly string[] TablesToTruncate =
    [
        "sites",
        "space_groups",          // alias of resource_groups in older schemas
        "resource_groups",
        "resource_group_members",
        "resources",             // also carries the folded spaces / person_profiles columns (1700)
        "departments",
        "job_titles",
        "criteria",
        "space_capabilities",
        "resource_capabilities",
        "resource_capabilities_phase1",
        "resource_group_capabilities",
        "availability_events",
        "availability_event_scopes",
        "resource_absences",
        "templates",
        "template_items",
        "request_templates",
        "request_template_requirements",
        "requests",
        "request_requirements",
        "resource_assignments",
        "scheduling_settings",
        "off_times",
        "off_time_spaces",
        "search_documents",
        "preset_applications",
        "preset_mappings",
        "assets",
        // Tenant-defined shape, seeded alongside the machines. These are NOT swept by truncating
        // `resources`: a shared list instance has no resource_id, and field/definition rows are
        // parents rather than children. Left behind, a second `--mode reset` run dies on
        // resource_custom_fields_key_unique / list_definitions_name_unique.
        "resource_custom_fields",
        "list_definitions",
        "list_columns",
        "list_instances",
        "list_rows",
    ];

    public static async Task TruncateAllAsync(NpgsqlConnection conn, NpgsqlTransaction tx)
    {
        // Filter to tables that actually exist (the schema evolves; older or
        // newer DBs may lack some). information_schema lookup is cheap.
        var existing = await GetExistingTablesAsync(conn, tx);
        var toTruncate = TablesToTruncate
            .Where(existing.Contains)
            .Select(t => $"public.{t}")
            .ToArray();
        if (toTruncate.Length == 0) return;

        var sql = $"TRUNCATE TABLE {string.Join(", ", toTruncate)} RESTART IDENTITY CASCADE";
        await using (var cmd = new NpgsqlCommand(sql, conn, tx))
        {
            await cmd.ExecuteNonQueryAsync();
        }

        await DeleteSeededResourceTypesAsync(conn, tx, existing);
    }

    /// <summary>
    /// Removes the resource types the seed defines for itself, so re-seeding does not collide with
    /// resource_types_key_unique. A DELETE rather than a TRUNCATE because the built-in types are in
    /// the same table and belong to the migration, not to us — and everything referencing a seeded
    /// type (resources, custom fields, criterion applicability) has just been truncated in this
    /// same transaction.
    /// </summary>
    private static async Task DeleteSeededResourceTypesAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, HashSet<string> existing)
    {
        if (!existing.Contains("resource_types")) return;

        await using var cmd = new NpgsqlCommand(
            "DELETE FROM public.resource_types WHERE is_system = false", conn, tx);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<HashSet<string>> GetExistingTablesAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var cmd = new NpgsqlCommand(
            "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public'",
            conn, tx);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            names.Add(reader.GetString(0));
        return names;
    }
}
