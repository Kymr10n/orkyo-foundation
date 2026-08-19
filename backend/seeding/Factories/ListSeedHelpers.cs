using Npgsql;
using NpgsqlTypes;

namespace Orkyo.Foundation.Seed.Factories;

/// <summary>
/// The insert statements every list-seeding factory needs: definitions, their columns, shared
/// instances, rows, and the custom fields that bind them.
///
/// One copy rather than one per factory. The machine catalogues and the organization lists differ
/// in what they seed, not in how a list is written, and two copies of the same INSERT drift the
/// moment a column is added — which is exactly what migration 1810 did to `list_definitions`.
/// </summary>
internal static class ListSeedHelpers
{
    /// <summary>
    /// Creates a definition. <paramref name="scope"/> and <paramref name="resourceTypeId"/> say who
    /// owns it (migration 1810); the database pairs them with a CHECK, so a resource scope must
    /// name a type and the other two scopes must not.
    /// </summary>
    public static async Task<Guid> InsertDefinitionAsync(
        NpgsqlConnection conn, string name, string description, DateTime now,
        string scope = "common", Guid? resourceTypeId = null)
    {
        var id = Guid.NewGuid();
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO public.list_definitions " +
            "(id, name, description, scope, resource_type_id, is_active, created_at, updated_at) " +
            "VALUES (@id, @name, @description, @scope, @resourceTypeId, true, @now, @now)", conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("name", name);
        cmd.Parameters.AddWithValue("description", description);
        cmd.Parameters.AddWithValue("scope", scope);
        cmd.Parameters.AddWithValue("resourceTypeId", (object?)resourceTypeId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("now", now);
        await cmd.ExecuteNonQueryAsync();
        return id;
    }

    /// <summary>Finds a definition by its scoped name, or null. Used by seeds that must adopt what
    /// a migration already created rather than insert a duplicate.</summary>
    public static async Task<Guid?> FindDefinitionAsync(
        NpgsqlConnection conn, string scope, string name)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT id FROM public.list_definitions WHERE scope = @scope AND name = @name " +
            "AND resource_type_id IS NULL LIMIT 1", conn);
        cmd.Parameters.AddWithValue("scope", scope);
        cmd.Parameters.AddWithValue("name", name);
        return (Guid?)await cmd.ExecuteScalarAsync();
    }

    public static async Task<Guid> InsertColumnAsync(
        NpgsqlConnection conn, Guid definitionId, string key, string label, string dataType,
        bool required, int sort, DateTime now, string? optionsJson = null, string? description = null)
    {
        var id = Guid.NewGuid();
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO public.list_columns " +
            "(id, list_definition_id, key, label, description, data_type, options, is_required, " +
            " sort_order, is_active, created_at, updated_at) " +
            "VALUES (@id, @definitionId, @key, @label, @description, @dataType, @options, @required, " +
            "        @sort, true, @now, @now)", conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("definitionId", definitionId);
        cmd.Parameters.AddWithValue("key", key);
        cmd.Parameters.AddWithValue("label", label);
        cmd.Parameters.AddWithValue("description", (object?)description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("dataType", dataType);
        cmd.Parameters.Add(new NpgsqlParameter("options", NpgsqlDbType.Jsonb)
        {
            Value = (object?)optionsJson ?? DBNull.Value,
        });
        cmd.Parameters.AddWithValue("required", required);
        cmd.Parameters.AddWithValue("sort", sort);
        cmd.Parameters.AddWithValue("now", now);
        await cmd.ExecuteNonQueryAsync();
        return id;
    }

    /// <summary>Designated after the columns exist — the FK points at a column of this definition,
    /// so it cannot be set in the same statement that creates the definition.</summary>
    public static async Task SetDisplayColumnAsync(NpgsqlConnection conn, Guid definitionId, Guid columnId)
    {
        await using var cmd = new NpgsqlCommand(
            "UPDATE public.list_definitions SET display_column_id = @columnId WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("columnId", columnId);
        cmd.Parameters.AddWithValue("id", definitionId);
        await cmd.ExecuteNonQueryAsync();
    }

    public static async Task<Guid> InsertSharedInstanceAsync(
        NpgsqlConnection conn, Guid definitionId, string name, DateTime now)
    {
        var id = Guid.NewGuid();
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO public.list_instances (id, list_definition_id, kind, name, created_at, updated_at) " +
            "VALUES (@id, @definitionId, 'shared', @name, @now, @now)", conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("definitionId", definitionId);
        cmd.Parameters.AddWithValue("name", name);
        cmd.Parameters.AddWithValue("now", now);
        await cmd.ExecuteNonQueryAsync();
        return id;
    }

    /// <summary>The shared instance of a definition, or null when it has none yet.</summary>
    public static async Task<Guid?> FindSharedInstanceAsync(NpgsqlConnection conn, Guid definitionId)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT id FROM public.list_instances " +
            "WHERE list_definition_id = @definitionId AND kind = 'shared' ORDER BY name LIMIT 1", conn);
        cmd.Parameters.AddWithValue("definitionId", definitionId);
        return (Guid?)await cmd.ExecuteScalarAsync();
    }

    public static async Task<Guid> InsertRowAsync(
        NpgsqlConnection conn, Guid instanceId, string valuesJson, DateTime now)
    {
        var id = Guid.NewGuid();
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO public.list_rows (id, list_instance_id, values, created_at, updated_at) " +
            "VALUES (@id, @instanceId, @values, @now, @now)", conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("instanceId", instanceId);
        cmd.Parameters.Add(new NpgsqlParameter("values", NpgsqlDbType.Jsonb) { Value = valuesJson });
        cmd.Parameters.AddWithValue("now", now);
        await cmd.ExecuteNonQueryAsync();
        return id;
    }

    /// <summary>
    /// A custom field on a type. Pass <paramref name="listDefinitionId"/> for a per-resource
    /// <c>list</c> field, <paramref name="listInstanceId"/> for a shared <c>list_lookup</c>, and
    /// neither for a scalar. <c>ON CONFLICT DO NOTHING</c> keeps a seed that runs over a database a
    /// migration already prepared from dying on the (type, key) unique index.
    /// </summary>
    public static async Task<Guid?> InsertCustomFieldAsync(
        NpgsqlConnection conn, Guid resourceTypeId, string key, string label, string dataType,
        bool required, int sort, DateTime now,
        Guid? listDefinitionId = null, Guid? listInstanceId = null)
    {
        var id = Guid.NewGuid();
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO public.resource_custom_fields " +
            "(id, resource_type_id, key, label, data_type, is_required, sort_order, is_active, " +
            " list_definition_id, list_instance_id, created_at, updated_at) " +
            "VALUES (@id, @typeId, @key, @label, @dataType, @required, @sort, true, " +
            "        @defId, @instId, @now, @now) " +
            "ON CONFLICT (resource_type_id, key) DO NOTHING " +
            "RETURNING id", conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("typeId", resourceTypeId);
        cmd.Parameters.AddWithValue("key", key);
        cmd.Parameters.AddWithValue("label", label);
        cmd.Parameters.AddWithValue("dataType", dataType);
        cmd.Parameters.AddWithValue("required", required);
        cmd.Parameters.AddWithValue("sort", sort);
        cmd.Parameters.AddWithValue("defId", (object?)listDefinitionId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("instId", (object?)listInstanceId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("now", now);
        return (Guid?)await cmd.ExecuteScalarAsync();
    }
}
