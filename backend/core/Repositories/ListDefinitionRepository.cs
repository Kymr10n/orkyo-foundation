using System.Text.Json;
using Api.Helpers;
using Api.Models;
using Api.Services;
using Npgsql;

namespace Api.Repositories;

public interface IListDefinitionRepository
{
    /// <summary>Every definition, by name. Columns are not loaded — use <see cref="GetByIdAsync"/>.</summary>
    Task<List<ListDefinitionInfo>> GetAllAsync(bool includeInactive = false, CancellationToken ct = default);

    /// <summary>One definition with its columns in form order, or null.</summary>
    Task<ListDefinitionInfo?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<ListDefinitionInfo> CreateAsync(CreateListDefinitionRequest request, CancellationToken ct = default);
    Task<ListDefinitionInfo?> UpdateAsync(Guid id, UpdateListDefinitionRequest request, CancellationToken ct = default);

    /// <summary>
    /// Deletes a definition. Returns false when it does not exist; throws
    /// <see cref="ConflictException"/> when a field or instance still references it — the FKs are
    /// RESTRICT, so the database is the arbiter rather than a read-then-delete two callers could race.
    /// </summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);

    Task<List<ListColumnInfo>> GetColumnsAsync(Guid definitionId, CancellationToken ct = default);
    Task<ListColumnInfo?> GetColumnAsync(Guid columnId, CancellationToken ct = default);
    Task<ListColumnInfo> CreateColumnAsync(Guid definitionId, CreateListColumnRequest request, CancellationToken ct = default);
    Task<ListColumnInfo?> UpdateColumnAsync(Guid columnId, UpdateListColumnRequest request, CancellationToken ct = default);

    /// <summary>
    /// Deletes a column and strips its key from every row of every instance of the definition, in
    /// one transaction — the same reason ResourceCustomFieldRepository.DeleteAsync strips values:
    /// an orphaned cell would resurrect under a later column that reused the key, with a different
    /// data type and no definition to validate it.
    /// </summary>
    Task<bool> DeleteColumnAsync(Guid columnId, CancellationToken ct = default);
}

public class ListDefinitionRepository(OrgContext orgContext, IOrgDbConnectionFactory connectionFactory)
    : IListDefinitionRepository
{
    private const string DefinitionColumns =
        "id, name, description, scope, resource_type_id, is_active, display_column_id, "
        + "created_at, updated_at";

    private const string ColumnColumns =
        "id, list_definition_id, key, label, description, data_type, options, "
        + "is_required, sort_order, is_active, created_at, updated_at";

    // Form order, with label as the tiebreak so two columns sharing a sort_order do not swap
    // places between requests.
    private const string FormOrder = "ORDER BY sort_order, label";

    public async Task<List<ListDefinitionInfo>> GetAllAsync(bool includeInactive = false, CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        var where = includeInactive ? "" : "WHERE is_active = true ";
        return await db.QueryListAsync(
            $"SELECT {DefinitionColumns} FROM list_definitions {where}ORDER BY name",
            _ => { }, MapDefinition, ct);
    }

    public async Task<ListDefinitionInfo?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        var definition = await db.QuerySingleOrDefaultAsync(
            $"SELECT {DefinitionColumns} FROM list_definitions WHERE id = @id",
            p => p.AddWithValue("id", id), MapDefinition, ct);

        if (definition is null) return null;

        var columns = await db.QueryListAsync(
            $"SELECT {ColumnColumns} FROM list_columns WHERE list_definition_id = @id {FormOrder}",
            p => p.AddWithValue("id", id), MapColumn, ct);

        return definition with { Columns = columns };
    }

    public async Task<ListDefinitionInfo> CreateAsync(CreateListDefinitionRequest request, CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        try
        {
            return (await db.QuerySingleOrDefaultAsync(
                $@"INSERT INTO list_definitions (name, description, scope, resource_type_id)
                   VALUES (@name, @description, @scope, @resource_type_id)
                   RETURNING {DefinitionColumns}",
                p =>
                {
                    p.AddWithValue("name", request.Name);
                    p.AddNullable("description", request.Description);
                    p.AddWithValue("scope", request.Scope);
                    p.AddNullable("resource_type_id", request.ResourceTypeId);
                }, MapDefinition, ct))!;
        }
        catch (PostgresException pg) when (pg.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new ConflictException(
                $"A list definition named '{request.Name}' already exists in this scope");
        }
    }

    public async Task<ListDefinitionInfo?> UpdateAsync(Guid id, UpdateListDefinitionRequest request, CancellationToken ct = default)
    {
        var update = new UpdateBuilder();
        update.SetIfNotNull("name", request.Name);
        update.SetIfNotNull("description", request.Description);
        if (request.IsActive.HasValue) update.Set("is_active", request.IsActive.Value);
        // Clearing wins over setting: a request that says both is contradictory, and dropping the
        // designation is the safer reading of it.
        if (request.ClearDisplayColumn) update.SetExpression("display_column_id = NULL");
        else if (request.DisplayColumnId.HasValue) update.Set("display_column_id", request.DisplayColumnId.Value);

        if (update.IsEmpty) return await GetByIdAsync(id, ct);

        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        try
        {
            return await db.QuerySingleOrDefaultAsync(
                $"UPDATE list_definitions SET {update.SetClause} WHERE id = @id RETURNING {DefinitionColumns}",
                p => { p.AddWithValue("id", id); update.Apply(p); }, MapDefinition, ct);
        }
        catch (PostgresException pg) when (pg.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new ConflictException(
                $"A list definition named '{request.Name}' already exists in this scope");
        }
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        try
        {
            var deleted = await db.ExecuteAsync(
                "DELETE FROM list_definitions WHERE id = @id",
                p => p.AddWithValue("id", id), ct);
            return deleted > 0;
        }
        catch (PostgresException pg) when (pg.SqlState == PostgresErrorCodes.ForeignKeyViolation)
        {
            throw new ConflictException(
                "This list definition is still in use by a custom field or a shared instance. "
                + "Deactivate it instead, or remove what references it first.");
        }
    }

    public async Task<List<ListColumnInfo>> GetColumnsAsync(Guid definitionId, CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        return await db.QueryListAsync(
            $"SELECT {ColumnColumns} FROM list_columns WHERE list_definition_id = @id {FormOrder}",
            p => p.AddWithValue("id", definitionId), MapColumn, ct);
    }

    public async Task<ListColumnInfo?> GetColumnAsync(Guid columnId, CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        return await db.QuerySingleOrDefaultAsync(
            $"SELECT {ColumnColumns} FROM list_columns WHERE id = @id",
            p => p.AddWithValue("id", columnId), MapColumn, ct);
    }

    public async Task<ListColumnInfo> CreateColumnAsync(
        Guid definitionId, CreateListColumnRequest request, CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        try
        {
            return (await db.QuerySingleOrDefaultAsync(
                $@"INSERT INTO list_columns
                       (list_definition_id, key, label, description, data_type, options, is_required, sort_order)
                   VALUES (@definitionId, @key, @label, @description, @dataType, @options, @isRequired, @sortOrder)
                   RETURNING {ColumnColumns}",
                p =>
                {
                    p.AddWithValue("definitionId", definitionId);
                    p.AddWithValue("key", request.Key);
                    p.AddWithValue("label", request.Label);
                    p.AddNullable("description", request.Description);
                    p.AddWithValue("dataType", request.DataType);
                    if (request.Options is null) p.AddNullable("options", null);
                    else p.AddJsonb("options", JsonSerializer.Serialize(request.Options));
                    p.AddWithValue("isRequired", request.IsRequired);
                    p.AddWithValue("sortOrder", request.SortOrder);
                }, MapColumn, ct))!;
        }
        catch (PostgresException pg) when (pg.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new ConflictException($"A column with key '{request.Key}' already exists in this list definition");
        }
    }

    public async Task<ListColumnInfo?> UpdateColumnAsync(
        Guid columnId, UpdateListColumnRequest request, CancellationToken ct = default)
    {
        var update = new UpdateBuilder();
        update.SetIfNotNull("label", request.Label);
        update.SetIfNotNull("description", request.Description);
        if (request.IsRequired.HasValue) update.Set("is_required", request.IsRequired.Value);
        if (request.SortOrder.HasValue) update.Set("sort_order", request.SortOrder.Value);
        if (request.IsActive.HasValue) update.Set("is_active", request.IsActive.Value);
        if (request.Options is not null) update.SetExpression("options = @options");

        if (update.IsEmpty) return await GetColumnAsync(columnId, ct);

        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        return await db.QuerySingleOrDefaultAsync(
            $"UPDATE list_columns SET {update.SetClause} WHERE id = @id RETURNING {ColumnColumns}",
            p =>
            {
                p.AddWithValue("id", columnId);
                update.Apply(p);
                if (request.Options is not null) p.AddJsonb("options", JsonSerializer.Serialize(request.Options));
            }, MapColumn, ct);
    }

    public async Task<bool> DeleteColumnAsync(Guid columnId, CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        await db.OpenAsync(ct);
        await using var tx = await db.BeginTransactionAsync(ct);

        // Read the column on this transaction's connection: the definition and key it names are
        // what the cell strip below targets, and the row is gone by the time we need them.
        Guid definitionId;
        string key;
        await using (var readCmd = new NpgsqlCommand(
            "SELECT list_definition_id, key FROM list_columns WHERE id = @id", db, tx))
        {
            readCmd.Parameters.AddWithValue("id", columnId);
            await using var reader = await readCmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) return false;
            definitionId = reader.GetGuid(0);
            key = reader.GetString(1);
        }

        await using (var stripCmd = new NpgsqlCommand(
            "UPDATE list_rows SET values = values - @key "
            + "WHERE list_instance_id IN (SELECT id FROM list_instances WHERE list_definition_id = @definitionId) "
            // jsonb_exists rather than the `?` operator, for the reason given in
            // ResourceCustomFieldRepository: `?` is also a parameter placeholder.
            + "AND jsonb_exists(values, @key)", db, tx))
        {
            stripCmd.Parameters.AddWithValue("key", key);
            stripCmd.Parameters.AddWithValue("definitionId", definitionId);
            await stripCmd.ExecuteNonQueryAsync(ct);
        }

        await using (var deleteCmd = new NpgsqlCommand(
            "DELETE FROM list_columns WHERE id = @id", db, tx))
        {
            deleteCmd.Parameters.AddWithValue("id", columnId);
            await deleteCmd.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
        return true;
    }

    private static ListDefinitionInfo MapDefinition(NpgsqlDataReader r) => new()
    {
        Id = r.GetGuid("id"),
        Name = r.GetString("name"),
        Description = r.GetNullableString("description"),
        Scope = r.GetString("scope"),
        ResourceTypeId = r.GetNullableGuid("resource_type_id"),
        IsActive = r.GetBoolean("is_active"),
        DisplayColumnId = r.GetNullableGuid("display_column_id"),
        CreatedAt = r.GetDateTime("created_at"),
        UpdatedAt = r.GetDateTime("updated_at"),
    };

    private static ListColumnInfo MapColumn(NpgsqlDataReader r)
    {
        var optionsJson = r.GetNullableString("options");
        return new ListColumnInfo
        {
            Id = r.GetGuid("id"),
            ListDefinitionId = r.GetGuid("list_definition_id"),
            Key = r.GetString("key"),
            Label = r.GetString("label"),
            Description = r.GetNullableString("description"),
            DataType = r.GetString("data_type"),
            Options = optionsJson is null ? null : JsonSerializer.Deserialize<List<string>>(optionsJson),
            IsRequired = r.GetBoolean("is_required"),
            SortOrder = r.GetInt32("sort_order"),
            IsActive = r.GetBoolean("is_active"),
            CreatedAt = r.GetDateTime("created_at"),
            UpdatedAt = r.GetDateTime("updated_at"),
        };
    }
}
