using Api.Helpers;
using Api.Models;
using Api.Services;
using Npgsql;

namespace Api.Repositories;

public class SiteRepository : ISiteRepository
{
    private const string SelectColumns =
        "id, code, name, description, address, created_at, updated_at";

    private readonly OrgContext _orgContext;
    private readonly IOrgDbConnectionFactory _connectionFactory;

    public SiteRepository(
        OrgContext orgContext,
        IOrgDbConnectionFactory connectionFactory)
    {
        _orgContext = orgContext;
        _connectionFactory = connectionFactory;
    }

    public async Task<List<SiteInfo>> GetAllAsync()
    {
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            $"SELECT {SelectColumns} FROM sites ORDER BY name LIMIT 200", conn);

        var sitesList = new List<SiteInfo>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            sitesList.Add(SiteMapper.MapFromReader(reader));
        }

        return sitesList;
    }

    public async Task<PagedResult<SiteInfo>> GetAllAsync(PageRequest page)
    {
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);
        await conn.OpenAsync();

        return await DbQueryHelper.ExecutePagedQueryAsync(
            conn,
            page,
            countSql: "SELECT COUNT(*) FROM sites",
            querySql: $"SELECT {SelectColumns} FROM sites ORDER BY name LIMIT @limit OFFSET @offset",
            addParams: null,
            mapper: SiteMapper.MapFromReader);
    }

    public async Task<SiteInfo?> GetByIdAsync(Guid siteId)
    {
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            $"SELECT {SelectColumns} FROM sites WHERE id = @siteId", conn);
        cmd.Parameters.AddWithValue("siteId", siteId);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return SiteMapper.MapFromReader(reader);
    }

    public async Task<int> GetEstimatedCountAsync()
    {
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "SELECT GREATEST(reltuples::bigint, 0) FROM pg_class WHERE relname = 'sites'", conn);
        return (int)(long)(await cmd.ExecuteScalarAsync() ?? 0L);
    }

    public async Task<SiteInfo> CreateAsync(string code, string name, string? description, string? address)
    {
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);
        await conn.OpenAsync();

        // Check if code already exists
        await using var checkCmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM sites WHERE code = @code", conn);
        checkCmd.Parameters.AddWithValue("code", code);
        var count = (long)(await checkCmd.ExecuteScalarAsync() ?? 0L);

        if (count > 0)
        {
            throw new InvalidOperationException("Site with this code already exists");
        }

        // Create the site
        var siteId = Guid.NewGuid();
        await using var cmd = new NpgsqlCommand(
            $"INSERT INTO sites (id, code, name, description, address, created_at, updated_at) VALUES (@id, @code, @name, @description, @address, NOW(), NOW()) RETURNING {SelectColumns}",
            conn);

        cmd.Parameters.AddWithValue("id", siteId);
        cmd.Parameters.AddWithValue("code", code);
        cmd.Parameters.AddWithValue("name", name);
        cmd.Parameters.AddWithValue("description", (object?)description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("address", (object?)address ?? DBNull.Value);

        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();

        return SiteMapper.MapFromReader(reader);
    }

    public async Task<SiteInfo?> UpdateAsync(Guid siteId, string code, string name, string? description, string? address)
    {
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);
        await conn.OpenAsync();

        // Check if another site has this code
        await using var checkCmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM sites WHERE code = @code AND id != @siteId", conn);
        checkCmd.Parameters.AddWithValue("code", code);
        checkCmd.Parameters.AddWithValue("siteId", siteId);
        var count = (long)(await checkCmd.ExecuteScalarAsync() ?? 0L);

        if (count > 0)
        {
            throw new InvalidOperationException("Another site with this code already exists");
        }

        await using var cmd = new NpgsqlCommand(
            $"UPDATE sites SET code = @code, name = @name, description = @description, address = @address, updated_at = NOW() WHERE id = @siteId RETURNING {SelectColumns}",
            conn);

        cmd.Parameters.AddWithValue("siteId", siteId);
        cmd.Parameters.AddWithValue("code", code);
        cmd.Parameters.AddWithValue("name", name);
        cmd.Parameters.AddWithValue("description", (object?)description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("address", (object?)address ?? DBNull.Value);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return SiteMapper.MapFromReader(reader);
    }

    public async Task<bool> DeleteAsync(Guid siteId)
    {
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);
        await conn.OpenAsync();
        var rowsAffected = await DbQueryHelper.ExecuteDeleteAsync(conn, "DELETE FROM sites WHERE id = @id", siteId);
        return rowsAffected > 0;
    }
}
