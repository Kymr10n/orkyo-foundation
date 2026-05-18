using System.Text.Json;
using Api.Helpers;
using Api.Models;
using Api.Services;
using Npgsql;
using NpgsqlTypes;

namespace Api.Repositories;

public class CriteriaRepository : ICriteriaRepository
{
    // Columns are always selected with the `c.` alias on `criteria c` so the
    // aggregate sub-select can correlate. `resource_type_keys` is a text[].
    private const string SelectColumns =
        "c.id, c.name, c.description, c.data_type, c.enum_values, c.unit, " +
        "c.applicable_to_requests, c.created_at, c.updated_at, " +
        "COALESCE(" +
        "  (SELECT ARRAY_AGG(rt.key ORDER BY rt.key) " +
        "   FROM criterion_resource_types crt " +
        "   JOIN resource_types rt ON rt.id = crt.resource_type_id " +
        "   WHERE crt.criterion_id = c.id), " +
        "  '{}'::text[]" +
        ") AS resource_type_keys";

    private readonly OrgContext _orgContext;
    private readonly IOrgDbConnectionFactory _connectionFactory;

    public CriteriaRepository(OrgContext orgContext, IOrgDbConnectionFactory connectionFactory)
    {
        _orgContext = orgContext;
        _connectionFactory = connectionFactory;
    }

    public async Task<List<CriterionInfo>> GetAllAsync()
    {
        await using var db = _connectionFactory.CreateOrgConnection(_orgContext);
        await db.OpenAsync();

        var criteria = new List<CriterionInfo>();
        var cmd = new NpgsqlCommand(
            $"SELECT {SelectColumns} FROM criteria c ORDER BY c.name LIMIT 500", db);

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            criteria.Add(CriteriaMapper.MapFromReader(reader));
        }

        return criteria;
    }

    public async Task<List<CriterionInfo>> GetByResourceTypeAsync(string resourceTypeKey)
    {
        await using var db = _connectionFactory.CreateOrgConnection(_orgContext);
        await db.OpenAsync();

        // Strict rule (post-applicability-backfill): a criterion is included
        // only when explicitly tagged for this resource type. The previous
        // open-world fallback (criteria with no applicability rows treated as
        // universal) was removed in the same release that backfilled all
        // untagged criteria as 'space'.
        var cmd = new NpgsqlCommand(
            $@"SELECT {SelectColumns} FROM criteria c
               WHERE EXISTS (
                   SELECT 1 FROM criterion_resource_types crt
                   JOIN resource_types rt ON rt.id = crt.resource_type_id
                   WHERE crt.criterion_id = c.id AND rt.key = @key
               )
               ORDER BY c.name LIMIT 500", db);
        cmd.Parameters.AddWithValue("key", resourceTypeKey);

        var criteria = new List<CriterionInfo>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            criteria.Add(CriteriaMapper.MapFromReader(reader));
        return criteria;
    }

    public async Task<PagedResult<CriterionInfo>> GetAllAsync(PageRequest page)
    {
        await using var db = _connectionFactory.CreateOrgConnection(_orgContext);
        await db.OpenAsync();

        return await DbQueryHelper.ExecutePagedQueryAsync(
            db,
            page,
            countSql: "SELECT COUNT(*) FROM criteria",
            querySql: $"SELECT {SelectColumns} FROM criteria c ORDER BY c.name LIMIT @limit OFFSET @offset",
            addParams: null,
            mapper: CriteriaMapper.MapFromReader);
    }

    public async Task<CriterionInfo?> GetByIdAsync(Guid id)
    {
        await using var db = _connectionFactory.CreateOrgConnection(_orgContext);
        await db.OpenAsync();

        var cmd = new NpgsqlCommand(
            $"SELECT {SelectColumns} FROM criteria c WHERE c.id = @id", db);
        cmd.Parameters.AddWithValue("id", id);

        using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return CriteriaMapper.MapFromReader(reader);
    }

    public async Task<CriterionInfo> CreateAsync(
        string name,
        string? description,
        CriterionDataType dataType,
        List<string>? enumValues,
        string? unit,
        IReadOnlyList<string> resourceTypeKeys)
    {
        if (resourceTypeKeys is null || resourceTypeKeys.Count == 0)
            throw new ArgumentException("At least one applicability value is required.", nameof(resourceTypeKeys));

        await using var db = _connectionFactory.CreateOrgConnection(_orgContext);
        await db.OpenAsync();
        await using var tx = await db.BeginTransactionAsync();

        try
        {
            // Insert criterion row.
            var insertCriterion = new NpgsqlCommand(
                @"INSERT INTO criteria (name, description, data_type, enum_values, unit)
                  VALUES (@name, @description, @data_type, @enum_values::jsonb, @unit)
                  ON CONFLICT ((LOWER(name))) DO NOTHING
                  RETURNING id",
                db, tx);
            insertCriterion.Parameters.AddWithValue("name", name);
            insertCriterion.Parameters.AddWithValue("description", (object?)description ?? DBNull.Value);
            insertCriterion.Parameters.AddWithValue("data_type", dataType.ToString());
            insertCriterion.Parameters.AddWithValue("enum_values",
                enumValues != null ? JsonSerializer.Serialize(enumValues) : DBNull.Value);
            insertCriterion.Parameters.AddWithValue("unit", (object?)unit ?? DBNull.Value);

            var idObj = await insertCriterion.ExecuteScalarAsync();
            if (idObj is null)
                throw new InvalidOperationException("A criterion with this name already exists");
            var criterionId = (Guid)idObj;

            // Insert applicability rows. Resolve resource_type_ids by key one
            // by one — this keeps the SQL simple and gives us per-key error
            // messages if anything is unknown. Validation runs upstream so all
            // keys are guaranteed to be in the known set; this is the DB-level
            // backstop.
            var keys = resourceTypeKeys.Distinct(StringComparer.Ordinal).ToArray();
            foreach (var key in keys)
            {
                // Resolve key → id first, then insert. The two-step approach
                // works around Npgsql VARCHAR/text parameter type inference
                // edge cases observed with single-statement INSERT … SELECT.
                var lookup = new NpgsqlCommand(
                    "SELECT id FROM resource_types WHERE key = @key", db, tx);
                lookup.Parameters.AddWithValue("key", key);
                var idObj2 = await lookup.ExecuteScalarAsync();
                if (idObj2 is null)
                    throw new ArgumentException(
                        $"Applicability key did not match a known resource type: '{key}'");
                var resourceTypeId = (Guid)idObj2;

                var ins = new NpgsqlCommand(
                    @"INSERT INTO criterion_resource_types (criterion_id, resource_type_id)
                      VALUES (@criterionId, @resourceTypeId)
                      ON CONFLICT DO NOTHING",
                    db, tx);
                ins.Parameters.AddWithValue("criterionId", criterionId);
                ins.Parameters.AddWithValue("resourceTypeId", resourceTypeId);
                await ins.ExecuteNonQueryAsync();
            }

            // Re-select with the aggregate column populated so the response
            // shape matches every other read path.
            var fetch = new NpgsqlCommand(
                $"SELECT {SelectColumns} FROM criteria c WHERE c.id = @id", db, tx);
            fetch.Parameters.AddWithValue("id", criterionId);

            await using var reader = await fetch.ExecuteReaderAsync();
            await reader.ReadAsync();
            var created = CriteriaMapper.MapFromReader(reader);
            await reader.CloseAsync();

            await tx.CommitAsync();
            return created;
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    public async Task<CriterionInfo?> UpdateAsync(Guid id, string? description, List<string>? enumValues, string? unit)
    {
        await using var db = _connectionFactory.CreateOrgConnection(_orgContext);
        await db.OpenAsync();

        // Check if criterion exists and get its data type
        var checkCmd = new NpgsqlCommand("SELECT data_type FROM criteria WHERE id = @id", db);
        checkCmd.Parameters.AddWithValue("id", id);
        var existingDataType = await checkCmd.ExecuteScalarAsync() as string;
        if (existingDataType == null)
        {
            return null;
        }

        // Build dynamic update query
        var updateFields = new List<string>();
        var cmd = new NpgsqlCommand();
        cmd.Connection = db;
        cmd.Parameters.AddWithValue("id", id);

        if (description != null)
        {
            updateFields.Add("description = @description");
            cmd.Parameters.AddWithValue("description", description);
        }

        if (enumValues != null)
        {
            // Only allow updating enum_values for Enum type
            if (existingDataType != "Enum")
            {
                throw new ArgumentException("Cannot set enum values for non-Enum criterion");
            }
            updateFields.Add("enum_values = @enum_values::jsonb");
            cmd.Parameters.AddWithValue("enum_values", JsonSerializer.Serialize(enumValues));
        }

        if (unit != null)
        {
            updateFields.Add("unit = @unit");
            cmd.Parameters.AddWithValue("unit", unit);
        }

        if (updateFields.Count == 0)
        {
            throw new ArgumentException("No fields to update");
        }

        updateFields.Add("updated_at = CURRENT_TIMESTAMP");

        cmd.CommandText =
            $"UPDATE criteria SET {string.Join(", ", updateFields)} WHERE id = @id";
        await cmd.ExecuteNonQueryAsync();

        // Re-select via GetByIdAsync to return the full DTO with aggregate columns.
        return await GetByIdAsync(id);
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        await using var db = _connectionFactory.CreateOrgConnection(_orgContext);
        await db.OpenAsync();
        var rowsAffected = await DbQueryHelper.ExecuteDeleteAsync(db, "DELETE FROM criteria WHERE id = @id", id);
        return rowsAffected > 0;
    }
}
