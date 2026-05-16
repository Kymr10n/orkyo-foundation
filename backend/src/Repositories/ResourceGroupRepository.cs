using Api.Models;
using Api.Services;
using Npgsql;

namespace Api.Repositories;

public interface IResourceGroupRepository
{
    Task<List<ResourceGroupInfo>> GetByTypeKeyAsync(string resourceTypeKey);
    Task<ResourceGroupInfo?> GetByIdAsync(Guid id);
    Task<ResourceGroupInfo> CreateAsync(string resourceTypeKey, string name, string? description, int defaultAvailabilityPercent);
    Task<ResourceGroupInfo?> UpdateAsync(Guid id, string? name, string? description, int? defaultAvailabilityPercent);
    Task<bool> DeleteAsync(Guid id);
}

public class ResourceGroupRepository(OrgContext orgContext, IOrgDbConnectionFactory connectionFactory)
    : IResourceGroupRepository
{
    private const string SelectColumns =
        "g.id, g.name, g.description, g.default_availability_percent, " +
        "COUNT(m.resource_id) AS member_count, g.created_at, g.updated_at";

    public async Task<List<ResourceGroupInfo>> GetByTypeKeyAsync(string resourceTypeKey)
    {
        await using var conn = connectionFactory.CreateOrgConnection(orgContext);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            $"SELECT {SelectColumns} " +
            "FROM resource_groups g " +
            "JOIN resource_types rt ON rt.id = g.resource_type_id " +
            "LEFT JOIN resource_group_members m ON m.resource_group_id = g.id " +
            "WHERE rt.key = @resourceTypeKey " +
            "GROUP BY g.id, g.name, g.description, g.default_availability_percent, g.created_at, g.updated_at " +
            "ORDER BY g.name", conn);
        cmd.Parameters.AddWithValue("resourceTypeKey", resourceTypeKey);

        var groups = new List<ResourceGroupInfo>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            groups.Add(MapGroup(reader));
        return groups;
    }

    public async Task<ResourceGroupInfo?> GetByIdAsync(Guid id)
    {
        await using var conn = connectionFactory.CreateOrgConnection(orgContext);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            $"SELECT {SelectColumns} " +
            "FROM resource_groups g " +
            "LEFT JOIN resource_group_members m ON m.resource_group_id = g.id " +
            "WHERE g.id = @id " +
            "GROUP BY g.id, g.name, g.description, g.default_availability_percent, g.created_at, g.updated_at",
            conn);
        cmd.Parameters.AddWithValue("id", id);

        await using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? MapGroup(reader) : null;
    }

    public async Task<ResourceGroupInfo> CreateAsync(string resourceTypeKey, string name, string? description, int defaultAvailabilityPercent)
    {
        await using var conn = connectionFactory.CreateOrgConnection(orgContext);
        await conn.OpenAsync();

        // RETURNING includes a correlated COUNT subquery so MapGroup can be reused
        // here (and in UpdateAsync below). Newly-created groups always have 0 members
        // but we compute it for shape-uniformity rather than hardcoding.
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO resource_groups (name, description, resource_type_id, default_availability_percent) " +
            "SELECT @name, @description, id, @defaultAvailabilityPercent FROM resource_types WHERE key = @resourceTypeKey " +
            "RETURNING id, name, description, default_availability_percent, " +
            "  (SELECT COUNT(*) FROM resource_group_members WHERE resource_group_id = resource_groups.id)::int AS member_count, " +
            "  created_at, updated_at",
            conn);
        cmd.Parameters.AddWithValue("name", name);
        cmd.Parameters.AddWithValue("description", (object?)description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("resourceTypeKey", resourceTypeKey);
        cmd.Parameters.AddWithValue("defaultAvailabilityPercent", defaultAvailabilityPercent);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            throw new InvalidOperationException("Failed to create resource group: resource type not found");

        return MapGroup(reader);
    }

    public async Task<ResourceGroupInfo?> UpdateAsync(Guid id, string? name, string? description, int? defaultAvailabilityPercent)
    {
        await using var conn = connectionFactory.CreateOrgConnection(orgContext);
        await conn.OpenAsync();

        var updates = new List<string>();
        var parameters = new List<NpgsqlParameter> { new("id", id) };

        if (name != null)
        {
            updates.Add("name = @name");
            parameters.Add(new NpgsqlParameter("name", name));
        }
        if (description != null)
        {
            updates.Add("description = @description");
            parameters.Add(new NpgsqlParameter("description", description));
        }
        if (defaultAvailabilityPercent.HasValue)
        {
            updates.Add("default_availability_percent = @defaultAvailabilityPercent");
            parameters.Add(new NpgsqlParameter("defaultAvailabilityPercent", defaultAvailabilityPercent.Value));
        }

        if (updates.Count == 0)
            return await GetByIdAsync(id);

        var sql = $"UPDATE resource_groups SET {string.Join(", ", updates)} WHERE id = @id " +
                  "RETURNING id, name, description, default_availability_percent, " +
                  "  (SELECT COUNT(*) FROM resource_group_members WHERE resource_group_id = resource_groups.id)::int AS member_count, " +
                  "  created_at, updated_at";

        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var p in parameters)
            cmd.Parameters.Add(p);

        await using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? MapGroup(reader) : null;
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        await using var conn = connectionFactory.CreateOrgConnection(orgContext);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "DELETE FROM resource_groups WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", id);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    private static ResourceGroupInfo MapGroup(NpgsqlDataReader reader) => new()
    {
        Id = reader.GetGuid(0),
        Name = reader.GetString(1),
        Description = reader.IsDBNull(2) ? null : reader.GetString(2),
        DefaultAvailabilityPercent = reader.GetInt32(3),
        MemberCount = reader.GetInt32(4),
        CreatedAt = reader.GetDateTime(5),
        UpdatedAt = reader.GetDateTime(6),
    };
}
