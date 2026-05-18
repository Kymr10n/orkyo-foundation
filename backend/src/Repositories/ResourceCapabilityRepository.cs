using System.Text.Json;
using Api.Helpers;
using Api.Models;
using Api.Services;
using Npgsql;

namespace Api.Repositories;

public interface IResourceCapabilityRepository
{
    Task<List<ResourceCapabilityInfo>> GetByResourceAsync(Guid resourceId);
    Task<List<ResourceCapabilityInfo>> GetByResourceGroupAsync(Guid resourceGroupId);
    Task<ResourceCapabilityInfo> UpsertAsync(Guid resourceId, Guid criterionId, JsonElement value);
    Task<bool> DeleteAsync(Guid resourceId, Guid capabilityId);
}

public class ResourceCapabilityRepository(OrgContext orgContext, IOrgDbConnectionFactory connectionFactory)
    : IResourceCapabilityRepository
{
    // Single constant so Phase 2 only changes this string.
    private const string TableName = "resource_capabilities";

    private const string SelectColumns =
        $"rc.id, rc.resource_id, rc.criterion_id, rc.value, rc.created_at, rc.updated_at, " +
        $"c.name as criterion_name, c.data_type as criterion_type, c.unit as criterion_unit";

    public async Task<List<ResourceCapabilityInfo>> GetByResourceAsync(Guid resourceId)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        await db.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            $"SELECT {SelectColumns} FROM {TableName} rc " +
            "JOIN criteria c ON rc.criterion_id = c.id " +
            "WHERE rc.resource_id = @resourceId ORDER BY c.name", db);
        cmd.Parameters.AddWithValue("resourceId", resourceId);

        return await ReadAllAsync(cmd);
    }

    public async Task<List<ResourceCapabilityInfo>> GetByResourceGroupAsync(Guid resourceGroupId)
    {
        // Group capabilities live in resource_group_capabilities (renamed in Phase 2).
        // In Phase 2, groups are in resource_groups.
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        await db.OpenAsync();

        await using var cmd = new NpgsqlCommand(@"
            SELECT gc.id, sg.id as resource_id, gc.criterion_id, gc.value,
                   gc.created_at, gc.updated_at,
                   c.name as criterion_name, c.data_type as criterion_type, c.unit as criterion_unit
            FROM resource_group_capabilities gc
            JOIN resource_groups sg ON sg.id = gc.resource_group_id
            JOIN criteria c ON gc.criterion_id = c.id
            WHERE gc.resource_group_id = @groupId
            ORDER BY c.name", db);
        cmd.Parameters.AddWithValue("groupId", resourceGroupId);

        return await ReadAllAsync(cmd);
    }

    public async Task<ResourceCapabilityInfo> UpsertAsync(Guid resourceId, Guid criterionId, JsonElement value)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        await db.OpenAsync();
        await using var tx = await db.BeginTransactionAsync();

        try
        {
            // Validate criterion exists and is applicable to this resource's type.
            // If no criterion_resource_types entries exist for the resource type, all criteria
            // are considered applicable (open-world assumption for new resource types).
            await using var checkCmd = new NpgsqlCommand(@"
                SELECT 1
                FROM resources r
                WHERE r.id = @resourceId
                  AND (
                    EXISTS (
                        SELECT 1 FROM criterion_resource_types
                        WHERE criterion_id = @criterionId AND resource_type_id = r.resource_type_id
                    )
                    OR NOT EXISTS (
                        SELECT 1 FROM criterion_resource_types
                        WHERE resource_type_id = r.resource_type_id
                    )
                  )", db, tx);
            checkCmd.Parameters.AddWithValue("resourceId", resourceId);
            checkCmd.Parameters.AddWithValue("criterionId", criterionId);

            var isApplicable = await checkCmd.ExecuteScalarAsync() != null;
            if (!isApplicable)
                throw new CapabilityNotApplicableException(
                    resourceId, criterionId,
                    "Criterion is not applicable to this resource type");

            var valueJson = value.GetRawText();

            await using var cmd = new NpgsqlCommand(
                $"INSERT INTO {TableName} (resource_id, criterion_id, value) " +
                "VALUES (@resourceId, @criterionId, @value::jsonb) " +
                "ON CONFLICT (resource_id, criterion_id) DO UPDATE " +
                "SET value = EXCLUDED.value, updated_at = NOW() " +
                "RETURNING id, created_at, updated_at", db, tx);
            cmd.Parameters.AddWithValue("resourceId", resourceId);
            cmd.Parameters.AddWithValue("criterionId", criterionId);
            cmd.Parameters.AddWithValue("value", valueJson);

            Guid newId;
            DateTime createdAt, updatedAt;

            await using (var reader = await cmd.ExecuteReaderAsync())
            {
                await reader.ReadAsync();
                newId = reader.GetGuid(reader.GetOrdinal("id"));
                createdAt = reader.GetDateTime(reader.GetOrdinal("created_at"));
                updatedAt = reader.GetDateTime(reader.GetOrdinal("updated_at"));
            }

            await tx.CommitAsync();

            return new ResourceCapabilityInfo
            {
                Id = newId,
                ResourceId = resourceId,
                CriterionId = criterionId,
                Value = value,
                CreatedAt = createdAt,
                UpdatedAt = updatedAt,
            };
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    public async Task<bool> DeleteAsync(Guid resourceId, Guid capabilityId)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        await db.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            $"DELETE FROM {TableName} WHERE resource_id = @resourceId AND id = @capabilityId", db);
        cmd.Parameters.AddWithValue("resourceId", resourceId);
        cmd.Parameters.AddWithValue("capabilityId", capabilityId);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    private static async Task<List<ResourceCapabilityInfo>> ReadAllAsync(NpgsqlCommand cmd)
    {
        var result = new List<ResourceCapabilityInfo>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            result.Add(Map(reader));
        return result;
    }

    private static ResourceCapabilityInfo Map(NpgsqlDataReader r)
    {
        var valueJson = r.GetString(r.GetOrdinal("value"));
        return new ResourceCapabilityInfo
        {
            Id = r.GetGuid(r.GetOrdinal("id")),
            ResourceId = r.GetGuid(r.GetOrdinal("resource_id")),
            CriterionId = r.GetGuid(r.GetOrdinal("criterion_id")),
            Value = JsonDocument.Parse(valueJson).RootElement,
            Criterion = new CriterionMetadata
            {
                Id = r.GetGuid(r.GetOrdinal("criterion_id")),
                Name = r.GetString(r.GetOrdinal("criterion_name")),
                DataType = EnumMapper.ParseEnum<CriterionDataType>(r.GetString(r.GetOrdinal("criterion_type"))),
                Unit = r.IsDBNull(r.GetOrdinal("criterion_unit")) ? null : r.GetString(r.GetOrdinal("criterion_unit")),
            },
            CreatedAt = r.GetDateTime(r.GetOrdinal("created_at")),
            UpdatedAt = r.GetDateTime(r.GetOrdinal("updated_at")),
        };
    }
}
