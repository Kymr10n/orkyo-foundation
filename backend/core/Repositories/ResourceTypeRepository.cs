using Api.Helpers;
using Api.Models;
using Api.Services;
using Npgsql;

namespace Api.Repositories;

public interface IResourceTypeRepository
{
    Task<List<ResourceTypeInfo>> GetAllAsync(CancellationToken ct = default);
    Task<ResourceTypeInfo?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<ResourceTypeInfo?> GetByKeyAsync(string key, CancellationToken ct = default);
    /// <summary>Creates a user-defined resource type. Always <c>is_system = false</c>.</summary>
    Task<ResourceTypeInfo> CreateAsync(CreateResourceTypeRequest request, CancellationToken ct = default);
    /// <summary>Applies a partial update. Returns the updated row, or null when not found.</summary>
    Task<ResourceTypeInfo?> UpdateAsync(Guid id, UpdateResourceTypeRequest request, CancellationToken ct = default);
    /// <summary>Hard-deletes a type. Callers must ensure it is non-system and unused.</summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
    /// <summary>Number of resources (active or not) referencing this type.</summary>
    Task<int> CountResourcesAsync(Guid id, CancellationToken ct = default);
    Task<int> CountRequestTargetsAsync(Guid id, CancellationToken ct = default);
    /// <summary>Keys of every active type that declares geometry — what "needs a place" means.</summary>
    Task<IReadOnlyList<string>> GetPlaceableKeysAsync(CancellationToken ct = default);

    /// <summary>Resource and request-target counts for every type, in one round trip.
    /// Types with zero of both are absent from the result.</summary>
    Task<Dictionary<Guid, ResourceTypeUsage>> GetUsageCountsAsync(CancellationToken ct = default);

    /// <summary>
    /// Deletes a type together with everything that hangs off it — resources (and their
    /// assignments), groups, request targets, availability-event scopes — in one transaction.
    /// The remaining references (custom fields, criterion applicability, type-scoped list
    /// definitions, capabilities, memberships, absences, list rows, search documents) go by
    /// FK cascade or trigger. Returns null when the type does not exist.
    /// </summary>
    Task<ResourceTypePurgeResult?> PurgeAsync(Guid id, CancellationToken ct = default);
}

/// <summary>Per-type usage: what makes deactivation a decision rather than a click.</summary>
public sealed record ResourceTypeUsage(int Resources, int RequestTargets);

/// <summary>What a purge removed, for the confirmation toast and the audit trail.</summary>
public sealed record ResourceTypePurgeResult(
    int Resources, int Assignments, int Groups, int RequestTargets);

public class ResourceTypeRepository(OrgContext orgContext, IOrgDbConnectionFactory connectionFactory)
    : IResourceTypeRepository
{
    private const string SelectColumns =
        "id, key, display_name, display_name_plural, description, icon, has_geometry, "
        + "has_directory_profile, single_group_membership, is_system, is_active, created_at, updated_at";

    public async Task<IReadOnlyList<string>> GetPlaceableKeysAsync(CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        return await db.QueryListAsync(
            "SELECT key FROM resource_types WHERE has_geometry AND is_active ORDER BY key",
            null, r => r.GetString(0), ct);
    }

    public async Task<List<ResourceTypeInfo>> GetAllAsync(CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        return await db.QueryListAsync(
            $"SELECT {SelectColumns} FROM resource_types ORDER BY display_name", null, Map, ct);
    }

    public async Task<ResourceTypeInfo?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        return await db.QuerySingleOrDefaultAsync(
            $"SELECT {SelectColumns} FROM resource_types WHERE id = @id",
            p => p.AddWithValue("id", id), Map, ct);
    }

    public async Task<ResourceTypeInfo?> GetByKeyAsync(string key, CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        return await db.QuerySingleOrDefaultAsync(
            $"SELECT {SelectColumns} FROM resource_types WHERE key = @key",
            p => p.AddWithValue("key", key), Map, ct);
    }

    public async Task<ResourceTypeInfo> CreateAsync(CreateResourceTypeRequest request, CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        return (await db.QuerySingleOrDefaultAsync(
            $@"INSERT INTO resource_types (key, display_name, display_name_plural, description, icon,
                                             has_geometry, has_directory_profile, single_group_membership,
                                             is_system, is_active)
               VALUES (@key, @displayName, @displayNamePlural, @description, @icon,
                       @hasGeometry, @hasDirectoryProfile, @singleGroupMembership, false, true)
               RETURNING {SelectColumns}",
            p =>
            {
                p.AddWithValue("key", request.Key);
                p.AddWithValue("displayName", request.DisplayName);
                p.AddWithValue("displayNamePlural", request.DisplayNamePlural);
                p.AddNullable("description", request.Description);
                p.AddNullable("icon", request.Icon);
                p.AddWithValue("hasGeometry", request.HasGeometry);
                p.AddWithValue("hasDirectoryProfile", request.HasDirectoryProfile);
                p.AddWithValue("singleGroupMembership", request.SingleGroupMembership);
            }, Map, ct))!;
    }

    public async Task<ResourceTypeInfo?> UpdateAsync(Guid id, UpdateResourceTypeRequest request, CancellationToken ct = default)
    {
        var update = new UpdateBuilder();
        update.SetIfNotNull("display_name", request.DisplayName);
        update.SetIfNotNull("display_name_plural", request.DisplayNamePlural);
        update.SetIfNotNull("has_geometry", request.HasGeometry);
        update.SetIfNotNull("has_directory_profile", request.HasDirectoryProfile);
        update.SetIfNotNull("single_group_membership", request.SingleGroupMembership);
        update.SetIfNotNull("description", request.Description);
        update.SetIfNotNull("icon", request.Icon);
        if (request.IsActive.HasValue) update.Set("is_active", request.IsActive.Value);

        if (update.IsEmpty) return await GetByIdAsync(id, ct);

        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        return await db.QuerySingleOrDefaultAsync(
            $"UPDATE resource_types SET {update.SetClause} WHERE id = @id RETURNING {SelectColumns}",
            p => { p.AddWithValue("id", id); update.Apply(p); }, Map, ct);
    }

    /// <summary>Requests naming this type as a target. They hold it via ON DELETE RESTRICT,
    /// so a type with none of its own resources can still be undeletable.</summary>
    public async Task<int> CountRequestTargetsAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        return (int)await db.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM request_target_resource_types WHERE resource_type_id = @id",
            p => p.AddWithValue("id", id), ct);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        return await db.ExecuteAsync(
            "DELETE FROM resource_types WHERE id = @id",
            p => p.AddWithValue("id", id), ct) > 0;
    }

    public async Task<int> CountResourcesAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        return (int)await db.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM resources WHERE resource_type_id = @id",
            p => p.AddWithValue("id", id), ct);
    }

    public async Task<Dictionary<Guid, ResourceTypeUsage>> GetUsageCountsAsync(CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        await db.OpenAsync(ct);

        var result = new Dictionary<Guid, ResourceTypeUsage>();
        await using var cmd = new NpgsqlCommand(@"
            SELECT type_id, SUM(resources)::int, SUM(targets)::int
            FROM (
                SELECT resource_type_id AS type_id, COUNT(*) AS resources, 0 AS targets
                FROM resources GROUP BY resource_type_id
                UNION ALL
                SELECT resource_type_id, 0, COUNT(*)
                FROM request_target_resource_types GROUP BY resource_type_id
            ) u
            GROUP BY type_id", db);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            result[reader.GetGuid(0)] = new ResourceTypeUsage(reader.GetInt32(1), reader.GetInt32(2));
        return result;
    }

    public async Task<ResourceTypePurgeResult?> PurgeAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        await db.OpenAsync(ct);
        await using var tx = await db.BeginTransactionAsync(ct);

        async Task<int> ExecAsync(string sql)
        {
            await using var cmd = new NpgsqlCommand(sql, db, tx);
            cmd.Parameters.AddWithValue("t", id);
            return await cmd.ExecuteNonQueryAsync(ct);
        }

        // Order matters. availability_event_scopes is polymorphic with no FK, so dangling
        // rows would silently corrupt availability resolution; assignments and request
        // targets are ON DELETE RESTRICT; groups reference the type with a plain FK (no
        // cascade). Everything else cascades from the resources/type deletes, and the search
        // documents go through the AFTER DELETE trigger.
        await ExecAsync(@"
            DELETE FROM availability_event_scopes
            WHERE (target_type = 'resource_type'  AND target_id = @t)
               OR (target_type = 'resource'       AND target_id IN (SELECT id FROM resources WHERE resource_type_id = @t))
               OR (target_type = 'resource_group' AND target_id IN (SELECT id FROM resource_groups WHERE resource_type_id = @t))");
        var assignments = await ExecAsync(@"
            DELETE FROM resource_assignments
            WHERE resource_id IN (SELECT id FROM resources WHERE resource_type_id = @t)");
        var targets = await ExecAsync(
            "DELETE FROM request_target_resource_types WHERE resource_type_id = @t");
        var resources = await ExecAsync("DELETE FROM resources WHERE resource_type_id = @t");
        var groups = await ExecAsync("DELETE FROM resource_groups WHERE resource_type_id = @t");

        // The type's own lists, taken down child-first. list_definitions cascades from the type
        // (migration 1810), but its own children are ON DELETE RESTRICT (1780) — so left alone the
        // cascade aborts with a raw 23503 and the purge surfaces as a 500. The type's custom fields
        // go first because a field bound to one of these instances is one of those RESTRICTs; they
        // would cascade with the type anyway, one statement earlier.
        await ExecAsync("DELETE FROM resource_custom_fields WHERE resource_type_id = @t");
        await ExecAsync(@"
            DELETE FROM list_rows
            WHERE list_instance_id IN (
                SELECT li.id FROM list_instances li
                  JOIN list_definitions ld ON ld.id = li.list_definition_id
                 WHERE ld.resource_type_id = @t)");
        await ExecAsync(@"
            DELETE FROM list_instances
            WHERE list_definition_id IN (SELECT id FROM list_definitions WHERE resource_type_id = @t)");

        int deleted;
        try
        {
            deleted = await ExecAsync("DELETE FROM resource_types WHERE id = @t");
        }
        catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.ForeignKeyViolation)
        {
            // Something outside this type still points at one of its lists — a custom field on
            // another type bound to a shared instance of it. Refusing is the honest answer: the
            // alternative is deleting a field the admin did not ask about.
            await tx.RollbackAsync(ct);
            throw new ConflictException(
                "This type owns a list another resource type still uses. Remove that field first");
        }

        if (deleted == 0)
        {
            await tx.RollbackAsync(ct);
            return null;
        }

        await tx.CommitAsync(ct);
        return new ResourceTypePurgeResult(resources, assignments, groups, targets);
    }

    private static ResourceTypeInfo Map(NpgsqlDataReader r) => new()
    {
        Id = r.GetGuid(r.GetOrdinal("id")),
        Key = r.GetString(r.GetOrdinal("key")),
        DisplayName = r.GetString(r.GetOrdinal("display_name")),
        DisplayNamePlural = r.GetString(r.GetOrdinal("display_name_plural")),
        HasGeometry = r.GetBoolean(r.GetOrdinal("has_geometry")),
        HasDirectoryProfile = r.GetBoolean(r.GetOrdinal("has_directory_profile")),
        SingleGroupMembership = r.GetBoolean(r.GetOrdinal("single_group_membership")),
        Description = r.IsDBNull(r.GetOrdinal("description")) ? null : r.GetString(r.GetOrdinal("description")),
        Icon = r.IsDBNull(r.GetOrdinal("icon")) ? null : r.GetString(r.GetOrdinal("icon")),
        IsSystem = r.GetBoolean(r.GetOrdinal("is_system")),
        IsActive = r.GetBoolean(r.GetOrdinal("is_active")),
        CreatedAt = r.GetDateTime(r.GetOrdinal("created_at")),
        UpdatedAt = r.GetDateTime(r.GetOrdinal("updated_at")),
    };
}
