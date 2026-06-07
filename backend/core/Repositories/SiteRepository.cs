using Api.Helpers;
using Api.Models;
using Api.Security.Encryption;
using Api.Services;
using Npgsql;

namespace Api.Repositories;

public class SiteRepository : ISiteRepository
{
    private const string SelectColumns =
        "id, code, name, description, address, created_at, updated_at";

    private readonly OrgContext _orgContext;
    private readonly IOrgDbConnectionFactory _connectionFactory;
    private readonly IEncryptionService _encryption;

    public SiteRepository(
        OrgContext orgContext,
        IOrgDbConnectionFactory connectionFactory,
        IEncryptionService encryption)
    {
        _orgContext = orgContext;
        _connectionFactory = connectionFactory;
        _encryption = encryption;
    }

    // sites.description / sites.address hold confidential facility info → encrypted
    // at rest. code / name stay plaintext (used for search, sort, uniqueness).
    private string? Enc(string? value) => _encryption.ProtectString(value, _orgContext.OrgId);

    private SiteInfo Dec(SiteInfo s) => s with
    {
        Description = _encryption.UnprotectString(s.Description, _orgContext.OrgId),
        Address = _encryption.UnprotectString(s.Address, _orgContext.OrgId),
    };

    public async Task<List<SiteInfo>> GetAllAsync(CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);
        var sites = await conn.QueryListAsync(
            $"SELECT {SelectColumns} FROM sites ORDER BY name LIMIT 200", null,
            SiteMapper.MapFromReader, ct);
        return sites.Select(Dec).ToList();
    }

    public async Task<PagedResult<SiteInfo>> GetAllAsync(PageRequest page, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);
        var result = await conn.QueryPagedAsync(
            page,
            countSql: "SELECT COUNT(*) FROM sites",
            querySql: $"SELECT {SelectColumns} FROM sites ORDER BY name LIMIT @limit OFFSET @offset",
            bind: null,
            map: SiteMapper.MapFromReader,
            ct: ct);
        return result with { Items = result.Items.Select(Dec).ToList() };
    }

    public async Task<SiteInfo?> GetByIdAsync(Guid siteId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);
        var site = await conn.QuerySingleOrDefaultAsync(
            $"SELECT {SelectColumns} FROM sites WHERE id = @siteId",
            p => p.AddWithValue("siteId", siteId), SiteMapper.MapFromReader, ct);
        return site is null ? null : Dec(site);
    }

    public async Task<int> GetEstimatedCountAsync(CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);
        return (int)await conn.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM sites", null, ct);
    }

    public async Task<SiteInfo> CreateAsync(string code, string name, string? description, string? address, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);
        await conn.OpenAsync(ct);

        // Check if code already exists
        if (await conn.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM sites WHERE code = @code",
                p => p.AddWithValue("code", code), ct) > 0)
            throw new ConflictException("Site with this code already exists");

        return Dec((await conn.QuerySingleOrDefaultAsync(
            $"INSERT INTO sites (id, code, name, description, address, created_at, updated_at) VALUES (@id, @code, @name, @description, @address, NOW(), NOW()) RETURNING {SelectColumns}",
            p =>
            {
                p.AddWithValue("id", Guid.NewGuid());
                p.AddWithValue("code", code);
                p.AddWithValue("name", name);
                p.AddNullable("description", Enc(description));
                p.AddNullable("address", Enc(address));
            }, SiteMapper.MapFromReader, ct))!);
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

        var updated = await conn.QuerySingleOrDefaultAsync(
            $"UPDATE sites SET code = @code, name = @name, description = @description, address = @address, updated_at = NOW() WHERE id = @siteId RETURNING {SelectColumns}",
            p =>
            {
                p.AddWithValue("siteId", siteId);
                p.AddWithValue("code", code);
                p.AddWithValue("name", name);
                p.AddNullable("description", Enc(description));
                p.AddNullable("address", Enc(address));
            }, SiteMapper.MapFromReader, ct);
        return updated is null ? null : Dec(updated);
    }

    public async Task<bool> DeleteAsync(Guid siteId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);
        return await conn.ExecuteAsync("DELETE FROM sites WHERE id = @id",
            p => p.AddWithValue("id", siteId), ct) > 0;
    }
}
