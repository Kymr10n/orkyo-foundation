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
        await using var tx = await db.BeginTransactionAsync();

        await using var del = new NpgsqlCommand(
            "DELETE FROM resource_group_members WHERE resource_group_id = @groupId", db, tx);
        del.Parameters.AddWithValue("groupId", groupId);
        await del.ExecuteNonQueryAsync();

        if (resourceIds.Count > 0)
        {
            var values = string.Join(", ",
                Enumerable.Range(0, resourceIds.Count).Select(i => $"(@groupId, @r{i})"));
            await using var ins = new NpgsqlCommand(
                $"INSERT INTO resource_group_members (resource_group_id, resource_id) VALUES {values}",
                db, tx);
            ins.Parameters.AddWithValue("groupId", groupId);
            for (var i = 0; i < resourceIds.Count; i++)
                ins.Parameters.AddWithValue($"r{i}", resourceIds[i]);
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
