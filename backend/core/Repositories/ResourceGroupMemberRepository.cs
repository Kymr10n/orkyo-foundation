using Api.Constants;
using Api.Models;
using Api.Services;
using Npgsql;

namespace Api.Repositories;

public interface IResourceGroupMemberRepository
{
    Task<ResourceGroupMembersResponse> GetMembersAsync(Guid groupId, CancellationToken ct = default);
    Task SetMembersAsync(Guid groupId, IReadOnlyList<Guid> resourceIds, CancellationToken ct = default);

    /// <summary>Returns the IDs of all resource groups that contain the given resource.</summary>
    Task<IReadOnlyList<Guid>> GetGroupIdsForResourceAsync(Guid resourceId, CancellationToken ct = default);

    /// <summary>Returns group IDs keyed by resource ID for a batch of resources.</summary>
    Task<Dictionary<Guid, IReadOnlyList<Guid>>> GetGroupIdsForResourcesAsync(IReadOnlyList<Guid> resourceIds, CancellationToken ct = default);
}

public class ResourceGroupMemberRepository(OrgContext orgContext, IOrgDbConnectionFactory connectionFactory)
    : IResourceGroupMemberRepository
{
    private const string ResourceSelectColumns =
        "r.id, r.resource_type_id, rt.key as resource_type_key, r.name, r.description, " +
        "r.external_reference, r.allocation_mode, r.base_availability_percent, " +
        "r.is_active, r.created_at, r.updated_at";

    public async Task<ResourceGroupMembersResponse> GetMembersAsync(Guid groupId, CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        var members = await db.QueryListAsync(
            $"SELECT {ResourceSelectColumns} " +
            "FROM resource_group_members m " +
            "JOIN resources r ON r.id = m.resource_id " +
            "JOIN resource_types rt ON rt.id = r.resource_type_id " +
            "WHERE m.resource_group_id = @groupId ORDER BY r.name",
            p => p.AddWithValue("groupId", groupId),
            MapResource, ct);
        return new ResourceGroupMembersResponse { GroupId = groupId, Members = members };
    }

    public async Task SetMembersAsync(Guid groupId, IReadOnlyList<Guid> resourceIds, CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        await db.OpenAsync(ct);

        // Validate that all provided resources share the group's resource type.
        await using var typeCheckCmd = new NpgsqlCommand(
            "SELECT g.resource_type_id AS group_type_id, " +
            "  array_agg(r.id) FILTER (WHERE r.resource_type_id != g.resource_type_id) AS mismatched_ids " +
            "FROM resource_groups g " +
            "CROSS JOIN (SELECT unnest(@ids::uuid[]) AS id) ids_list " +
            "LEFT JOIN resources r ON r.id = ids_list.id " +
            "WHERE g.id = @groupId " +
            "GROUP BY g.resource_type_id",
            db);
        typeCheckCmd.Parameters.AddWithValue("groupId", groupId);
        typeCheckCmd.Parameters.AddWithValue("ids", resourceIds.ToArray());

        await using (var checkReader = await typeCheckCmd.ExecuteReaderAsync(ct))
        {
            if (await checkReader.ReadAsync(ct))
            {
                var mismatchedIds = checkReader.IsDBNull(1)
                    ? []
                    : (Guid[])checkReader.GetValue(1);

                if (mismatchedIds.Length > 0)
                {
                    throw new ArgumentException(
                        $"resource_type_mismatch: {mismatchedIds.Length} resource(s) do not match the group's resource type: " +
                        string.Join(", ", mismatchedIds));
                }
            }
            else
            {
                // Group not found — let the FK on resource_group_members handle it.
                // If group doesn't exist, proceed to delete+insert which will be a no-op / FK violation.
            }
        }

        await using var tx = await db.BeginTransactionAsync();

        await using var del = new NpgsqlCommand(
            "DELETE FROM resource_group_members WHERE resource_group_id = @groupId", db, tx);
        del.Parameters.AddWithValue("groupId", groupId);
        await del.ExecuteNonQueryAsync(ct);

        if (resourceIds.Count > 0)
        {
            // 1:1 move semantics for spaces: a space may belong to at most one group, so
            // remove each incoming space from any OTHER space-group before inserting here
            // (delete-before-insert, same txn, satisfies the trg_space_single_group guard).
            // No-op for people/other types — they have no space-group memberships.
            await using var move = new NpgsqlCommand(
                "DELETE FROM resource_group_members m " +
                "USING resource_groups g " +
                "WHERE m.resource_group_id = g.id " +
                "  AND m.resource_id = ANY(@ids) " +
                "  AND m.resource_group_id <> @groupId " +
                $"  AND g.resource_type_id = (SELECT id FROM resource_types WHERE key = '{ResourceTypeKeys.Space}')",
                db, tx);
            move.Parameters.AddWithValue("groupId", groupId);
            move.Parameters.AddWithValue("ids", resourceIds.ToArray());
            await move.ExecuteNonQueryAsync(ct);

            await using var ins = new NpgsqlCommand(
                "INSERT INTO resource_group_members (resource_group_id, resource_id, resource_type_id) " +
                "SELECT @groupId, r.id, r.resource_type_id " +
                "FROM resources r " +
                "WHERE r.id = ANY(@ids)",
                db, tx);
            ins.Parameters.AddWithValue("groupId", groupId);
            ins.Parameters.AddWithValue("ids", resourceIds.ToArray());
            await ins.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync();
    }

    public async Task<IReadOnlyList<Guid>> GetGroupIdsForResourceAsync(Guid resourceId, CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        return await db.QueryListAsync(
            "SELECT resource_group_id FROM resource_group_members WHERE resource_id = @resourceId",
            p => p.AddWithValue("resourceId", resourceId),
            r => r.GetGuid(0), ct);
    }

    public async Task<Dictionary<Guid, IReadOnlyList<Guid>>> GetGroupIdsForResourcesAsync(
        IReadOnlyList<Guid> resourceIds, CancellationToken ct = default)
    {
        if (resourceIds.Count == 0) return [];

        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        var rows = await db.QueryListAsync(
            "SELECT resource_id, resource_group_id FROM resource_group_members WHERE resource_id = ANY(@ids)",
            p => p.AddWithValue("ids", resourceIds.ToArray()),
            r => (r.GetGuid(0), r.GetGuid(1)), ct);

        var map = new Dictionary<Guid, List<Guid>>();
        foreach (var (rId, gId) in rows)
        {
            if (!map.TryGetValue(rId, out var list)) { list = []; map[rId] = list; }
            list.Add(gId);
        }
        return map.ToDictionary(kvp => kvp.Key, kvp => (IReadOnlyList<Guid>)kvp.Value);
    }

    private static ResourceInfo MapResource(NpgsqlDataReader r) => new()
    {
        Id = r.GetGuid(r.GetOrdinal("id")),
        ResourceTypeId = r.GetGuid(r.GetOrdinal("resource_type_id")),
        ResourceTypeKey = r.GetString(r.GetOrdinal("resource_type_key")),
        Name = r.GetString(r.GetOrdinal("name")),
        Description = r.IsDBNull(r.GetOrdinal("description")) ? null : r.GetString(r.GetOrdinal("description")),
        ExternalReference = r.IsDBNull(r.GetOrdinal("external_reference")) ? null : r.GetString(r.GetOrdinal("external_reference")),
        AllocationMode = r.GetString(r.GetOrdinal("allocation_mode")),
        BaseAvailabilityPercent = r.GetInt32(r.GetOrdinal("base_availability_percent")),
        IsActive = r.GetBoolean(r.GetOrdinal("is_active")),
        CreatedAt = r.GetDateTime(r.GetOrdinal("created_at")),
        UpdatedAt = r.GetDateTime(r.GetOrdinal("updated_at")),
    };
}
