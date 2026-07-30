using System.Text.Json;
using Api.Models;
using Api.Services;
using Npgsql;

namespace Api.Repositories;

public interface IResourceTypeFieldRepository
{
    /// <summary>Field definitions for a type, ordered by sort order then key.</summary>
    Task<List<ResourceTypeFieldInfo>> GetByTypeAsync(Guid resourceTypeId, bool includeInactive = false, CancellationToken ct = default);
    Task<ResourceTypeFieldInfo?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<ResourceTypeFieldInfo> CreateAsync(Guid resourceTypeId, CreateResourceTypeFieldRequest request, CancellationToken ct = default);
    Task<ResourceTypeFieldInfo?> UpdateAsync(Guid id, UpdateResourceTypeFieldRequest request, CancellationToken ct = default);
    Task<bool> DeactivateAsync(Guid id, CancellationToken ct = default);
}

public class ResourceTypeFieldRepository(OrgContext orgContext, IOrgDbConnectionFactory connectionFactory)
    : IResourceTypeFieldRepository
{
    private const string SelectColumns =
        "id, resource_type_id, key, label, description, data_type, options_json, validation_json, " +
        "is_required, sort_order, is_active, created_at, updated_at";

    public async Task<List<ResourceTypeFieldInfo>> GetByTypeAsync(
        Guid resourceTypeId, bool includeInactive = false, CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        var activeClause = includeInactive ? "" : " AND is_active";
        return await db.QueryListAsync(
            $"SELECT {SelectColumns} FROM resource_type_fields " +
            $"WHERE resource_type_id = @typeId{activeClause} ORDER BY sort_order, key",
            p => p.AddWithValue("typeId", resourceTypeId), Map, ct);
    }

    public async Task<ResourceTypeFieldInfo?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        return await db.QuerySingleOrDefaultAsync(
            $"SELECT {SelectColumns} FROM resource_type_fields WHERE id = @id",
            p => p.AddWithValue("id", id), Map, ct);
    }

    public async Task<ResourceTypeFieldInfo> CreateAsync(
        Guid resourceTypeId, CreateResourceTypeFieldRequest request, CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        return (await db.QuerySingleOrDefaultAsync(
            $@"INSERT INTO resource_type_fields
                   (resource_type_id, key, label, description, data_type,
                    options_json, validation_json, is_required, sort_order)
               VALUES
                   (@typeId, @key, @label, @description, @dataType,
                    @options, @validation, @isRequired, @sortOrder)
               RETURNING {SelectColumns}",
            p =>
            {
                p.AddWithValue("typeId", resourceTypeId);
                p.AddWithValue("key", request.Key);
                p.AddWithValue("label", request.Label);
                p.AddNullable("description", request.Description);
                p.AddWithValue("dataType", request.DataType);
                p.AddJsonb("options", request.Options?.GetRawText());
                p.AddJsonb("validation", request.Validation?.GetRawText());
                p.AddWithValue("isRequired", request.IsRequired);
                p.AddWithValue("sortOrder", request.SortOrder);
            }, Map, ct))!;
    }

    public async Task<ResourceTypeFieldInfo?> UpdateAsync(
        Guid id, UpdateResourceTypeFieldRequest request, CancellationToken ct = default)
    {
        var update = new UpdateBuilder();
        update.SetIfNotNull("label", request.Label);
        update.SetIfNotNull("description", request.Description);
        if (request.IsRequired.HasValue) update.Set("is_required", request.IsRequired.Value);
        if (request.SortOrder.HasValue) update.Set("sort_order", request.SortOrder.Value);
        if (request.IsActive.HasValue) update.Set("is_active", request.IsActive.Value);
        // JSONB columns need typed parameters, so they bypass UpdateBuilder's parameter binding.
        if (request.Options.HasValue) update.SetExpression("options_json = @options");
        if (request.Validation.HasValue) update.SetExpression("validation_json = @validation");

        if (update.IsEmpty) return await GetByIdAsync(id, ct);

        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        return await db.QuerySingleOrDefaultAsync(
            $"UPDATE resource_type_fields SET {update.SetClause} WHERE id = @id RETURNING {SelectColumns}",
            p =>
            {
                p.AddWithValue("id", id);
                update.Apply(p);
                if (request.Options.HasValue) p.AddJsonb("options", request.Options.Value.GetRawText());
                if (request.Validation.HasValue) p.AddJsonb("validation", request.Validation.Value.GetRawText());
            }, Map, ct);
    }

    public async Task<bool> DeactivateAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        return await db.ExecuteAsync(
            "UPDATE resource_type_fields SET is_active = false WHERE id = @id",
            p => p.AddWithValue("id", id), ct) > 0;
    }

    private static ResourceTypeFieldInfo Map(NpgsqlDataReader r) => new()
    {
        Id = r.GetGuid(r.GetOrdinal("id")),
        ResourceTypeId = r.GetGuid(r.GetOrdinal("resource_type_id")),
        Key = r.GetString(r.GetOrdinal("key")),
        Label = r.GetString(r.GetOrdinal("label")),
        Description = r.IsDBNull(r.GetOrdinal("description")) ? null : r.GetString(r.GetOrdinal("description")),
        DataType = r.GetString(r.GetOrdinal("data_type")),
        Options = ReadJson(r, "options_json"),
        Validation = ReadJson(r, "validation_json"),
        IsRequired = r.GetBoolean(r.GetOrdinal("is_required")),
        SortOrder = r.GetInt32(r.GetOrdinal("sort_order")),
        IsActive = r.GetBoolean(r.GetOrdinal("is_active")),
        CreatedAt = r.GetDateTime(r.GetOrdinal("created_at")),
        UpdatedAt = r.GetDateTime(r.GetOrdinal("updated_at")),
    };

    private static JsonElement? ReadJson(NpgsqlDataReader r, string column)
    {
        var ordinal = r.GetOrdinal(column);
        if (r.IsDBNull(ordinal)) return null;
        using var doc = JsonDocument.Parse(r.GetString(ordinal));
        return doc.RootElement.Clone();
    }
}
