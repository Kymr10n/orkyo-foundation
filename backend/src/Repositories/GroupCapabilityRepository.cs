using Npgsql;
using System.Text.Json;
using Api.Helpers;
using Api.Models;
using Api.Services;

namespace Api.Repositories;

public interface IGroupCapabilityRepository
{
    Task<List<GroupCapabilityInfo>> GetAllAsync(Guid groupId);
    Task<GroupCapabilityInfo> CreateAsync(Guid groupId, Guid criterionId, object value);
    Task<bool> DeleteAsync(Guid groupId, Guid capabilityId);
}

public class GroupCapabilityRepository : IGroupCapabilityRepository
{
    private readonly OrgContext _orgContext;
    private readonly IOrgDbConnectionFactory _connectionFactory;

    public GroupCapabilityRepository(OrgContext orgContext, IOrgDbConnectionFactory connectionFactory)
    {
        _orgContext = orgContext;
        _connectionFactory = connectionFactory;
    }

    public async Task<List<GroupCapabilityInfo>> GetAllAsync(Guid groupId)
    {
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);
        await conn.OpenAsync();

        // Verify group exists
        if (!await DbQueryHelper.ExistsAsync(conn, "space_groups", groupId))
            throw new InvalidOperationException("Group not found");

        // Get capabilities with criterion details
        await using var cmd = new NpgsqlCommand(@"
            SELECT 
                gc.id,
                gc.group_id,
                gc.criterion_id,
                gc.value,
                gc.created_at,
                gc.updated_at,
                c.name as criterion_name,
                c.data_type as criterion_type,
                c.unit as criterion_unit
            FROM group_capabilities gc
            JOIN criteria c ON gc.criterion_id = c.id
            WHERE gc.group_id = @groupId
            ORDER BY c.name",
            conn);
        cmd.Parameters.AddWithValue("groupId", groupId);

        await using var reader = await cmd.ExecuteReaderAsync();
        var capabilities = new List<GroupCapabilityInfo>();

        while (await reader.ReadAsync())
        {
            capabilities.Add(MapFromReader(reader));
        }

        return capabilities;
    }

    public async Task<GroupCapabilityInfo> CreateAsync(Guid groupId, Guid criterionId, object value)
    {
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);
        await conn.OpenAsync();

        // Verify group and criterion exist
        if (!await DbQueryHelper.ExistsAsync(conn, "space_groups", groupId))
            throw new InvalidOperationException("Group not found");

        if (!await DbQueryHelper.ExistsAsync(conn, "criteria", criterionId))
            throw new InvalidOperationException("Criterion not found");

        // Insert capability
        await using var cmd = new NpgsqlCommand(@"
            INSERT INTO group_capabilities (group_id, criterion_id, value)
            VALUES (@groupId, @criterionId, @value::jsonb)
            RETURNING id, group_id, criterion_id, value, created_at, updated_at",
            conn);
        cmd.Parameters.AddWithValue("groupId", groupId);
        cmd.Parameters.AddWithValue("criterionId", criterionId);
        cmd.Parameters.AddWithValue("value", JsonSerializer.Serialize(value));

        try
        {
            await using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                throw new InvalidOperationException("Failed to create capability");
            }

            return MapFromReader(reader, includeCriterion: false);
        }
        catch (PostgresException ex) when (ex.SqlState == "23505") // Unique violation
        {
            throw new InvalidOperationException("This criterion already has a value for this group");
        }
    }

    public async Task<bool> DeleteAsync(Guid groupId, Guid capabilityId)
    {
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);
        await conn.OpenAsync();

        // Verify group exists
        if (!await DbQueryHelper.ExistsAsync(conn, "space_groups", groupId))
            throw new InvalidOperationException("Group not found");

        // Delete capability
        await using var cmd = new NpgsqlCommand(
            "DELETE FROM group_capabilities WHERE id = @id AND group_id = @groupId",
            conn);
        cmd.Parameters.AddWithValue("id", capabilityId);
        cmd.Parameters.AddWithValue("groupId", groupId);

        var rowsAffected = await cmd.ExecuteNonQueryAsync();
        return rowsAffected > 0;
    }

    private static GroupCapabilityInfo MapFromReader(NpgsqlDataReader reader, bool includeCriterion = true)
    {
        object? value = null;
        if (!reader.IsDBNull(3))
        {
            value = JsonSerializer.Deserialize<JsonElement>(reader.GetString(3));
        }

        CriterionMetadata? criterion = null;
        if (includeCriterion)
        {
            criterion = new CriterionMetadata
            {
                Id = reader.GetGuid(2),
                Name = reader.GetString(6),
                DataType = reader.GetString(7),
                Unit = reader.IsDBNull(8) ? null : reader.GetString(8)
            };
        }

        return new GroupCapabilityInfo
        {
            Id = reader.GetGuid(0),
            GroupId = reader.GetGuid(1),
            CriterionId = reader.GetGuid(2),
            Value = value,
            CreatedAt = reader.GetDateTime(4),
            UpdatedAt = reader.GetDateTime(5),
            Criterion = criterion
        };
    }
}

public record GroupCapabilityInfo
{
    public Guid Id { get; init; }
    public Guid GroupId { get; init; }
    public Guid CriterionId { get; init; }
    public object? Value { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
    public CriterionMetadata? Criterion { get; init; }
}
