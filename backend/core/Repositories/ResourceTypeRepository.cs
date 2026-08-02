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
}

public class ResourceTypeRepository(OrgContext orgContext, IOrgDbConnectionFactory connectionFactory)
    : IResourceTypeRepository
{
    private const string SelectColumns =
        "id, key, display_name, display_name_plural, description, icon, has_geometry, "
        + "has_directory_profile, single_group_membership, is_system, is_active, created_at, updated_at";

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
