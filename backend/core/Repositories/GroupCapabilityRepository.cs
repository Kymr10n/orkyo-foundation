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
    /// <summary>
    /// Sets the group's value for a criterion — inserted, or replaced when the group already has
    /// one. Throws <see cref="NotFoundException"/> for a missing group or criterion.
    /// </summary>
    Task<GroupCapabilityInfo> UpsertAsync(Guid groupId, Guid criterionId, JsonElement value, CancellationToken ct = default);
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

        return capabilities.GroupBy(c => c.GroupId).ToDictionary(g => g.Key, g => g.ToList());
    }

    public async Task<GroupCapabilityInfo> UpsertAsync(Guid groupId, Guid criterionId, JsonElement value, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);

        // Verify group and criterion exist
        if (!await conn.ExistsAsync("resource_groups", groupId, ct))
            throw new NotFoundException("Group", groupId);

        if (!await conn.ExistsAsync("criteria", criterionId, ct))
            throw new NotFoundException("Criterion", criterionId);

        // The shared applicability rule (CriterionScopeSql.AppliesTo), as for resources.
        var isApplicable = await conn.ExecuteScalarAsync<bool>($@"
            SELECT {CriterionScopeSql.AppliesTo("@criterionId", "crt.resource_type_id = g.resource_type_id")}
            FROM resource_groups g
            WHERE g.id = @groupId",
            p =>
            {
                p.AddWithValue("groupId", groupId);
                p.AddWithValue("criterionId", criterionId);
            }, ct);

        if (!isApplicable)
            throw new CapabilityNotApplicableException(
                groupId, criterionId,
                "Criterion is not applicable to this group's resource type");

        return (await conn.QuerySingleOrDefaultAsync(@"
            INSERT INTO resource_group_capabilities (resource_group_id, criterion_id, value)
            VALUES (@groupId, @criterionId, @value)
            ON CONFLICT (resource_group_id, criterion_id) DO UPDATE
            SET value = EXCLUDED.value, updated_at = NOW()
            RETURNING id, resource_group_id, criterion_id, value, created_at, updated_at",
            p =>
            {
                p.AddWithValue("groupId", groupId);
                p.AddWithValue("criterionId", criterionId);
                p.AddJsonb("value", value.GetRawText());
            },
            r => MapFromReader(r, includeCriterion: false), ct))!;
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
        CriterionMetadata? criterion = null;
        if (includeCriterion)
        {
            criterion = new CriterionMetadata
            {
                Id = reader.GetGuid("criterion_id"),
                Name = reader.GetString("criterion_name"),
                DataType = EnumMapper.FromDbValue<CriterionDataType>(reader.GetString("criterion_type")),
                Unit = reader.GetNullableString("criterion_unit")
            };
        }

        return new GroupCapabilityInfo
        {
            Id = reader.GetGuid("id"),
            GroupId = reader.GetGuid("resource_group_id"),
            CriterionId = reader.GetGuid("criterion_id"),
            Value = reader.GetJsonElement("value"),
            CreatedAt = reader.GetDateTime("created_at"),
            UpdatedAt = reader.GetDateTime("updated_at"),
            Criterion = criterion
        };
    }
}
