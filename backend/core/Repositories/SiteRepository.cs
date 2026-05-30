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

    public async Task<List<SiteInfo>> GetAllAsync(CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);
        return await conn.QueryListAsync(
            $"SELECT {SelectColumns} FROM sites ORDER BY name LIMIT 200", null,
            SiteMapper.MapFromReader, ct);
    }

    public async Task<PagedResult<SiteInfo>> GetAllAsync(PageRequest page, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);
        return await conn.QueryPagedAsync(
            page,
            countSql: "SELECT COUNT(*) FROM sites",
            querySql: $"SELECT {SelectColumns} FROM sites ORDER BY name LIMIT @limit OFFSET @offset",
            bind: null,
            map: SiteMapper.MapFromReader,
            ct: ct);
    }

    public async Task<SiteInfo?> GetByIdAsync(Guid siteId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);
        return await conn.QuerySingleOrDefaultAsync(
            $"SELECT {SelectColumns} FROM sites WHERE id = @siteId",
            p => p.AddWithValue("siteId", siteId), SiteMapper.MapFromReader, ct);
    }

    public async Task<int> GetEstimatedCountAsync(CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);
        return (int)await conn.ExecuteScalarAsync<long>(
            "SELECT GREATEST(reltuples::bigint, 0) FROM pg_class WHERE relname = 'sites'",
            null, ct);
    }

    public async Task<SiteInfo> CreateAsync(string code, string name, string? description, string? address, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);
        await conn.OpenAsync(ct);

        // Check if code already exists
        if (await conn.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM sites WHERE code = @code",
                p => p.AddWithValue("code", code), ct) > 0)
            throw new ConflictException("Site with this code already exists");

        return (await conn.QuerySingleOrDefaultAsync(
            $"INSERT INTO sites (id, code, name, description, address, created_at, updated_at) VALUES (@id, @code, @name, @description, @address, NOW(), NOW()) RETURNING {SelectColumns}",
            p =>
            {
                p.AddWithValue("id", Guid.NewGuid());
                p.AddWithValue("code", code);
                p.AddWithValue("name", name);
                p.AddNullable("description", description);
                p.AddNullable("address", address);
            }, SiteMapper.MapFromReader, ct))!;
    }

    public async Task<SiteInfo?> UpdateAsync(Guid siteId, string code, string name, string? description, string? address, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);
        await conn.OpenAsync(ct);

        // Check if another site has this code
        if (await conn.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM sites WHERE code = @code AND id != @siteId",
                p => { p.AddWithValue("code", code); p.AddWithValue("siteId", siteId); }, ct) > 0)
            throw new ConflictException("Another site with this code already exists");

        return await conn.QuerySingleOrDefaultAsync(
            $"UPDATE sites SET code = @code, name = @name, description = @description, address = @address, updated_at = NOW() WHERE id = @siteId RETURNING {SelectColumns}",
            p =>
            {
                p.AddWithValue("siteId", siteId);
                p.AddWithValue("code", code);
                p.AddWithValue("name", name);
                p.AddNullable("description", description);
                p.AddNullable("address", address);
            }, SiteMapper.MapFromReader, ct);
    }

    public async Task<bool> DeleteAsync(Guid siteId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);
        return await conn.ExecuteAsync("DELETE FROM sites WHERE id = @id",
            p => p.AddWithValue("id", siteId), ct) > 0;
    }
}
