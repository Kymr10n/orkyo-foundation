using System.Text.Json;
using Api.Helpers;
using Api.Models;
using Api.Services;
using Npgsql;

namespace Api.Repositories;

public class SpaceRepository : ISpaceRepository
{
    // Every column lives on resources now; the spaces side table is gone. home_site_id is
    // aliased to site_id so SpaceInfo and its mapper are unchanged — a space is simply a
    // resource whose home site is set and which may not travel.
    // group_id comes from resource_group_members (single source of truth for membership);
    // the single-group guard ensures the LEFT JOIN yields at most one row per space.
    private const string SelectColumns =
        "r.id, r.home_site_id AS site_id, r.name, r.code, r.description, r.is_physical, " +
        "r.geometry, r.properties, r.capacity, rgm.resource_group_id AS group_id, " +
        "r.created_at, r.updated_at";

    // Scoping that the spaces table used to provide for free. has_geometry rather than
    // key = 'space' so a tenant type declaring itself placeable is a first-class citizen here.
    // is_active replaces the presence of the spaces row: deleting a space used to remove that
    // row and deactivate the resource, so an inactive resource was already invisible.
    private const string FromJoin =
        "FROM resources r " +
        "JOIN resource_types rt ON rt.id = r.resource_type_id " +
        "LEFT JOIN resource_group_members rgm ON rgm.resource_id = r.id";

    private const string TypeScope = "rt.has_geometry AND r.is_active";

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
            $"SELECT {SelectColumns} {FromJoin} WHERE {TypeScope} AND r.home_site_id = @siteId ORDER BY r.code, r.name LIMIT 1000",
            p => p.AddWithValue("siteId", siteId), SpaceMapper.MapFromReader, ct);
    }

    public async Task<Dictionary<Guid, List<SpaceInfo>>> GetBySitesAsync(IReadOnlyList<Guid> siteIds, CancellationToken ct = default)
    {
        if (siteIds.Count == 0) return [];

        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);
        var spaces = await conn.QueryListAsync(
            $"SELECT {SelectColumns} {FromJoin} WHERE {TypeScope} AND r.home_site_id = ANY(@siteIds) ORDER BY r.home_site_id, r.code, r.name",
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
            countSql: $"SELECT COUNT(*) {FromJoin} WHERE {TypeScope} AND r.home_site_id = @siteId",
            querySql: $"SELECT {SelectColumns} {FromJoin} WHERE {TypeScope} AND r.home_site_id = @siteId ORDER BY r.code, r.name LIMIT @limit OFFSET @offset",
            bind: p => p.AddWithValue("siteId", siteId),
            map: SpaceMapper.MapFromReader,
            ct: ct);
    }

    public async Task<SpaceInfo?> GetByIdAsync(Guid siteId, Guid resourceId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);
        return await conn.QuerySingleOrDefaultAsync(
            $"SELECT {SelectColumns} {FromJoin} WHERE {TypeScope} AND r.home_site_id = @siteId AND r.id = @resourceId",
            p => { p.AddWithValue("siteId", siteId); p.AddWithValue("resourceId", resourceId); },
            SpaceMapper.MapFromReader, ct);
    }

    public async Task<int> GetEstimatedCountAsync(CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);
        return (int)await conn.ExecuteScalarAsync<long>(
            $"SELECT COUNT(*) {FromJoin} WHERE {TypeScope}", null, ct);
    }

    public async Task<SpaceInfo> CreateAsync(Guid resourceId, Guid siteId, string? code, bool isPhysical, SpaceGeometry? geometry, Dictionary<string, object>? properties, int capacity = 1, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);
        await conn.OpenAsync(ct);

        // Check if code already exists for this site
        if (!string.IsNullOrWhiteSpace(code))
        {
            await using var checkCmd = new NpgsqlCommand(
                $"SELECT COUNT(*) {FromJoin} WHERE {TypeScope} AND r.home_site_id = @siteId AND r.code = @code",
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

        // SpaceService has already created the resources row; with the side table gone,
        // "creating a space" is filling in that row's placement columns, including the home
        // site it deliberately left null.
        await using var cmd = new NpgsqlCommand(
            "UPDATE resources SET home_site_id = @siteId, code = @code, is_physical = @isPhysical, " +
            "geometry = @geometry::jsonb, properties = @properties::jsonb, capacity = @capacity " +
            "WHERE id = @resourceId",
            conn);

        cmd.Parameters.AddWithValue("resourceId", resourceId);
        cmd.Parameters.AddWithValue("siteId", siteId);
        cmd.Parameters.AddWithValue("code", (object?)code ?? DBNull.Value);
        cmd.Parameters.AddWithValue("isPhysical", isPhysical);
        cmd.Parameters.AddWithValue("geometry", (object?)geometryJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("properties", propertiesJson);
        cmd.Parameters.AddWithValue("capacity", capacity);

        await cmd.ExecuteNonQueryAsync(ct);

        await using var sel = new NpgsqlCommand(
            $"SELECT {SelectColumns} {FromJoin} WHERE {TypeScope} AND r.id = @resourceId AND r.home_site_id = @siteId",
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
            $"SELECT COUNT(*) {FromJoin} WHERE {TypeScope} AND r.home_site_id = @siteId AND r.id = @resourceId",
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
        // No explicit updated_at: the resources_updated_at BEFORE UPDATE trigger maintains it,
        // where the spaces table needed its own.
        var sql = $"UPDATE resources SET {string.Join(", ", updates)} WHERE home_site_id = @siteId AND id = @resourceId";

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

        // Deleting a space was always a deactivation dressed up as a delete: it dropped the
        // spaces row and deactivated the resource, keeping the resource for its assignment
        // history. With one table left, the deactivation is the whole operation.
        await using var cmd = new NpgsqlCommand(
            "UPDATE resources r SET is_active = false " +
            "FROM resource_types rt WHERE rt.id = r.resource_type_id " +
            $"AND {TypeScope} AND r.home_site_id = @siteId AND r.id = @resourceId",
            conn);
        cmd.Parameters.AddWithValue("siteId", siteId);
        cmd.Parameters.AddWithValue("resourceId", resourceId);

        var rowsAffected = await cmd.ExecuteNonQueryAsync(ct);
        return rowsAffected > 0;
    }
}
