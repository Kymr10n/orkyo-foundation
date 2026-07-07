using System.Text.Json;
using Api.Helpers;
using Api.Models;
using Api.Services;
using Npgsql;

namespace Api.Repositories;

public interface IGroupCapabilityRepository
{
    Task<List<GroupCapabilityInfo>> GetAllAsync(Guid groupId, CancellationToken ct = default);
    /// <summary>Bulk fetch capabilities for many groups in one query, keyed by group id — for export.</summary>
    Task<Dictionary<Guid, List<GroupCapabilityInfo>>> GetByGroupsAsync(IReadOnlyList<Guid> groupIds, CancellationToken ct = default);
    Task<GroupCapabilityInfo> CreateAsync(Guid groupId, Guid criterionId, object value, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid groupId, Guid capabilityId, CancellationToken ct = default);
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

    public async Task<List<GroupCapabilityInfo>> GetAllAsync(Guid groupId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);

        // Verify group exists
        if (!await conn.ExistsAsync("resource_groups", groupId, ct))
            throw new NotFoundException("Group", groupId);

        return await conn.QueryListAsync(@"
            SELECT
                gc.id, gc.resource_group_id, gc.criterion_id, gc.value,
                gc.created_at, gc.updated_at,
                c.name as criterion_name, c.data_type as criterion_type, c.unit as criterion_unit
            FROM resource_group_capabilities gc
            JOIN criteria c ON gc.criterion_id = c.id
            WHERE gc.resource_group_id = @groupId
            ORDER BY c.name",
            p => p.AddWithValue("groupId", groupId),
            r => MapFromReader(r), ct);
    }

    public async Task<Dictionary<Guid, List<GroupCapabilityInfo>>> GetByGroupsAsync(IReadOnlyList<Guid> groupIds, CancellationToken ct = default)
    {
        if (groupIds.Count == 0) return [];

        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);

        // No per-group existence check: callers pass ids they just read from resource_groups.
        var capabilities = await conn.QueryListAsync(@"
            SELECT
                gc.id, gc.resource_group_id, gc.criterion_id, gc.value,
                gc.created_at, gc.updated_at,
                c.name as criterion_name, c.data_type as criterion_type, c.unit as criterion_unit
            FROM resource_group_capabilities gc
            JOIN criteria c ON gc.criterion_id = c.id
            WHERE gc.resource_group_id = ANY(@groupIds)
            ORDER BY gc.resource_group_id, c.name",
            p => p.AddWithValue("groupIds", groupIds.ToArray()),
            r => MapFromReader(r), ct);

        var map = new Dictionary<Guid, List<GroupCapabilityInfo>>();
        foreach (var capability in capabilities)
        {
            if (!map.TryGetValue(capability.GroupId, out var list))
            {
                list = [];
                map[capability.GroupId] = list;
            }
            list.Add(capability);
        }
        return map;
    }

    public async Task<GroupCapabilityInfo> CreateAsync(Guid groupId, Guid criterionId, object value, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);

        // Verify group and criterion exist
        if (!await conn.ExistsAsync("resource_groups", groupId, ct))
            throw new NotFoundException("Group", groupId);

        if (!await conn.ExistsAsync("criteria", criterionId, ct))
            throw new NotFoundException("Criterion", criterionId);

        // Validate the criterion is applicable to this group's resource type. Mirrors the
        // resource-level guard in ResourceCapabilityRepository.UpsertAsync: if the resource
        // type has no criterion_resource_types entries at all, every criterion is considered
        // applicable (open-world assumption for new resource types).
        var isApplicable = await conn.ExecuteScalarAsync<object>(@"
            SELECT 1
            FROM resource_groups g
            WHERE g.id = @groupId
              AND (
                EXISTS (
                    SELECT 1 FROM criterion_resource_types
                    WHERE criterion_id = @criterionId AND resource_type_id = g.resource_type_id
                )
                OR NOT EXISTS (
                    SELECT 1 FROM criterion_resource_types
                    WHERE resource_type_id = g.resource_type_id
                )
              )",
            p =>
            {
                p.AddWithValue("groupId", groupId);
                p.AddWithValue("criterionId", criterionId);
            }, ct) is not null;

        if (!isApplicable)
            throw new CapabilityNotApplicableException(
                groupId, criterionId,
                "Criterion is not applicable to this group's resource type");

        try
        {
            return await conn.QuerySingleOrDefaultAsync(@"
                INSERT INTO resource_group_capabilities (resource_group_id, criterion_id, value)
                VALUES (@groupId, @criterionId, @value::jsonb)
                RETURNING id, resource_group_id, criterion_id, value, created_at, updated_at",
                p =>
                {
                    p.AddWithValue("groupId", groupId);
                    p.AddWithValue("criterionId", criterionId);
                    p.AddWithValue("value", JsonSerializer.Serialize(value));
                },
                r => MapFromReader(r, includeCriterion: false), ct)
                ?? throw new InvalidOperationException("Failed to create capability");
        }
        catch (PostgresException ex) when (ex.SqlState == "23505") // Unique violation
        {
            throw new ConflictException("This criterion already has a value for this group");
        }
    }

    public async Task<bool> DeleteAsync(Guid groupId, Guid capabilityId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);

        // Verify group exists
        if (!await conn.ExistsAsync("resource_groups", groupId, ct))
            throw new NotFoundException("Group", groupId);

        return await conn.ExecuteAsync(
            "DELETE FROM resource_group_capabilities WHERE id = @id AND resource_group_id = @groupId",
            p => { p.AddWithValue("id", capabilityId); p.AddWithValue("groupId", groupId); }, ct) > 0;
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
                DataType = EnumMapper.ParseEnum<CriterionDataType>(reader.GetString(7)),
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
