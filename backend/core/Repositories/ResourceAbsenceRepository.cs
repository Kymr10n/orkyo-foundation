using Api.Helpers;
using Api.Models;
using Api.Services;
using Npgsql;

namespace Api.Repositories;

public class ResourceAbsenceRepository(OrgContext orgContext, IOrgDbConnectionFactory connectionFactory)
    : IResourceAbsenceRepository
{
    private const string Cols =
        "id, resource_id, absence_type, title, notes, start_ts, end_ts, " +
        "is_recurring, recurrence_rule, enabled, created_at, updated_at";

    public async Task<List<ResourceAbsenceInfo>> GetByResourceAsync(Guid resourceId, CancellationToken ct = default)
    {
        await using var conn = connectionFactory.CreateOrgConnection(orgContext);
        return await conn.QueryListAsync(
            $"SELECT {Cols} FROM resource_absences WHERE resource_id = @resourceId ORDER BY start_ts",
            p => p.AddWithValue("resourceId", resourceId),
            SchedulingMapper.MapResourceAbsenceFromReader, ct);
    }

    public async Task<ResourceAbsenceInfo> CreateAsync(Guid resourceId, CreateResourceAbsenceRequest request, CancellationToken ct = default)
    {
        await using var conn = connectionFactory.CreateOrgConnection(orgContext);
        // The HTTP endpoint and the MCP tool both rely on this: a missing resource is a 404, not
        // a foreign-key violation.
        if (!await conn.ExistsAsync("resources", resourceId, ct))
            throw new NotFoundException("Resource", resourceId);

        return (await conn.QuerySingleOrDefaultAsync($@"
            INSERT INTO resource_absences
                (resource_id, absence_type, title, notes, start_ts, end_ts,
                 is_recurring, recurrence_rule, enabled)
            VALUES
                (@resourceId, @absenceType, @title, @notes, @startTs, @endTs,
                 @isRecurring, @recurrenceRule, @enabled)
            RETURNING {Cols}",
            p =>
            {
                p.AddWithValue("resourceId", resourceId);
                p.AddWithValue("absenceType", EnumMapper.ToDbValue(request.AbsenceType));
                p.AddWithValue("title", request.Title);
                p.AddNullable("notes", request.Notes);
                p.AddWithValue("startTs", request.StartTs);
                p.AddWithValue("endTs", request.EndTs);
                p.AddWithValue("isRecurring", request.IsRecurring);
                p.AddNullable("recurrenceRule", request.RecurrenceRule);
                p.AddWithValue("enabled", request.Enabled);
            }, SchedulingMapper.MapResourceAbsenceFromReader, ct))!;
    }

    public async Task<ResourceAbsenceInfo?> UpdateAsync(Guid resourceId, Guid id, UpdateResourceAbsenceRequest request, CancellationToken ct = default)
    {
        // Only the fields the request carries are written: a read-merge-write of the whole row
        // would overwrite a concurrent update of another field with the value read before it.
        var update = RecurringWindowUpdate.Build(request)
            .SetIfNotNull("absence_type", request.AbsenceType is { } type ? EnumMapper.ToDbValue(type) : null)
            .SetIfNotNull("notes", request.Notes);

        await using var conn = connectionFactory.CreateOrgConnection(orgContext);
        return update.IsEmpty
            ? await conn.QuerySingleOrDefaultAsync(
                $"SELECT {Cols} FROM resource_absences WHERE id = @id AND resource_id = @resourceId",
                p => { p.AddWithValue("id", id); p.AddWithValue("resourceId", resourceId); },
                SchedulingMapper.MapResourceAbsenceFromReader, ct)
            : await conn.QuerySingleOrDefaultAsync(
                $"UPDATE resource_absences SET {update.SetClause} WHERE id = @id AND resource_id = @resourceId RETURNING {Cols}",
                p =>
                {
                    p.AddWithValue("id", id);
                    p.AddWithValue("resourceId", resourceId);
                    update.Apply(p);
                }, SchedulingMapper.MapResourceAbsenceFromReader, ct);
    }

    public async Task<bool> DeleteAsync(Guid resourceId, Guid id, CancellationToken ct = default)
    {
        await using var conn = connectionFactory.CreateOrgConnection(orgContext);
        return await conn.ExecuteAsync("DELETE FROM resource_absences WHERE id = @id AND resource_id = @resourceId",
            p => { p.AddWithValue("id", id); p.AddWithValue("resourceId", resourceId); }, ct) > 0;
    }

    public async Task<Dictionary<Guid, List<ResourceAbsenceInfo>>> GetEnabledByResourcesAsync(
        IReadOnlyList<Guid> resourceIds, CancellationToken ct = default)
    {
        if (resourceIds.Count == 0) return [];

        await using var conn = connectionFactory.CreateOrgConnection(orgContext);
        var absences = await conn.QueryListAsync(
            $"SELECT {Cols} FROM resource_absences WHERE resource_id = ANY(@ids) AND enabled = true ORDER BY start_ts",
            p => p.AddWithValue("ids", resourceIds.ToArray()),
            SchedulingMapper.MapResourceAbsenceFromReader, ct);

        return absences.GroupBy(x => x.ResourceId).ToDictionary(g => g.Key, g => g.ToList());
    }
}
