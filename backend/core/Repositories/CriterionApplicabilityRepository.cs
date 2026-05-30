using Api.Helpers;
using Api.Models;
using Api.Services;
using Npgsql;

namespace Api.Repositories;

public interface ICriterionApplicabilityRepository
{
    Task<CriterionApplicabilityInfo?> GetByCriterionAsync(Guid criterionId, CancellationToken ct = default);
    Task<List<Guid>> GetApplicableResourceTypeIdsAsync(Guid criterionId, CancellationToken ct = default);
    Task<bool> IsCriterionApplicableToTypeAsync(Guid criterionId, Guid resourceTypeId, CancellationToken ct = default);
    Task SetApplicableToRequestsAsync(Guid criterionId, bool applicable, CancellationToken ct = default);
    Task SetResourceTypeApplicabilityAsync(Guid criterionId, IEnumerable<Guid> resourceTypeIds, CancellationToken ct = default);
}

public class CriterionApplicabilityRepository(OrgContext orgContext, IOrgDbConnectionFactory connectionFactory)
    : ICriterionApplicabilityRepository
{
    public async Task<CriterionApplicabilityInfo?> GetByCriterionAsync(Guid criterionId, CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);

        // applicable_to_requests lives on the main criteria table (Phase 3).
        var scalar = await db.ExecuteScalarAsync<object>(
            "SELECT applicable_to_requests FROM criteria WHERE id = @id",
            p => p.AddWithValue("id", criterionId), ct);
        if (scalar is null) return null; // criterion does not exist

        var keys = await db.QueryListAsync(
            "SELECT rt.key FROM criterion_resource_types crt " +
            "JOIN resource_types rt ON rt.id = crt.resource_type_id " +
            "WHERE crt.criterion_id = @id",
            p => p.AddWithValue("id", criterionId),
            r => r.GetString(0), ct);

        return new CriterionApplicabilityInfo
        {
            CriterionId = criterionId,
            ApplicableToRequests = (bool)scalar,
            ResourceTypeKeys = keys,
        };
    }

    public async Task<List<Guid>> GetApplicableResourceTypeIdsAsync(Guid criterionId, CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        return await db.QueryListAsync(
            "SELECT resource_type_id FROM criterion_resource_types WHERE criterion_id = @id",
            p => p.AddWithValue("id", criterionId),
            r => r.GetGuid(0), ct);
    }

    public async Task<bool> IsCriterionApplicableToTypeAsync(Guid criterionId, Guid resourceTypeId, CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        return await db.ExecuteScalarAsync<object>(
            "SELECT 1 FROM criterion_resource_types WHERE criterion_id = @criterionId AND resource_type_id = @typeId",
            p => { p.AddWithValue("criterionId", criterionId); p.AddWithValue("typeId", resourceTypeId); }, ct) is not null;
    }

    public async Task SetApplicableToRequestsAsync(Guid criterionId, bool applicable, CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        await db.ExecuteAsync("UPDATE criteria SET applicable_to_requests = @val WHERE id = @id",
            p => { p.AddWithValue("id", criterionId); p.AddWithValue("val", applicable); }, ct);
    }

    public async Task SetResourceTypeApplicabilityAsync(Guid criterionId, IEnumerable<Guid> resourceTypeIds, CancellationToken ct = default)
    {
        var newTypeIds = new HashSet<Guid>(resourceTypeIds);

        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        await db.OpenAsync(ct);
        await using var tx = await db.BeginTransactionAsync();

        // Block removing applicability for a resource type while assignments of
        // that type still reference this criterion. Without this guard, the
        // criterion could be silently rendered non-applicable to existing
        // assignments, leaving the system in an inconsistent state.
        var currentTypeIds = await GetApplicableResourceTypeIdsAsync(criterionId, ct);
        var removedTypeIds = currentTypeIds.Where(t => !newTypeIds.Contains(t)).ToList();

        foreach (var typeId in removedTypeIds)
        {
            await using var checkCmd = new NpgsqlCommand(
                "SELECT " +
                "  (SELECT COUNT(*) FROM resource_capabilities rc " +
                "     JOIN resources r ON r.id = rc.resource_id " +
                "    WHERE rc.criterion_id = @criterionId AND r.resource_type_id = @typeId) AS resources, " +
                "  (SELECT COUNT(*) FROM resource_group_capabilities rgc " +
                "     JOIN resource_groups rg ON rg.id = rgc.resource_group_id " +
                "    WHERE rgc.criterion_id = @criterionId AND rg.resource_type_id = @typeId) AS groups, " +
                "  (SELECT key FROM resource_types WHERE id = @typeId) AS type_key",
                db, tx);
            checkCmd.Parameters.AddWithValue("criterionId", criterionId);
            checkCmd.Parameters.AddWithValue("typeId", typeId);

            await using var reader = await checkCmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) continue;
            var resources = reader.GetInt64(0);
            var groups = reader.GetInt64(1);
            var typeKey = reader.IsDBNull(2) ? typeId.ToString() : reader.GetString(2);
            await reader.CloseAsync();

            if (resources > 0 || groups > 0)
            {
                var parts = new List<string>();
                if (resources > 0) parts.Add($"{resources} resource assignment{(resources == 1 ? "" : "s")}");
                if (groups > 0) parts.Add($"{groups} group assignment{(groups == 1 ? "" : "s")}");
                throw new ConflictException(
                    $"Cannot remove '{typeKey}' applicability: criterion still has {string.Join(", ", parts)} on {typeKey} resources. " +
                    "Clear those assignments first.");
            }
        }

        // Replace all entries for this criterion.
        await using var del = new NpgsqlCommand(
            "DELETE FROM criterion_resource_types WHERE criterion_id = @id", db, tx);
        del.Parameters.AddWithValue("id", criterionId);
        await del.ExecuteNonQueryAsync(ct);

        foreach (var typeId in newTypeIds)
        {
            await using var ins = new NpgsqlCommand(
                "INSERT INTO criterion_resource_types (criterion_id, resource_type_id) " +
                "VALUES (@criterionId, @typeId) ON CONFLICT DO NOTHING", db, tx);
            ins.Parameters.AddWithValue("criterionId", criterionId);
            ins.Parameters.AddWithValue("typeId", typeId);
            await ins.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync();
    }
}
