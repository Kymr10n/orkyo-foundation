using Npgsql;

namespace Orkyo.Foundation.Seed.Factories;

/// <summary>
/// The one way a seed profile writes a <c>resource_types</c> row. Four factories used to
/// carry their own copy of this INSERT, and the copies drifted: only the machine variant
/// reactivated a deactivated type on conflict, so a reseed resurrected machine types but
/// not person, room or tool types.
///
/// Reactivation is the rule, not the drift. Adopting a type is what catalog activation
/// does, and activation reactivates while changing nothing else — the row is the
/// tenant's, renames and edits included. Every seeded type now follows it.
/// </summary>
public static class ResourceTypeSeedHelpers
{
    public static async Task<Guid> UpsertResourceTypeAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction? tx,
        string key,
        string displayName,
        string displayNamePlural,
        string description,
        string icon,
        bool hasGeometry = false,
        bool singleGroupMembership = false,
        bool hasDirectoryProfile = false)
    {
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO public.resource_types " +
            "(id, key, display_name, display_name_plural, description, icon, " +
            " is_system, is_active, has_geometry, single_group_membership, has_directory_profile, created_at, updated_at) " +
            "VALUES (@id, @key, @name, @plural, @description, @icon, " +
            "false, true, @hasGeometry, @singleGroup, @hasDirectory, @now, @now) " +
            "ON CONFLICT (key) DO UPDATE SET is_active = true, updated_at = @now " +
            "RETURNING id", conn, tx);
        cmd.Parameters.AddWithValue("id", Guid.NewGuid());
        cmd.Parameters.AddWithValue("key", key);
        cmd.Parameters.AddWithValue("name", displayName);
        cmd.Parameters.AddWithValue("plural", displayNamePlural);
        cmd.Parameters.AddWithValue("description", description);
        cmd.Parameters.AddWithValue("icon", icon);
        cmd.Parameters.AddWithValue("hasGeometry", hasGeometry);
        cmd.Parameters.AddWithValue("singleGroup", singleGroupMembership);
        cmd.Parameters.AddWithValue("hasDirectory", hasDirectoryProfile);
        cmd.Parameters.AddWithValue("now", DateTime.UtcNow);
        return (Guid)(await cmd.ExecuteScalarAsync())!;
    }
}
