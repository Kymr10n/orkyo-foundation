using System.Text.Json;
using Api.Helpers;
using Api.Models;
using Api.Services;
using Npgsql;

namespace Api.Repositories;

public class CriteriaRepository : ICriteriaRepository
{
    private const string SelectColumns =
        "id, name, description, data_type, enum_values, unit, created_at, updated_at";

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
            $"SELECT {SelectColumns} FROM criteria ORDER BY name LIMIT 500", db);

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            criteria.Add(CriteriaMapper.MapFromReader(reader));
        }

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
            querySql: $"SELECT {SelectColumns} FROM criteria ORDER BY name LIMIT @limit OFFSET @offset",
            addParams: null,
            mapper: CriteriaMapper.MapFromReader);
    }

    public async Task<CriterionInfo?> GetByIdAsync(Guid id)
    {
        await using var db = _connectionFactory.CreateOrgConnection(_orgContext);
        await db.OpenAsync();

        var cmd = new NpgsqlCommand(
            $"SELECT {SelectColumns} FROM criteria WHERE id = @id", db);
        cmd.Parameters.AddWithValue("id", id);

        using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return CriteriaMapper.MapFromReader(reader);
    }

    public async Task<CriterionInfo> CreateAsync(string name, string? description, CriterionDataType dataType, List<string>? enumValues, string? unit)
    {
        await using var db = _connectionFactory.CreateOrgConnection(_orgContext);
        await db.OpenAsync();

        // Insert with conflict detection — single round-trip (#16)
        var cmd = new NpgsqlCommand(
            $@"INSERT INTO criteria (name, description, data_type, enum_values, unit)
               VALUES (@name, @description, @data_type, @enum_values::jsonb, @unit)
               ON CONFLICT ((LOWER(name))) DO NOTHING
               RETURNING {SelectColumns}",
            db);

        cmd.Parameters.AddWithValue("name", name);
        cmd.Parameters.AddWithValue("description", (object?)description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("data_type", dataType.ToString());
        cmd.Parameters.AddWithValue("enum_values",
            enumValues != null ? JsonSerializer.Serialize(enumValues) : DBNull.Value);
        cmd.Parameters.AddWithValue("unit", (object?)unit ?? DBNull.Value);

        using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            throw new InvalidOperationException("A criterion with this name already exists");
        }
        return CriteriaMapper.MapFromReader(reader);
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
            $"UPDATE criteria SET {string.Join(", ", updateFields)} WHERE id = @id RETURNING {SelectColumns}";

        using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        return CriteriaMapper.MapFromReader(reader);
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        await using var db = _connectionFactory.CreateOrgConnection(_orgContext);
        await db.OpenAsync();
        var rowsAffected = await DbQueryHelper.ExecuteDeleteAsync(db, "DELETE FROM criteria WHERE id = @id", id);
        return rowsAffected > 0;
    }
}
