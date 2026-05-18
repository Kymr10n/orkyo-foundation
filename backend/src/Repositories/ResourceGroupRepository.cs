using Api.Models;
using Api.Services;
using Npgsql;

namespace Api.Repositories;

public interface IResourceGroupRepository
{
    Task<List<ResourceGroupInfo>> GetByTypeKeyAsync(string resourceTypeKey);
    Task<ResourceGroupInfo?> GetByIdAsync(Guid id);
    Task<ResourceGroupInfo> CreateAsync(string resourceTypeKey, string name, string? description, int defaultAvailabilityPercent, string? color, int? displayOrder);
    Task<ResourceGroupInfo?> UpdateAsync(Guid id, string? name, string? description, int? defaultAvailabilityPercent, string? color, int? displayOrder);
    Task<bool> DeleteAsync(Guid id);
}

public class ResourceGroupRepository(OrgContext orgContext, IOrgDbConnectionFactory connectionFactory)
    : IResourceGroupRepository
{
    public async Task<List<ResourceGroupInfo>> GetByTypeKeyAsync(string resourceTypeKey)
    {
        await using var conn = connectionFactory.CreateOrgConnection(orgContext);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "SELECT g.id, g.name, g.description, g.default_availability_percent, " +
            "  COUNT(m.resource_id) AS member_count, g.created_at, g.updated_at, " +
            "  rt.key AS resource_type_key, g.color, g.display_order " +
            "FROM resource_groups g " +
            "JOIN resource_types rt ON rt.id = g.resource_type_id " +
            "LEFT JOIN resource_group_members m ON m.resource_group_id = g.id " +
            "WHERE rt.key = @resourceTypeKey " +
            "GROUP BY g.id, g.name, g.description, g.default_availability_percent, g.created_at, g.updated_at, rt.key, g.color, g.display_order " +
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
            "SELECT g.id, g.name, g.description, g.default_availability_percent, " +
            "  COUNT(m.resource_id) AS member_count, g.created_at, g.updated_at, " +
            "  rt.key AS resource_type_key, g.color, g.display_order " +
            "FROM resource_groups g " +
            "JOIN resource_types rt ON rt.id = g.resource_type_id " +
            "LEFT JOIN resource_group_members m ON m.resource_group_id = g.id " +
            "WHERE g.id = @id " +
            "GROUP BY g.id, g.name, g.description, g.default_availability_percent, g.created_at, g.updated_at, rt.key, g.color, g.display_order",
            conn);
        cmd.Parameters.AddWithValue("id", id);

        await using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? MapGroup(reader) : null;
    }

    public async Task<ResourceGroupInfo> CreateAsync(string resourceTypeKey, string name, string? description, int defaultAvailabilityPercent, string? color, int? displayOrder)
    {
        await using var conn = connectionFactory.CreateOrgConnection(orgContext);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "WITH inserted AS ( " +
            "  INSERT INTO resource_groups (name, description, resource_type_id, default_availability_percent, color, display_order) " +
            "  SELECT @name, @description, id, @defaultAvailabilityPercent, @color, @displayOrder " +
            "  FROM resource_types WHERE key = @resourceTypeKey " +
            "  RETURNING * " +
            ") " +
            "SELECT i.id, i.name, i.description, i.default_availability_percent, " +
            "  0 AS member_count, i.created_at, i.updated_at, " +
            "  rt.key AS resource_type_key, i.color, i.display_order " +
            "FROM inserted i " +
            "JOIN resource_types rt ON rt.id = i.resource_type_id",
            conn);
        cmd.Parameters.AddWithValue("name", name);
        cmd.Parameters.AddWithValue("description", (object?)description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("resourceTypeKey", resourceTypeKey);
        cmd.Parameters.AddWithValue("defaultAvailabilityPercent", defaultAvailabilityPercent);
        cmd.Parameters.AddWithValue("color", (object?)color ?? DBNull.Value);
        cmd.Parameters.AddWithValue("displayOrder", displayOrder ?? 0);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            throw new InvalidOperationException("Failed to create resource group: resource type not found");

        return MapGroup(reader);
    }

    public async Task<ResourceGroupInfo?> UpdateAsync(Guid id, string? name, string? description, int? defaultAvailabilityPercent, string? color, int? displayOrder)
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
        if (color != null)
        {
            updates.Add("color = @color");
            parameters.Add(new NpgsqlParameter("color", color));
        }
        if (displayOrder.HasValue)
        {
            updates.Add("display_order = @displayOrder");
            parameters.Add(new NpgsqlParameter("displayOrder", displayOrder.Value));
        }

        if (updates.Count == 0)
            return await GetByIdAsync(id);

        var sql =
            "WITH updated AS ( " +
            $"  UPDATE resource_groups SET {string.Join(", ", updates)} WHERE id = @id RETURNING * " +
            ") " +
            "SELECT u.id, u.name, u.description, u.default_availability_percent, " +
            "  (SELECT COUNT(*) FROM resource_group_members WHERE resource_group_id = u.id)::int AS member_count, " +
            "  u.created_at, u.updated_at, rt.key AS resource_type_key, u.color, u.display_order " +
            "FROM updated u " +
            "JOIN resource_types rt ON rt.id = u.resource_type_id";

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
        Id = reader.GetGuid(reader.GetOrdinal("id")),
        Name = reader.GetString(reader.GetOrdinal("name")),
        Description = reader.IsDBNull(reader.GetOrdinal("description")) ? null : reader.GetString(reader.GetOrdinal("description")),
        DefaultAvailabilityPercent = reader.GetInt32(reader.GetOrdinal("default_availability_percent")),
        MemberCount = reader.GetInt32(reader.GetOrdinal("member_count")),
        CreatedAt = reader.GetDateTime(reader.GetOrdinal("created_at")),
        UpdatedAt = reader.GetDateTime(reader.GetOrdinal("updated_at")),
        ResourceTypeKey = reader.GetString(reader.GetOrdinal("resource_type_key")),
        Color = reader.IsDBNull(reader.GetOrdinal("color")) ? null : reader.GetString(reader.GetOrdinal("color")),
        DisplayOrder = reader.IsDBNull(reader.GetOrdinal("display_order")) ? null : reader.GetInt32(reader.GetOrdinal("display_order")),
    };
}
