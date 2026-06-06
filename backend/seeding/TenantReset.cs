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
        "spaces",
        "space_groups",          // alias of resource_groups in older schemas
        "resource_groups",
        "resource_group_members",
        "resources",
        "person_profiles",
        "departments",
        "job_titles",
        "criteria",
        "space_capabilities",
        "resource_capabilities_phase1",
        "resource_group_capabilities",
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
        await using var cmd = new NpgsqlCommand(sql, conn, tx);
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
