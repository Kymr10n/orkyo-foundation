using System.Text.Json;
using Api.Helpers;
using Api.Models;
using Api.Services;
using Npgsql;

namespace Api.Repositories;

public class SpaceRepository : ISpaceRepository
{
    // Reads name and description from resources (Phase 2: spaces.name/description moved to resources).
    private const string SelectColumns =
        "s.id, s.site_id, r.name, s.code, r.description, s.is_physical, s.geometry, s.properties, s.capacity, s.group_id, s.created_at, s.updated_at";

    private const string FromJoin = "FROM spaces s JOIN resources r ON r.id = s.id";

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
            $"SELECT {SelectColumns} {FromJoin} WHERE s.site_id = @siteId ORDER BY s.code, r.name LIMIT 1000",
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
            querySql: $"SELECT {SelectColumns} {FromJoin} WHERE s.site_id = @siteId ORDER BY s.code, r.name LIMIT @limit OFFSET @offset",
            addParams: cmd => cmd.Parameters.AddWithValue("siteId", siteId),
            mapper: SpaceMapper.MapFromReader);
    }

    public async Task<SpaceInfo?> GetByIdAsync(Guid siteId, Guid resourceId)
    {
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            $"SELECT {SelectColumns} {FromJoin} WHERE s.site_id = @siteId AND s.id = @resourceId",
            conn);
        cmd.Parameters.AddWithValue("siteId", siteId);
        cmd.Parameters.AddWithValue("resourceId", resourceId);

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

    public async Task<SpaceInfo> CreateAsync(Guid resourceId, Guid siteId, string? code, bool isPhysical, SpaceGeometry? geometry, Dictionary<string, object>? properties, int capacity = 1)
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
                throw new ConflictException($"Space code '{code}' already exists for this site");
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
            $"INSERT INTO spaces (id, site_id, code, is_physical, geometry, properties, capacity) VALUES (@resourceId, @siteId, @code, @isPhysical, @geometry::jsonb, @properties::jsonb, @capacity)",
            conn);

        cmd.Parameters.AddWithValue("resourceId", resourceId);
        cmd.Parameters.AddWithValue("siteId", siteId);
        cmd.Parameters.AddWithValue("code", (object?)code ?? DBNull.Value);
        cmd.Parameters.AddWithValue("isPhysical", isPhysical);
        cmd.Parameters.AddWithValue("geometry", (object?)geometryJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("properties", propertiesJson);
        cmd.Parameters.AddWithValue("capacity", capacity);

        await cmd.ExecuteNonQueryAsync();

        // Fetch the created space via JOIN to get name/description from resources.
        await using var sel = new NpgsqlCommand(
            $"SELECT {SelectColumns} {FromJoin} WHERE s.id = @resourceId AND s.site_id = @siteId",
            conn);
        sel.Parameters.AddWithValue("resourceId", resourceId);
        sel.Parameters.AddWithValue("siteId", siteId);
        await using var reader = await sel.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            throw new InvalidOperationException("Failed to create space");
        return SpaceMapper.MapFromReader(reader);
    }

    public async Task<SpaceInfo?> UpdateAsync(Guid siteId, Guid resourceId, string? code, SpaceGeometry? geometry, Dictionary<string, object>? properties, Guid? groupId = null, int? capacity = null)
    {
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);
        await conn.OpenAsync();

        // Check if space exists
        await using var checkCmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM spaces WHERE site_id = @siteId AND id = @resourceId",
            conn);
        checkCmd.Parameters.AddWithValue("siteId", siteId);
        checkCmd.Parameters.AddWithValue("resourceId", resourceId);

        var exists = ((long)(await checkCmd.ExecuteScalarAsync() ?? 0L)) > 0;
        if (!exists)
        {
            return null;
        }

        // Build dynamic update query
        var updates = new List<string>();
        var parameters = new List<(string name, object? value)>();

        if (code != null)
        {
            updates.Add("code = @code");
            parameters.Add(("code", code));
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
            return await GetByIdAsync(siteId, resourceId);
        }

        // Execute update
        var sql = $"UPDATE spaces SET {string.Join(", ", updates)}, updated_at = NOW() WHERE site_id = @siteId AND id = @resourceId";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("siteId", siteId);
        cmd.Parameters.AddWithValue("resourceId", resourceId);

        foreach (var (paramName, value) in parameters)
        {
            cmd.Parameters.AddWithValue(paramName, value ?? DBNull.Value);
        }

        await cmd.ExecuteNonQueryAsync();
        return await GetByIdAsync(siteId, resourceId);
    }

    public async Task<bool> DeleteAsync(Guid siteId, Guid resourceId)
    {
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "DELETE FROM spaces WHERE site_id = @siteId AND id = @resourceId",
            conn);
        cmd.Parameters.AddWithValue("siteId", siteId);
        cmd.Parameters.AddWithValue("resourceId", resourceId);

        var rowsAffected = await cmd.ExecuteNonQueryAsync();
        return rowsAffected > 0;
    }
}
