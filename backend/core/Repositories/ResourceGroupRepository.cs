using Api.Models;
using Api.Services;
using Npgsql;

namespace Api.Repositories;

public interface IResourceGroupRepository
{
    Task<List<ResourceGroupInfo>> GetByTypeKeyAsync(string resourceTypeKey, CancellationToken ct = default);
    Task<ResourceGroupInfo?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<ResourceGroupInfo> CreateAsync(string resourceTypeKey, string name, string? description, int defaultAvailabilityPercent, string? color, int? displayOrder, CancellationToken ct = default);
    Task<ResourceGroupInfo?> UpdateAsync(Guid id, string? name, string? description, int? defaultAvailabilityPercent, string? color, int? displayOrder, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}

public class ResourceGroupRepository(OrgContext orgContext, IOrgDbConnectionFactory connectionFactory)
    : IResourceGroupRepository
{
    private const string SelectWithCount =
        "SELECT g.id, g.name, g.description, g.default_availability_percent, " +
        "  COUNT(m.resource_id) AS member_count, g.created_at, g.updated_at, " +
        "  rt.key AS resource_type_key, g.color, g.display_order " +
        "FROM resource_groups g " +
        "JOIN resource_types rt ON rt.id = g.resource_type_id " +
        "LEFT JOIN resource_group_members m ON m.resource_group_id = g.id ";

    private const string GroupBy =
        "GROUP BY g.id, g.name, g.description, g.default_availability_percent, g.created_at, g.updated_at, rt.key, g.color, g.display_order ";

    public async Task<List<ResourceGroupInfo>> GetByTypeKeyAsync(string resourceTypeKey, CancellationToken ct = default)
    {
        await using var conn = connectionFactory.CreateOrgConnection(orgContext);
        return await conn.QueryListAsync(
            SelectWithCount + "WHERE rt.key = @resourceTypeKey " + GroupBy + "ORDER BY g.name",
            p => p.AddWithValue("resourceTypeKey", resourceTypeKey), MapGroup, ct);
    }

    public async Task<ResourceGroupInfo?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = connectionFactory.CreateOrgConnection(orgContext);
        return await conn.QuerySingleOrDefaultAsync(
            SelectWithCount + "WHERE g.id = @id " + GroupBy,
            p => p.AddWithValue("id", id), MapGroup, ct);
    }

    public async Task<ResourceGroupInfo> CreateAsync(string resourceTypeKey, string name, string? description, int defaultAvailabilityPercent, string? color, int? displayOrder, CancellationToken ct = default)
    {
        await using var conn = connectionFactory.CreateOrgConnection(orgContext);

        var created = await conn.QuerySingleOrDefaultAsync(
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
            p =>
            {
                p.AddWithValue("name", name);
                p.AddNullable("description", description);
                p.AddWithValue("resourceTypeKey", resourceTypeKey);
                p.AddWithValue("defaultAvailabilityPercent", defaultAvailabilityPercent);
                p.AddNullable("color", color);
                p.AddWithValue("displayOrder", displayOrder ?? 0);
            }, MapGroup, ct);

        return created ?? throw new InvalidOperationException("Failed to create resource group: resource type not found");
    }

    public async Task<ResourceGroupInfo?> UpdateAsync(Guid id, string? name, string? description, int? defaultAvailabilityPercent, string? color, int? displayOrder, CancellationToken ct = default)
    {
        await using var conn = connectionFactory.CreateOrgConnection(orgContext);

        var update = new UpdateBuilder();
        update.SetIfNotNull("name", name);
        update.SetIfNotNull("description", description);
        if (defaultAvailabilityPercent.HasValue)
            update.Set("default_availability_percent", defaultAvailabilityPercent.Value);
        update.SetIfNotNull("color", color);
        if (displayOrder.HasValue)
            update.Set("display_order", displayOrder.Value);

        if (update.IsEmpty)
            return await GetByIdAsync(id, ct);

        var sql =
            "WITH updated AS ( " +
            $"  UPDATE resource_groups SET {update.SetClause} WHERE id = @id RETURNING * " +
            ") " +
            "SELECT u.id, u.name, u.description, u.default_availability_percent, " +
            "  (SELECT COUNT(*) FROM resource_group_members WHERE resource_group_id = u.id)::int AS member_count, " +
            "  u.created_at, u.updated_at, rt.key AS resource_type_key, u.color, u.display_order " +
            "FROM updated u " +
            "JOIN resource_types rt ON rt.id = u.resource_type_id";

        return await conn.QuerySingleOrDefaultAsync(sql, p =>
        {
            p.AddWithValue("id", id);
            update.Apply(p);
        }, MapGroup, ct);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = connectionFactory.CreateOrgConnection(orgContext);
        return await conn.ExecuteAsync("DELETE FROM resource_groups WHERE id = @id",
            p => p.AddWithValue("id", id), ct) > 0;
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
