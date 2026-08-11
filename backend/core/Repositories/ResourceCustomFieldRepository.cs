using Api.Helpers;
using Api.Models;
using Api.Services;
using Npgsql;

namespace Api.Repositories;

public interface IResourceCustomFieldRepository
{
    /// <summary>Every definition for a type, in form order.</summary>
    Task<List<ResourceCustomFieldInfo>> GetByResourceTypeAsync(Guid resourceTypeId, CancellationToken ct = default);
    Task<ResourceCustomFieldInfo?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<ResourceCustomFieldInfo> CreateAsync(Guid resourceTypeId, CreateResourceCustomFieldRequest request, CancellationToken ct = default);
    /// <summary>Applies a partial update. Returns the updated row, or null when not found.</summary>
    Task<ResourceCustomFieldInfo?> UpdateAsync(Guid id, UpdateResourceCustomFieldRequest request, CancellationToken ct = default);
    /// <summary>
    /// Deletes a definition and strips its key from every resource of that type, in one
    /// transaction. Leaving the values behind would resurrect them under a later field that
    /// happened to reuse the key — with a different data type, and no definition to validate against.
    /// </summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}

public class ResourceCustomFieldRepository(OrgContext orgContext, IOrgDbConnectionFactory connectionFactory)
    : IResourceCustomFieldRepository
{
    private const string SelectColumns =
        "id, resource_type_id, key, label, description, data_type, "
        + "is_required, sort_order, is_active, created_at, updated_at";

    // Form order, with label as the tiebreak so two fields sharing a sort_order do not swap
    // places between requests.
    private const string FormOrder = "ORDER BY sort_order, label";

    public async Task<List<ResourceCustomFieldInfo>> GetByResourceTypeAsync(Guid resourceTypeId, CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        return await db.QueryListAsync(
            $"SELECT {SelectColumns} FROM resource_custom_fields WHERE resource_type_id = @typeId {FormOrder}",
            p => p.AddWithValue("typeId", resourceTypeId), Map, ct);
    }

    public async Task<ResourceCustomFieldInfo?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        return await db.QuerySingleOrDefaultAsync(
            $"SELECT {SelectColumns} FROM resource_custom_fields WHERE id = @id",
            p => p.AddWithValue("id", id), Map, ct);
    }

    public async Task<ResourceCustomFieldInfo> CreateAsync(
        Guid resourceTypeId, CreateResourceCustomFieldRequest request, CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        try
        {
            return (await db.QuerySingleOrDefaultAsync(
                $@"INSERT INTO resource_custom_fields
                       (resource_type_id, key, label, description, data_type, is_required, sort_order)
                   VALUES (@typeId, @key, @label, @description, @dataType, @isRequired, @sortOrder)
                   RETURNING {SelectColumns}",
                p =>
                {
                    p.AddWithValue("typeId", resourceTypeId);
                    p.AddWithValue("key", request.Key);
                    p.AddWithValue("label", request.Label);
                    p.AddNullable("description", request.Description);
                    p.AddWithValue("dataType", request.DataType);
                    p.AddWithValue("isRequired", request.IsRequired);
                    p.AddWithValue("sortOrder", request.SortOrder);
                }, Map, ct))!;
        }
        catch (PostgresException pg) when (pg.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            // The unique constraint is the arbiter rather than a read-then-insert, which two
            // concurrent creates would both pass.
            throw new ConflictException(
                $"A custom field with key '{request.Key}' already exists for this resource type");
        }
    }

    public async Task<ResourceCustomFieldInfo?> UpdateAsync(
        Guid id, UpdateResourceCustomFieldRequest request, CancellationToken ct = default)
    {
        var update = new UpdateBuilder();
        update.SetIfNotNull("label", request.Label);
        update.SetIfNotNull("description", request.Description);
        if (request.IsRequired.HasValue) update.Set("is_required", request.IsRequired.Value);
        if (request.SortOrder.HasValue) update.Set("sort_order", request.SortOrder.Value);
        if (request.IsActive.HasValue) update.Set("is_active", request.IsActive.Value);

        if (update.IsEmpty) return await GetByIdAsync(id, ct);

        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        return await db.QuerySingleOrDefaultAsync(
            $"UPDATE resource_custom_fields SET {update.SetClause} WHERE id = @id RETURNING {SelectColumns}",
            p => { p.AddWithValue("id", id); update.Apply(p); }, Map, ct);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        await db.OpenAsync(ct);
        await using var tx = await db.BeginTransactionAsync(ct);

        // Read the definition on this transaction's connection: the key and type it names are
        // what the value strip below targets, and the row is gone by the time we need them.
        Guid resourceTypeId;
        string key;
        await using (var readCmd = new NpgsqlCommand(
            "SELECT resource_type_id, key FROM resource_custom_fields WHERE id = @id", db, tx))
        {
            readCmd.Parameters.AddWithValue("id", id);
            await using var reader = await readCmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) return false;
            resourceTypeId = reader.GetGuid(0);
            key = reader.GetString(1);
        }

        await using (var stripCmd = new NpgsqlCommand(
            "UPDATE resources SET custom_fields = custom_fields - @key "
            // jsonb_exists rather than the `?` operator: `?` is also a parameter placeholder in
            // several drivers, and the function form leaves nothing to interpretation.
            + "WHERE resource_type_id = @typeId AND jsonb_exists(custom_fields, @key)", db, tx))
        {
            stripCmd.Parameters.AddWithValue("key", key);
            stripCmd.Parameters.AddWithValue("typeId", resourceTypeId);
            await stripCmd.ExecuteNonQueryAsync(ct);
        }

        await using (var deleteCmd = new NpgsqlCommand(
            "DELETE FROM resource_custom_fields WHERE id = @id", db, tx))
        {
            deleteCmd.Parameters.AddWithValue("id", id);
            await deleteCmd.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
        return true;
    }

    private static ResourceCustomFieldInfo Map(NpgsqlDataReader r) => new()
    {
        Id = r.GetGuid(r.GetOrdinal("id")),
        ResourceTypeId = r.GetGuid(r.GetOrdinal("resource_type_id")),
        Key = r.GetString(r.GetOrdinal("key")),
        Label = r.GetString(r.GetOrdinal("label")),
        Description = r.GetNullableString("description"),
        DataType = r.GetString(r.GetOrdinal("data_type")),
        IsRequired = r.GetBoolean(r.GetOrdinal("is_required")),
        SortOrder = r.GetInt32(r.GetOrdinal("sort_order")),
        IsActive = r.GetBoolean(r.GetOrdinal("is_active")),
        CreatedAt = r.GetDateTime(r.GetOrdinal("created_at")),
        UpdatedAt = r.GetDateTime(r.GetOrdinal("updated_at")),
    };
}
