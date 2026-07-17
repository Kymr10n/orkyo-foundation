using System.Text.Json;
using Api.Helpers;
using Api.Models;
using Api.Services;
using Npgsql;

namespace Api.Repositories;

public class SpaceRepository : ISpaceRepository
{
    // Reads name and description from resources (Phase 2: spaces.name/description moved to resources).
    // group_id comes from resource_group_members (single source of truth for membership);
    // the 1:1 space-group guard ensures the LEFT JOIN yields at most one row per space.
    private const string SelectColumns =
        "s.id, s.site_id, r.name, s.code, r.description, s.is_physical, s.geometry, s.properties, s.capacity, rgm.resource_group_id AS group_id, s.created_at, s.updated_at";

    private const string FromJoin =
        "FROM spaces s JOIN resources r ON r.id = s.id " +
        "LEFT JOIN resource_group_members rgm ON rgm.resource_id = s.id";

    private readonly OrgContext _orgContext;
    private readonly IOrgDbConnectionFactory _connectionFactory;

    public SpaceRepository(
        OrgContext orgContext,
        IOrgDbConnectionFactory connectionFactory)
    {
        _orgContext = orgContext;
        _connectionFactory = connectionFactory;
    }

    public async Task<List<SpaceInfo>> GetAllAsync(Guid siteId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);
        return await conn.QueryListAsync(
            $"SELECT {SelectColumns} {FromJoin} WHERE s.site_id = @siteId ORDER BY s.code, r.name LIMIT 1000",
            p => p.AddWithValue("siteId", siteId), SpaceMapper.MapFromReader, ct);
    }

    public async Task<Dictionary<Guid, List<SpaceInfo>>> GetBySitesAsync(IReadOnlyList<Guid> siteIds, CancellationToken ct = default)
    {
        if (siteIds.Count == 0) return [];

        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);
        var spaces = await conn.QueryListAsync(
            $"SELECT {SelectColumns} {FromJoin} WHERE s.site_id = ANY(@siteIds) ORDER BY s.site_id, s.code, r.name",
            p => p.AddWithValue("siteIds", siteIds.ToArray()), SpaceMapper.MapFromReader, ct);

        var map = new Dictionary<Guid, List<SpaceInfo>>();
        foreach (var space in spaces)
        {
            if (!map.TryGetValue(space.SiteId, out var list))
            {
                list = [];
                map[space.SiteId] = list;
            }
            list.Add(space);
        }
        return map;
    }

    public async Task<PagedResult<SpaceInfo>> GetAllAsync(Guid siteId, PageRequest page, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);
        return await conn.QueryPagedAsync(
            page,
            countSql: "SELECT COUNT(*) FROM spaces WHERE site_id = @siteId",
            querySql: $"SELECT {SelectColumns} {FromJoin} WHERE s.site_id = @siteId ORDER BY s.code, r.name LIMIT @limit OFFSET @offset",
            bind: p => p.AddWithValue("siteId", siteId),
            map: SpaceMapper.MapFromReader,
            ct: ct);
    }

    public async Task<SpaceInfo?> GetByIdAsync(Guid siteId, Guid resourceId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);
        return await conn.QuerySingleOrDefaultAsync(
            $"SELECT {SelectColumns} {FromJoin} WHERE s.site_id = @siteId AND s.id = @resourceId",
            p => { p.AddWithValue("siteId", siteId); p.AddWithValue("resourceId", resourceId); },
            SpaceMapper.MapFromReader, ct);
    }

    public async Task<int> GetEstimatedCountAsync(CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);
        return (int)await conn.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM spaces", null, ct);
    }

    public async Task<SpaceInfo> CreateAsync(Guid resourceId, Guid siteId, string? code, bool isPhysical, SpaceGeometry? geometry, Dictionary<string, object>? properties, int capacity = 1, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);
        await conn.OpenAsync(ct);

        // Check if code already exists for this site
        if (!string.IsNullOrWhiteSpace(code))
        {
            await using var checkCmd = new NpgsqlCommand(
                "SELECT COUNT(*) FROM spaces WHERE site_id = @siteId AND code = @code",
                conn);
            checkCmd.Parameters.AddWithValue("siteId", siteId);
            checkCmd.Parameters.AddWithValue("code", code);

            var count = (long)(await checkCmd.ExecuteScalarAsync(ct) ?? 0L);
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

        await cmd.ExecuteNonQueryAsync(ct);

        // Fetch the created space via JOIN to get name/description from resources.
        await using var sel = new NpgsqlCommand(
            $"SELECT {SelectColumns} {FromJoin} WHERE s.id = @resourceId AND s.site_id = @siteId",
            conn);
        sel.Parameters.AddWithValue("resourceId", resourceId);
        sel.Parameters.AddWithValue("siteId", siteId);
        await using var reader = await sel.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            throw new InvalidOperationException("Failed to create space");
        return SpaceMapper.MapFromReader(reader);
    }

    public async Task<SpaceInfo?> UpdateAsync(Guid siteId, Guid resourceId, string? code, SpaceGeometry? geometry, Dictionary<string, object>? properties, int? capacity = null, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);
        await conn.OpenAsync(ct);

        // Check if space exists
        await using var checkCmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM spaces WHERE site_id = @siteId AND id = @resourceId",
            conn);
        checkCmd.Parameters.AddWithValue("siteId", siteId);
        checkCmd.Parameters.AddWithValue("resourceId", resourceId);

        var exists = ((long)(await checkCmd.ExecuteScalarAsync(ct) ?? 0L)) > 0;
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

        if (capacity.HasValue)
        {
            updates.Add("capacity = @capacity");
            parameters.Add(("capacity", capacity.Value));
        }

        if (!updates.Any())
        {
            // No updates to perform, return current space
            return await GetByIdAsync(siteId, resourceId, ct);
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

        await cmd.ExecuteNonQueryAsync(ct);
        return await GetByIdAsync(siteId, resourceId, ct);
    }

    public async Task<bool> DeleteAsync(Guid siteId, Guid resourceId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(
            "DELETE FROM spaces WHERE site_id = @siteId AND id = @resourceId",
            conn);
        cmd.Parameters.AddWithValue("siteId", siteId);
        cmd.Parameters.AddWithValue("resourceId", resourceId);

        var rowsAffected = await cmd.ExecuteNonQueryAsync(ct);
        return rowsAffected > 0;
    }
}
