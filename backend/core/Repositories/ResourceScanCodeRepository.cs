using Api.Helpers;
using Api.Models;
using Api.Services;
using Npgsql;

namespace Api.Repositories;

public interface IResourceScanCodeRepository
{
    /// <summary>The resource a code names, with its type switch, or null for an unknown code.</summary>
    Task<ScanCodeMatch?> GetByCodeAsync(string code, CancellationToken ct = default);
    Task<List<ResourceScanCodeInfo>> GetByResourceAsync(Guid resourceId, CancellationToken ct = default);
    /// <summary>Null when the code already exists — a concurrent link won; re-read it.</summary>
    Task<ResourceScanCodeInfo?> InsertAsync(Guid resourceId, string code, Guid? userId, CancellationToken ct = default);
    /// <summary>Points an existing code at another resource. Null when the code is gone.</summary>
    Task<ResourceScanCodeInfo?> ReassignAsync(Guid codeId, Guid resourceId, Guid? userId, CancellationToken ct = default);
    /// <summary>False when the code does not exist or belongs to a different resource.</summary>
    Task<bool> DeleteAsync(Guid resourceId, Guid codeId, CancellationToken ct = default);
}

public class ResourceScanCodeRepository(OrgContext orgContext, IOrgDbConnectionFactory connectionFactory)
    : IResourceScanCodeRepository
{
    private const string SelectColumns = "id, resource_id, code, created_at";

    public async Task<ScanCodeMatch?> GetByCodeAsync(string code, CancellationToken ct = default)
    {
        await using var conn = connectionFactory.CreateOrgConnection(orgContext);
        return await conn.QuerySingleOrDefaultAsync(
            @"SELECT c.id, c.resource_id, c.code, c.created_at, r.name, rt.key, r.is_active, rt.scan_codes_enabled
                FROM resource_scan_codes c
                JOIN resources r       ON r.id = c.resource_id
                JOIN resource_types rt ON rt.id = r.resource_type_id
               WHERE c.code = @code",
            p => p.AddWithValue("code", code),
            r => new ScanCodeMatch(
                Map(r),
                new ScanCodeResourceRef
                {
                    Id = r.GetGuid("resource_id"),
                    Name = r.GetString("name"),
                    ResourceTypeKey = r.GetString("key"),
                    IsActive = r.GetBoolean("is_active"),
                },
                r.GetBoolean("scan_codes_enabled")),
            ct);
    }

    public async Task<List<ResourceScanCodeInfo>> GetByResourceAsync(Guid resourceId, CancellationToken ct = default)
    {
        await using var conn = connectionFactory.CreateOrgConnection(orgContext);
        return await conn.QueryListAsync(
            $"SELECT {SelectColumns} FROM resource_scan_codes WHERE resource_id = @resourceId ORDER BY created_at, code",
            p => p.AddWithValue("resourceId", resourceId), Map, ct);
    }

    public async Task<ResourceScanCodeInfo?> InsertAsync(Guid resourceId, string code, Guid? userId, CancellationToken ct = default)
    {
        await using var conn = connectionFactory.CreateOrgConnection(orgContext);
        return await conn.QuerySingleOrDefaultAsync(
            $@"INSERT INTO resource_scan_codes (resource_id, code, created_by_user_id)
               VALUES (@resourceId, @code, @userId)
               ON CONFLICT (code) DO NOTHING
               RETURNING {SelectColumns}",
            p =>
            {
                p.AddWithValue("resourceId", resourceId);
                p.AddWithValue("code", code);
                p.AddNullable("userId", userId);
            }, Map, ct);
    }

    public async Task<ResourceScanCodeInfo?> ReassignAsync(Guid codeId, Guid resourceId, Guid? userId, CancellationToken ct = default)
    {
        await using var conn = connectionFactory.CreateOrgConnection(orgContext);
        return await conn.QuerySingleOrDefaultAsync(
            $@"UPDATE resource_scan_codes
                  SET resource_id = @resourceId, created_by_user_id = @userId, created_at = now()
                WHERE id = @id
                RETURNING {SelectColumns}",
            p =>
            {
                p.AddWithValue("id", codeId);
                p.AddWithValue("resourceId", resourceId);
                p.AddNullable("userId", userId);
            }, Map, ct);
    }

    public async Task<bool> DeleteAsync(Guid resourceId, Guid codeId, CancellationToken ct = default)
    {
        await using var conn = connectionFactory.CreateOrgConnection(orgContext);
        return await conn.ExecuteAsync(
            "DELETE FROM resource_scan_codes WHERE id = @id AND resource_id = @resourceId",
            p =>
            {
                p.AddWithValue("id", codeId);
                p.AddWithValue("resourceId", resourceId);
            }, ct) > 0;
    }

    private static ResourceScanCodeInfo Map(NpgsqlDataReader r) => new()
    {
        Id = r.GetGuid("id"),
        ResourceId = r.GetGuid("resource_id"),
        Code = r.GetString("code"),
        CreatedAt = r.GetDateTime("created_at"),
    };
}
