using Api.Models;
using Api.Services;
using Npgsql;

namespace Api.Repositories;

public interface ICriterionApplicabilityRepository
{
    Task<CriterionApplicabilityInfo?> GetByCriterionAsync(Guid criterionId);
    Task<List<Guid>> GetApplicableResourceTypeIdsAsync(Guid criterionId);
    Task<bool> IsCriterionApplicableToTypeAsync(Guid criterionId, Guid resourceTypeId);
    Task SetApplicableToRequestsAsync(Guid criterionId, bool applicable);
    Task SetResourceTypeApplicabilityAsync(Guid criterionId, IEnumerable<Guid> resourceTypeIds);
}

public class CriterionApplicabilityRepository(OrgContext orgContext, IOrgDbConnectionFactory connectionFactory)
    : ICriterionApplicabilityRepository
{
    public async Task<CriterionApplicabilityInfo?> GetByCriterionAsync(Guid criterionId)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        await db.OpenAsync();

        // applicable_to_requests - now on main criteria table (Phase 3)
        await using var reqCmd = new NpgsqlCommand(
            "SELECT applicable_to_requests FROM criteria WHERE id = @id", db);
        reqCmd.Parameters.AddWithValue("id", criterionId);
        var applicableToRequests = (bool?)await reqCmd.ExecuteScalarAsync() ?? true;

        // resource type keys
        await using var typeCmd = new NpgsqlCommand(
            "SELECT rt.key FROM criterion_resource_types crt " +
            "JOIN resource_types rt ON rt.id = crt.resource_type_id " +
            "WHERE crt.criterion_id = @id", db);
        typeCmd.Parameters.AddWithValue("id", criterionId);

        var keys = new List<string>();
        await using var reader = await typeCmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            keys.Add(reader.GetString(0));

        return new CriterionApplicabilityInfo
        {
            CriterionId = criterionId,
            ApplicableToRequests = applicableToRequests,
            ResourceTypeKeys = keys,
        };
    }

    public async Task<List<Guid>> GetApplicableResourceTypeIdsAsync(Guid criterionId)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        await db.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "SELECT resource_type_id FROM criterion_resource_types WHERE criterion_id = @id", db);
        cmd.Parameters.AddWithValue("id", criterionId);

        var result = new List<Guid>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            result.Add(reader.GetGuid(0));
        return result;
    }

    public async Task<bool> IsCriterionApplicableToTypeAsync(Guid criterionId, Guid resourceTypeId)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        await db.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "SELECT 1 FROM criterion_resource_types " +
            "WHERE criterion_id = @criterionId AND resource_type_id = @typeId", db);
        cmd.Parameters.AddWithValue("criterionId", criterionId);
        cmd.Parameters.AddWithValue("typeId", resourceTypeId);
        return await cmd.ExecuteScalarAsync() != null;
    }

    public async Task SetApplicableToRequestsAsync(Guid criterionId, bool applicable)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        await db.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "UPDATE criteria SET applicable_to_requests = @val WHERE id = @id", db);
        cmd.Parameters.AddWithValue("id", criterionId);
        cmd.Parameters.AddWithValue("val", applicable);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task SetResourceTypeApplicabilityAsync(Guid criterionId, IEnumerable<Guid> resourceTypeIds)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        await db.OpenAsync();
        await using var tx = await db.BeginTransactionAsync();

        // Replace all entries for this criterion.
        await using var del = new NpgsqlCommand(
            "DELETE FROM criterion_resource_types WHERE criterion_id = @id", db, tx);
        del.Parameters.AddWithValue("id", criterionId);
        await del.ExecuteNonQueryAsync();

        foreach (var typeId in resourceTypeIds)
        {
            await using var ins = new NpgsqlCommand(
                "INSERT INTO criterion_resource_types (criterion_id, resource_type_id) " +
                "VALUES (@criterionId, @typeId) ON CONFLICT DO NOTHING", db, tx);
            ins.Parameters.AddWithValue("criterionId", criterionId);
            ins.Parameters.AddWithValue("typeId", typeId);
            await ins.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();
    }
}
