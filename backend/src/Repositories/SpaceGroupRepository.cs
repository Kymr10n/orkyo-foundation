using System.Text.Json;
using Api.Helpers;
using Api.Models;
using Api.Services;
using Npgsql;

namespace Api.Repositories;

public class SpaceGroupRepository : ISpaceGroupRepository
{
    // GROUP BY columns (excludes the computed space_count aggregate)
    private const string GroupByColumns =
        "g.id, g.name, g.description, g.color, g.display_order, g.created_at, g.updated_at";

    private const string SelectWithCount =
        @"SELECT g.id, g.name, g.description, g.color, g.display_order, g.created_at, g.updated_at,
                 COUNT(s.id) as space_count
          FROM space_groups g
          LEFT JOIN spaces s ON s.group_id = g.id";

    private readonly OrgContext _orgContext;
    private readonly IOrgDbConnectionFactory _connectionFactory;

    public SpaceGroupRepository(OrgContext orgContext, IOrgDbConnectionFactory connectionFactory)
    {
        _orgContext = orgContext;
        _connectionFactory = connectionFactory;
    }

    public async Task<List<SpaceGroupInfo>> GetAllAsync()
    {
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            $"{SelectWithCount} GROUP BY {GroupByColumns} ORDER BY g.display_order, g.name LIMIT 200",
            conn);

        var groups = new List<SpaceGroupInfo>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            groups.Add(MapFromReader(reader));

        return groups;
    }

    public async Task<PagedResult<SpaceGroupInfo>> GetAllAsync(PageRequest page)
    {
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);
        await conn.OpenAsync();

        return await DbQueryHelper.ExecutePagedQueryAsync(
            conn,
            page,
            countSql: "SELECT COUNT(*) FROM space_groups",
            querySql: $"{SelectWithCount} GROUP BY {GroupByColumns} ORDER BY g.display_order, g.name LIMIT @limit OFFSET @offset",
            addParams: null,
            mapper: MapFromReader);
    }

    public async Task<SpaceGroupInfo?> GetByIdAsync(Guid groupId)
    {
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            $"{SelectWithCount} WHERE g.id = @groupId GROUP BY {GroupByColumns}",
            conn);
        cmd.Parameters.AddWithValue("groupId", groupId);

        await using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? MapFromReader(reader) : null;
    }

    public async Task<SpaceGroupInfo> CreateAsync(string name, string? description, string? color, int displayOrder)
    {
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            @"INSERT INTO space_groups (name, description, color, display_order)
              VALUES (@name, @description, @color, @displayOrder)
              RETURNING id, name, description, color, display_order, created_at, updated_at",
            conn);

        cmd.Parameters.AddWithValue("name", name);
        cmd.Parameters.AddWithValue("description", (object?)description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("color", (object?)color ?? DBNull.Value);
        cmd.Parameters.AddWithValue("displayOrder", displayOrder);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            throw new InvalidOperationException("Failed to create space group");
        }

        return new SpaceGroupInfo
        {
            Id = reader.GetGuid(0),
            Name = reader.GetString(1),
            Description = reader.IsDBNull(2) ? null : reader.GetString(2),
            Color = reader.IsDBNull(3) ? null : reader.GetString(3),
            DisplayOrder = reader.GetInt32(4),
            CreatedAt = reader.GetDateTime(5),
            UpdatedAt = reader.GetDateTime(6),
            SpaceCount = 0 // newly created group has no spaces
        };
    }

    public async Task<SpaceGroupInfo?> UpdateAsync(Guid groupId, string? name, string? description, string? color, int? displayOrder)
    {
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);
        await conn.OpenAsync();

        // Build dynamic update query
        var updates = new List<string>();
        var parameters = new List<NpgsqlParameter>
        {
            new("groupId", groupId)
        };

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
        {
            return await GetByIdAsync(groupId);
        }

        var sql = $@"UPDATE space_groups 
                     SET {string.Join(", ", updates)}
                     WHERE id = @groupId
                     RETURNING id, name, description, color, display_order, created_at, updated_at";

        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var param in parameters)
        {
            cmd.Parameters.Add(param);
        }

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;

        return new SpaceGroupInfo
        {
            Id = reader.GetGuid(0),
            Name = reader.GetString(1),
            Description = reader.IsDBNull(2) ? null : reader.GetString(2),
            Color = reader.IsDBNull(3) ? null : reader.GetString(3),
            DisplayOrder = reader.GetInt32(4),
            CreatedAt = reader.GetDateTime(5),
            UpdatedAt = reader.GetDateTime(6)
        };
    }

    public async Task<bool> DeleteAsync(Guid groupId)
    {
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "DELETE FROM space_groups WHERE id = @groupId",
            conn);
        cmd.Parameters.AddWithValue("groupId", groupId);

        var rowsAffected = await cmd.ExecuteNonQueryAsync();
        return rowsAffected > 0;
    }

    private static SpaceGroupInfo MapFromReader(NpgsqlDataReader reader) => new()
    {
        Id = reader.GetGuid(0),
        Name = reader.GetString(1),
        Description = reader.IsDBNull(2) ? null : reader.GetString(2),
        Color = reader.IsDBNull(3) ? null : reader.GetString(3),
        DisplayOrder = reader.GetInt32(4),
        CreatedAt = reader.GetDateTime(5),
        UpdatedAt = reader.GetDateTime(6),
        SpaceCount = reader.IsDBNull(7) ? 0 : Convert.ToInt32(reader.GetInt64(7))
    };
}
