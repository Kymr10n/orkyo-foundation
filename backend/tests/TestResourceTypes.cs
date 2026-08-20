using Npgsql;

namespace Orkyo.Foundation.Tests;

/// <summary>
/// The three classic resource types, ensured in a tenant database by the test fixtures.
/// </summary>
/// <remarks>
/// Migrations no longer seed any resource type — built-ins are a catalog a tenant activates
/// (1870/1880) — but most of the suite predates that and assumes space, person and tool exist.
/// Both fixtures create them here rather than each carrying its own copy: they diverged once
/// already, and the fixture that missed out failed three tests with a null resource type id.
/// </remarks>
public static class TestResourceTypes
{
    /// <summary>
    /// Inserts space, person and tool with the flags migrations 1300/1700 gave them and
    /// <c>is_system = false</c>, the only value that exists after 1870. A no-op on a database
    /// where the rows survive.
    /// </summary>
    public static async Task EnsureAsync(NpgsqlConnection tenantConnection, CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(@"
            INSERT INTO resource_types
                (key, display_name, display_name_plural, icon,
                 is_system, is_active, has_geometry, has_directory_profile, single_group_membership)
            VALUES
                ('space',  'Space',  'Spaces', 'Box',    false, true, true,  false, true),
                ('person', 'Person', 'People', 'Users',  false, true, false, true,  false),
                ('tool',   'Tool',   'Tools',  'Wrench', false, true, false, false, false)
            ON CONFLICT (key) DO NOTHING", tenantConnection);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
