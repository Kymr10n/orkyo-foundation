using System.Text.Json;
using Api.Helpers;
using Api.Models;
using Api.Services;
using Npgsql;

namespace Api.Repositories;

public interface IListInstanceRepository
{
    Task<ListInstanceInfo?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>The named instances of one definition, by name.</summary>
    Task<List<ListInstanceInfo>> GetSharedByDefinitionAsync(Guid definitionId, CancellationToken ct = default);

    Task<ListInstanceInfo> CreateSharedAsync(Guid definitionId, CreateListInstanceRequest request, CancellationToken ct = default);
    Task<ListInstanceInfo?> UpdateSharedAsync(Guid id, UpdateListInstanceRequest request, CancellationToken ct = default);

    /// <summary>
    /// Deletes a shared instance. Throws <see cref="ConflictException"/> while a lookup field still
    /// points at it — the FK is RESTRICT, so the database arbitrates rather than a racy pre-check.
    /// </summary>
    Task<bool> DeleteSharedAsync(Guid id, CancellationToken ct = default);

    /// <summary>The instance for one (resource, field), or null. Never creates — see the resolver.</summary>
    Task<ListInstanceInfo?> GetResourceInstanceAsync(Guid resourceId, Guid fieldId, CancellationToken ct = default);

    /// <summary>
    /// Returns the instance for one (resource, field), creating it if it does not exist. The unique
    /// constraint is the arbiter: two concurrent first-writes both insert, one conflicts, and both
    /// end up with the same row.
    /// </summary>
    Task<ListInstanceInfo> GetOrCreateResourceInstanceAsync(
        Guid definitionId, Guid resourceId, Guid fieldId, CancellationToken ct = default);

    Task<List<ListRowInfo>> GetRowsAsync(Guid instanceId, CancellationToken ct = default);
    Task<ListRowInfo?> GetRowAsync(Guid rowId, CancellationToken ct = default);
    Task<ListRowInfo> CreateRowAsync(Guid instanceId, IReadOnlyDictionary<string, JsonElement> values, CancellationToken ct = default);
    Task<ListRowInfo?> UpdateRowAsync(Guid rowId, IReadOnlyDictionary<string, JsonElement> values, CancellationToken ct = default);

    /// <summary>
    /// Deletes a row and, for a row of a shared instance, strips its id from every lookup value that
    /// picked it — in one transaction, so a stored id never outlives the row it names.
    /// </summary>
    Task<bool> DeleteRowAsync(Guid rowId, CancellationToken ct = default);

    /// <summary>How many of <paramref name="rowIds"/> exist in one instance. Batched: one round trip.</summary>
    Task<int> CountExistingRowsAsync(Guid instanceId, IReadOnlyList<Guid> rowIds, CancellationToken ct = default);

    Task<int> CountRowsAsync(Guid instanceId, CancellationToken ct = default);
}

public class ListInstanceRepository(OrgContext orgContext, IOrgDbConnectionFactory connectionFactory)
    : IListInstanceRepository
{
    private const string InstanceColumns =
        "id, list_definition_id, kind, name, resource_id, field_id, created_at, updated_at";

    private const string RowColumns =
        "id, list_instance_id, values, created_at, updated_at";

    // Rows have no sort_order (see migration 1780): insertion order is the stable default, and the
    // data table sorts by column from there.
    private const string RowOrder = "ORDER BY created_at, id";

    public async Task<ListInstanceInfo?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        return await db.QuerySingleOrDefaultAsync(
            $"SELECT {InstanceColumns} FROM list_instances WHERE id = @id",
            p => p.AddWithValue("id", id), MapInstance, ct);
    }

    public async Task<List<ListInstanceInfo>> GetSharedByDefinitionAsync(Guid definitionId, CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        return await db.QueryListAsync(
            $"SELECT {InstanceColumns} FROM list_instances "
            + "WHERE list_definition_id = @id AND kind = 'shared' ORDER BY name",
            p => p.AddWithValue("id", definitionId), MapInstance, ct);
    }

    public async Task<ListInstanceInfo> CreateSharedAsync(
        Guid definitionId, CreateListInstanceRequest request, CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        try
        {
            return (await db.QuerySingleOrDefaultAsync(
                $@"INSERT INTO list_instances (list_definition_id, kind, name)
                   VALUES (@definitionId, '{ListInstanceKinds.Shared}', @name)
                   RETURNING {InstanceColumns}",
                p =>
                {
                    p.AddWithValue("definitionId", definitionId);
                    p.AddWithValue("name", request.Name);
                }, MapInstance, ct))!;
        }
        catch (PostgresException pg) when (pg.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new ConflictException($"A list instance named '{request.Name}' already exists for this definition");
        }
    }

    public async Task<ListInstanceInfo?> UpdateSharedAsync(
        Guid id, UpdateListInstanceRequest request, CancellationToken ct = default)
    {
        var update = new UpdateBuilder();
        update.SetIfNotNull("name", request.Name);
        if (update.IsEmpty) return await GetByIdAsync(id, ct);

        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        try
        {
            return await db.QuerySingleOrDefaultAsync(
                $"UPDATE list_instances SET {update.SetClause} "
                + $"WHERE id = @id AND kind = '{ListInstanceKinds.Shared}' RETURNING {InstanceColumns}",
                p => { p.AddWithValue("id", id); update.Apply(p); }, MapInstance, ct);
        }
        catch (PostgresException pg) when (pg.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new ConflictException($"A list instance named '{request.Name}' already exists for this definition");
        }
    }

    public async Task<bool> DeleteSharedAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        try
        {
            var deleted = await db.ExecuteAsync(
                $"DELETE FROM list_instances WHERE id = @id AND kind = '{ListInstanceKinds.Shared}'",
                p => p.AddWithValue("id", id), ct);
            return deleted > 0;
        }
        catch (PostgresException pg) when (pg.SqlState == PostgresErrorCodes.ForeignKeyViolation)
        {
            throw new ConflictException(
                "This list instance is still in use by a custom field. Remove the field, or point it "
                + "at another instance, before deleting this one.");
        }
    }

    public async Task<ListInstanceInfo?> GetResourceInstanceAsync(
        Guid resourceId, Guid fieldId, CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        return await db.QuerySingleOrDefaultAsync(
            $"SELECT {InstanceColumns} FROM list_instances WHERE resource_id = @resourceId AND field_id = @fieldId",
            p =>
            {
                p.AddWithValue("resourceId", resourceId);
                p.AddWithValue("fieldId", fieldId);
            }, MapInstance, ct);
    }

    public async Task<ListInstanceInfo> GetOrCreateResourceInstanceAsync(
        Guid definitionId, Guid resourceId, Guid fieldId, CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);

        // DO NOTHING rather than DO UPDATE: there is nothing to update, and the RETURNING of a
        // conflicting insert is empty — hence the read below rather than a single statement.
        var created = await db.QuerySingleOrDefaultAsync(
            $@"INSERT INTO list_instances (list_definition_id, kind, resource_id, field_id)
               VALUES (@definitionId, '{ListInstanceKinds.Resource}', @resourceId, @fieldId)
               ON CONFLICT (resource_id, field_id) DO NOTHING
               RETURNING {InstanceColumns}",
            p =>
            {
                p.AddWithValue("definitionId", definitionId);
                p.AddWithValue("resourceId", resourceId);
                p.AddWithValue("fieldId", fieldId);
            }, MapInstance, ct);

        return created ?? (await GetResourceInstanceAsync(resourceId, fieldId, ct))!;
    }

    public async Task<List<ListRowInfo>> GetRowsAsync(Guid instanceId, CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        return await db.QueryListAsync(
            $"SELECT {RowColumns} FROM list_rows WHERE list_instance_id = @id {RowOrder}",
            p => p.AddWithValue("id", instanceId), MapRow, ct);
    }

    public async Task<ListRowInfo?> GetRowAsync(Guid rowId, CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        return await db.QuerySingleOrDefaultAsync(
            $"SELECT {RowColumns} FROM list_rows WHERE id = @id",
            p => p.AddWithValue("id", rowId), MapRow, ct);
    }

    public async Task<ListRowInfo> CreateRowAsync(
        Guid instanceId, IReadOnlyDictionary<string, JsonElement> values, CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        return (await db.QuerySingleOrDefaultAsync(
            $@"INSERT INTO list_rows (list_instance_id, values) VALUES (@instanceId, @values)
               RETURNING {RowColumns}",
            p =>
            {
                p.AddWithValue("instanceId", instanceId);
                p.AddJsonb("values", JsonSerializer.Serialize(values));
            }, MapRow, ct))!;
    }

    public async Task<ListRowInfo?> UpdateRowAsync(
        Guid rowId, IReadOnlyDictionary<string, JsonElement> values, CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        return await db.QuerySingleOrDefaultAsync(
            $"UPDATE list_rows SET values = @values WHERE id = @id RETURNING {RowColumns}",
            p =>
            {
                p.AddWithValue("id", rowId);
                p.AddJsonb("values", JsonSerializer.Serialize(values));
            }, MapRow, ct);
    }

    public async Task<bool> DeleteRowAsync(Guid rowId, CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        await db.OpenAsync(ct);
        await using var tx = await db.BeginTransactionAsync(ct);

        Guid instanceId;
        await using (var readCmd = new NpgsqlCommand(
            "SELECT list_instance_id FROM list_rows WHERE id = @id", db, tx))
        {
            readCmd.Parameters.AddWithValue("id", rowId);
            await using var reader = await readCmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) return false;
            instanceId = reader.GetGuid(0);
        }

        // Strip the id from every lookup value that picked this row. Matches nothing for a
        // per-resource instance — no field points at one — so the statement is unconditional
        // rather than branching on kind.
        await using (var stripCmd = new NpgsqlCommand(
            @"UPDATE resources r
                 SET custom_fields = jsonb_set(
                         r.custom_fields,
                         ARRAY[f.key],
                         COALESCE((SELECT jsonb_agg(elem)
                                     FROM jsonb_array_elements(r.custom_fields -> f.key) elem
                                    WHERE elem <> to_jsonb(@rowId::text)), '[]'::jsonb))
                FROM resource_custom_fields f
               WHERE f.data_type = @lookupType
                 AND f.list_instance_id = @instanceId
                 AND r.resource_type_id = f.resource_type_id
                 AND jsonb_exists(r.custom_fields, f.key)
                 AND r.custom_fields -> f.key @> to_jsonb(@rowId::text)", db, tx))
        {
            stripCmd.Parameters.AddWithValue("rowId", rowId.ToString());
            stripCmd.Parameters.AddWithValue("instanceId", instanceId);
            stripCmd.Parameters.AddWithValue("lookupType", CustomFieldDataTypes.ListLookup);
            await stripCmd.ExecuteNonQueryAsync(ct);
        }

        await using (var deleteCmd = new NpgsqlCommand("DELETE FROM list_rows WHERE id = @id", db, tx))
        {
            deleteCmd.Parameters.AddWithValue("id", rowId);
            await deleteCmd.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
        return true;
    }

    public async Task<int> CountExistingRowsAsync(
        Guid instanceId, IReadOnlyList<Guid> rowIds, CancellationToken ct = default)
    {
        if (rowIds.Count == 0) return 0;

        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        return await db.QuerySingleOrDefaultAsync(
            "SELECT count(*)::int AS n FROM list_rows WHERE list_instance_id = @instanceId AND id = ANY(@ids)",
            p =>
            {
                p.AddWithValue("instanceId", instanceId);
                p.AddWithValue("ids", rowIds.ToArray());
            }, r => r.GetInt32("n"), ct);
    }

    public async Task<int> CountRowsAsync(Guid instanceId, CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        return await db.QuerySingleOrDefaultAsync(
            "SELECT count(*)::int AS n FROM list_rows WHERE list_instance_id = @id",
            p => p.AddWithValue("id", instanceId), r => r.GetInt32("n"), ct);
    }

    private static ListInstanceInfo MapInstance(NpgsqlDataReader r) => new()
    {
        Id = r.GetGuid("id"),
        ListDefinitionId = r.GetGuid("list_definition_id"),
        Kind = r.GetString("kind"),
        Name = r.GetNullableString("name"),
        ResourceId = r.GetNullableGuid("resource_id"),
        FieldId = r.GetNullableGuid("field_id"),
        CreatedAt = r.GetDateTime("created_at"),
        UpdatedAt = r.GetDateTime("updated_at"),
    };

    private static ListRowInfo MapRow(NpgsqlDataReader r) => new()
    {
        Id = r.GetGuid("id"),
        ListInstanceId = r.GetGuid("list_instance_id"),
        Values = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(r.GetString("values")) ?? [],
        CreatedAt = r.GetDateTime("created_at"),
        UpdatedAt = r.GetDateTime("updated_at"),
    };
}
