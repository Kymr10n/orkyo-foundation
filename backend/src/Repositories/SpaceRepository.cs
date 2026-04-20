using System.Text.Json;
using Api.Helpers;
using Api.Models;
using Api.Services;
using Npgsql;

namespace Api.Repositories;

public class SpaceRepository : ISpaceRepository
{
    private const string SelectColumns =
        "id, site_id, name, code, description, is_physical, geometry, properties, capacity, group_id, created_at, updated_at";

    private readonly OrgContext _orgContext;
    private readonly IOrgDbConnectionFactory _connectionFactory;

    public SpaceRepository(
        OrgContext orgContext,
        IOrgDbConnectionFactory connectionFactory)
    {
        _orgContext = orgContext;
        _connectionFactory = connectionFactory;
    }

    public async Task<List<SpaceInfo>> GetAllAsync(Guid siteId)
    {
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            $"SELECT {SelectColumns} FROM spaces WHERE site_id = @siteId ORDER BY code, name LIMIT 1000",
            conn);
        cmd.Parameters.AddWithValue("siteId", siteId);

        var spacesList = new List<SpaceInfo>();

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            spacesList.Add(SpaceMapper.MapFromReader(reader));
        }

        return spacesList;
    }

    public async Task<PagedResult<SpaceInfo>> GetAllAsync(Guid siteId, PageRequest page)
    {
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);
        await conn.OpenAsync();

        return await DbQueryHelper.ExecutePagedQueryAsync(
            conn,
            page,
            countSql: "SELECT COUNT(*) FROM spaces WHERE site_id = @siteId",
            querySql: $"SELECT {SelectColumns} FROM spaces WHERE site_id = @siteId ORDER BY code, name LIMIT @limit OFFSET @offset",
            addParams: cmd => cmd.Parameters.AddWithValue("siteId", siteId),
            mapper: SpaceMapper.MapFromReader);
    }

    public async Task<SpaceInfo?> GetByIdAsync(Guid siteId, Guid spaceId)
    {
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            $"SELECT {SelectColumns} FROM spaces WHERE site_id = @siteId AND id = @spaceId",
            conn);
        cmd.Parameters.AddWithValue("siteId", siteId);
        cmd.Parameters.AddWithValue("spaceId", spaceId);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return SpaceMapper.MapFromReader(reader);
    }

    public async Task<int> GetEstimatedCountAsync()
    {
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "SELECT GREATEST(reltuples::bigint, 0) FROM pg_class WHERE relname = 'spaces'", conn);
        return (int)(long)(await cmd.ExecuteScalarAsync() ?? 0L);
    }

    public async Task<SpaceInfo> CreateAsync(Guid siteId, string name, string? code, string? description, bool isPhysical, SpaceGeometry? geometry, Dictionary<string, object>? properties, int capacity = 1)
    {
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);
        await conn.OpenAsync();

        // Check if code already exists for this site
        if (!string.IsNullOrWhiteSpace(code))
        {
            await using var checkCmd = new NpgsqlCommand(
                "SELECT COUNT(*) FROM spaces WHERE site_id = @siteId AND code = @code",
                conn);
            checkCmd.Parameters.AddWithValue("siteId", siteId);
            checkCmd.Parameters.AddWithValue("code", code);

            var count = (long)(await checkCmd.ExecuteScalarAsync() ?? 0L);
            if (count > 0)
            {
                throw new InvalidOperationException($"Space code '{code}' already exists for this site");
            }
        }

        // Serialize geometry and properties to JSON
        var geometryJson = geometry != null
            ? JsonSerializer.Serialize(geometry)
            : null;
        var propertiesJson = properties != null
            ? JsonSerializer.Serialize(properties)
            : "{}";

        // Insert space
        await using var cmd = new NpgsqlCommand(
            $"INSERT INTO spaces (site_id, name, code, description, is_physical, geometry, properties, capacity) VALUES (@siteId, @name, @code, @description, @isPhysical, @geometry::jsonb, @properties::jsonb, @capacity) RETURNING {SelectColumns}",
            conn);

        cmd.Parameters.AddWithValue("siteId", siteId);
        cmd.Parameters.AddWithValue("name", name);
        cmd.Parameters.AddWithValue("code", (object?)code ?? DBNull.Value);
        cmd.Parameters.AddWithValue("description", (object?)description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("isPhysical", isPhysical);
        cmd.Parameters.AddWithValue("geometry", (object?)geometryJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("properties", propertiesJson);
        cmd.Parameters.AddWithValue("capacity", capacity);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            throw new InvalidOperationException("Failed to create space");
        }

        return SpaceMapper.MapFromReader(reader);
    }

    public async Task<SpaceInfo?> UpdateAsync(Guid siteId, Guid spaceId, string? name, string? code, string? description, SpaceGeometry? geometry, Dictionary<string, object>? properties, Guid? groupId = null, int? capacity = null)
    {
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);
        await conn.OpenAsync();

        // Check if space exists
        await using var checkCmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM spaces WHERE site_id = @siteId AND id = @spaceId",
            conn);
        checkCmd.Parameters.AddWithValue("siteId", siteId);
        checkCmd.Parameters.AddWithValue("spaceId", spaceId);

        var exists = ((long)(await checkCmd.ExecuteScalarAsync() ?? 0L)) > 0;
        if (!exists)
        {
            return null;
        }

        // Build dynamic update query
        var updates = new List<string>();
        var parameters = new List<(string name, object? value)>();

        if (name != null)
        {
            updates.Add("name = @name");
            parameters.Add(("name", name));
        }

        if (code != null)
        {
            updates.Add("code = @code");
            parameters.Add(("code", code));
        }

        if (description != null)
        {
            updates.Add("description = @description");
            parameters.Add(("description", description));
        }

        if (geometry != null)
        {
            var geometryJson = JsonSerializer.Serialize(geometry);
            updates.Add("geometry = @geometry::jsonb");
            parameters.Add(("geometry", geometryJson));
        }

        if (properties != null)
        {
            var propertiesJson = JsonSerializer.Serialize(properties);
            updates.Add("properties = @properties::jsonb");
            parameters.Add(("properties", propertiesJson));
        }

        if (groupId != null)
        {
            updates.Add("group_id = @groupId");
            parameters.Add(("groupId", groupId));
        }

        if (capacity.HasValue)
        {
            updates.Add("capacity = @capacity");
            parameters.Add(("capacity", capacity.Value));
        }

        if (!updates.Any())
        {
            // No updates to perform, return current space
            return await GetByIdAsync(siteId, spaceId);
        }

        // Execute update
        var sql = $"UPDATE spaces SET {string.Join(", ", updates)}, updated_at = NOW() WHERE site_id = @siteId AND id = @spaceId RETURNING {SelectColumns}";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("siteId", siteId);
        cmd.Parameters.AddWithValue("spaceId", spaceId);

        foreach (var (paramName, value) in parameters)
        {
            cmd.Parameters.AddWithValue(paramName, value ?? DBNull.Value);
        }

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            throw new InvalidOperationException("Failed to update space");
        }

        return SpaceMapper.MapFromReader(reader);
    }

    public async Task<bool> DeleteAsync(Guid siteId, Guid spaceId)
    {
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "DELETE FROM spaces WHERE site_id = @siteId AND id = @spaceId",
            conn);
        cmd.Parameters.AddWithValue("siteId", siteId);
        cmd.Parameters.AddWithValue("spaceId", spaceId);

        var rowsAffected = await cmd.ExecuteNonQueryAsync();
        return rowsAffected > 0;
    }
}
