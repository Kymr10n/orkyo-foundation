using Api.Models;
using Api.Services;
using Npgsql;

namespace Api.Repositories;

public interface IResourceTypeRepository
{
    Task<List<ResourceTypeInfo>> GetAllAsync(CancellationToken ct = default);
    Task<ResourceTypeInfo?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<ResourceTypeInfo?> GetByKeyAsync(string key, CancellationToken ct = default);
}

public class ResourceTypeRepository(OrgContext orgContext, IOrgDbConnectionFactory connectionFactory)
    : IResourceTypeRepository
{
    private const string SelectColumns =
        "id, key, display_name, description, is_system, is_active, created_at, updated_at";

    public async Task<List<ResourceTypeInfo>> GetAllAsync(CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        await db.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(
            $"SELECT {SelectColumns} FROM resource_types ORDER BY display_name", db);

        var result = new List<ResourceTypeInfo>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            result.Add(Map(reader));
        return result;
    }

    public async Task<ResourceTypeInfo?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        await db.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(
            $"SELECT {SelectColumns} FROM resource_types WHERE id = @id", db);
        cmd.Parameters.AddWithValue("id", id);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Map(reader) : null;
    }

    public async Task<ResourceTypeInfo?> GetByKeyAsync(string key, CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        await db.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(
            $"SELECT {SelectColumns} FROM resource_types WHERE key = @key", db);
        cmd.Parameters.AddWithValue("key", key);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Map(reader) : null;
    }

    private static ResourceTypeInfo Map(NpgsqlDataReader r) => new()
    {
        Id = r.GetGuid(r.GetOrdinal("id")),
        Key = r.GetString(r.GetOrdinal("key")),
        DisplayName = r.GetString(r.GetOrdinal("display_name")),
        Description = r.IsDBNull(r.GetOrdinal("description")) ? null : r.GetString(r.GetOrdinal("description")),
        IsSystem = r.GetBoolean(r.GetOrdinal("is_system")),
        IsActive = r.GetBoolean(r.GetOrdinal("is_active")),
        CreatedAt = r.GetDateTime(r.GetOrdinal("created_at")),
        UpdatedAt = r.GetDateTime(r.GetOrdinal("updated_at")),
    };
}
