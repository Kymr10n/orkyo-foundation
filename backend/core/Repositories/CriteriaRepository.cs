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

    public async Task<List<CriterionInfo>> GetAllAsync(CancellationToken ct = default)
    {
        await using var db = _connectionFactory.CreateOrgConnection(_orgContext);
        await db.OpenAsync(ct);

        var criteria = new List<CriterionInfo>();
        var cmd = new NpgsqlCommand(
            $"SELECT {SelectColumns} FROM criteria c ORDER BY c.name LIMIT 500", db);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            criteria.Add(CriteriaMapper.MapFromReader(reader));
        }

        return criteria;
    }

    public async Task<List<CriterionInfo>> GetByResourceTypeAsync(string resourceTypeKey, CancellationToken ct = default)
    {
        await using var db = _connectionFactory.CreateOrgConnection(_orgContext);
        await db.OpenAsync(ct);

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
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            criteria.Add(CriteriaMapper.MapFromReader(reader));
        return criteria;
    }

    public async Task<PagedResult<CriterionInfo>> GetAllAsync(PageRequest page, CancellationToken ct = default)
    {
        await using var db = _connectionFactory.CreateOrgConnection(_orgContext);
        return await db.QueryPagedAsync(
            page,
            countSql: "SELECT COUNT(*) FROM criteria",
            querySql: $"SELECT {SelectColumns} FROM criteria c ORDER BY c.name LIMIT @limit OFFSET @offset",
            bind: null,
            map: CriteriaMapper.MapFromReader,
            ct: ct);
    }

    public async Task<CriterionInfo?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = _connectionFactory.CreateOrgConnection(_orgContext);
        return await db.QuerySingleOrDefaultAsync(
            $"SELECT {SelectColumns} FROM criteria c WHERE c.id = @id",
            p => p.AddWithValue("id", id), CriteriaMapper.MapFromReader, ct);
    }

    public async Task<CriterionInfo> CreateAsync(
        string name,
        string? description,
        CriterionDataType dataType,
        List<string>? enumValues,
        string? unit,
        IReadOnlyList<string> resourceTypeKeys, CancellationToken ct = default)
    {
        if (resourceTypeKeys is null || resourceTypeKeys.Count == 0)
            throw new ArgumentException("At least one applicability value is required.", nameof(resourceTypeKeys));

        await using var db = _connectionFactory.CreateOrgConnection(_orgContext);
        await db.OpenAsync(ct);
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

            var idObj = await insertCriterion.ExecuteScalarAsync(ct);
            if (idObj is null)
                throw new ConflictException("A criterion with this name already exists");
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
                var idObj2 = await lookup.ExecuteScalarAsync(ct);
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
                await ins.ExecuteNonQueryAsync(ct);
            }

            // Re-select with the aggregate column populated so the response
            // shape matches every other read path.
            var fetch = new NpgsqlCommand(
                $"SELECT {SelectColumns} FROM criteria c WHERE c.id = @id", db, tx);
            fetch.Parameters.AddWithValue("id", criterionId);

            await using var reader = await fetch.ExecuteReaderAsync(ct);
            await reader.ReadAsync(ct);
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

    public async Task<CriterionInfo?> UpdateAsync(Guid id, string? description, List<string>? enumValues, string? unit, CancellationToken ct = default)
    {
        await using var db = _connectionFactory.CreateOrgConnection(_orgContext);
        await db.OpenAsync(ct);

        // Check if criterion exists and get its data type
        var checkCmd = new NpgsqlCommand("SELECT data_type FROM criteria WHERE id = @id", db);
        checkCmd.Parameters.AddWithValue("id", id);
        var existingDataType = await checkCmd.ExecuteScalarAsync(ct) as string;
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
        await cmd.ExecuteNonQueryAsync(ct);

        // Re-select via GetByIdAsync to return the full DTO with aggregate columns.
        return await GetByIdAsync(id);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = _connectionFactory.CreateOrgConnection(_orgContext);
        await db.OpenAsync(ct);

        // All FKs to criteria(id) are ON DELETE CASCADE, so a raw DELETE would
        // silently destroy capabilities and requirements that reference this
        // criterion. Guard against that by counting references first and refusing
        // the delete when any exist; the user must clear assignments explicitly.
        await using var checkCmd = new NpgsqlCommand(
            "SELECT " +
            "  (SELECT COUNT(*) FROM resource_capabilities       WHERE criterion_id = @id) AS resources, " +
            "  (SELECT COUNT(*) FROM resource_group_capabilities WHERE criterion_id = @id) AS groups, " +
            "  (SELECT COUNT(*) FROM request_requirements        WHERE criterion_id = @id) AS requests, " +
            "  (SELECT COUNT(*) FROM request_template_requirements WHERE criterion_id = @id) AS request_templates, " +
            "  (SELECT COUNT(*) FROM template_items              WHERE criterion_id = @id) AS templates",
            db);
        checkCmd.Parameters.AddWithValue("id", id);

        long resources, groups, requests, requestTemplates, templates;
        await using (var reader = await checkCmd.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct))
                throw new InvalidOperationException("Reference count query returned no rows");
            resources = reader.GetInt64(0);
            groups = reader.GetInt64(1);
            requests = reader.GetInt64(2);
            requestTemplates = reader.GetInt64(3);
            templates = reader.GetInt64(4);
        }

        var parts = new List<string>();
        if (resources > 0) parts.Add($"{resources} resource assignment{(resources == 1 ? "" : "s")}");
        if (groups > 0) parts.Add($"{groups} group assignment{(groups == 1 ? "" : "s")}");
        if (requests > 0) parts.Add($"{requests} request requirement{(requests == 1 ? "" : "s")}");
        if (requestTemplates > 0) parts.Add($"{requestTemplates} request template requirement{(requestTemplates == 1 ? "" : "s")}");
        if (templates > 0) parts.Add($"{templates} template item{(templates == 1 ? "" : "s")}");

        if (parts.Count > 0)
        {
            throw new ConflictException(
                $"Cannot delete criterion: still referenced by {string.Join(", ", parts)}. " +
                "Remove these assignments first.");
        }

        var rowsAffected = await db.ExecuteAsync("DELETE FROM criteria WHERE id = @id",
            p => p.AddWithValue("id", id), ct);
        return rowsAffected > 0;
    }
}
