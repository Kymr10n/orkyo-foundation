using Api.Models;
using Api.Services;
using Npgsql;

namespace Api.Repositories;

public interface IResourceGroupMemberRepository
{
    Task<ResourceGroupMembersResponse> GetMembersAsync(Guid groupId);
    Task SetMembersAsync(Guid groupId, IReadOnlyList<Guid> resourceIds);
}

public class ResourceGroupMemberRepository(OrgContext orgContext, IOrgDbConnectionFactory connectionFactory)
    : IResourceGroupMemberRepository
{
    private const string ResourceSelectColumns =
        "r.id, r.resource_type_id, rt.key as resource_type_key, r.name, r.description, " +
        "r.external_reference, r.allocation_mode, r.base_availability_percent, " +
        "r.is_active, r.created_at, r.updated_at";

    public async Task<ResourceGroupMembersResponse> GetMembersAsync(Guid groupId)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        await db.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            $"SELECT {ResourceSelectColumns} " +
            "FROM resource_group_members m " +
            "JOIN resources r ON r.id = m.resource_id " +
            "JOIN resource_types rt ON rt.id = r.resource_type_id " +
            "WHERE m.resource_group_id = @groupId " +
            "ORDER BY r.name", db);
        cmd.Parameters.AddWithValue("groupId", groupId);

        var members = new List<ResourceInfo>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            members.Add(MapResource(reader));

        return new ResourceGroupMembersResponse { GroupId = groupId, Members = members };
    }

    public async Task SetMembersAsync(Guid groupId, IReadOnlyList<Guid> resourceIds)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        await db.OpenAsync();

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

        await using (var checkReader = await typeCheckCmd.ExecuteReaderAsync())
        {
            if (await checkReader.ReadAsync())
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
        await del.ExecuteNonQueryAsync();

        if (resourceIds.Count > 0)
        {
            await using var ins = new NpgsqlCommand(
                "INSERT INTO resource_group_members (resource_group_id, resource_id, resource_type_id) " +
                "SELECT @groupId, r.id, r.resource_type_id " +
                "FROM resources r " +
                "WHERE r.id = ANY(@ids)",
                db, tx);
            ins.Parameters.AddWithValue("groupId", groupId);
            ins.Parameters.AddWithValue("ids", resourceIds.ToArray());
            await ins.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();
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
