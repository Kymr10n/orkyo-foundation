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

    public async Task<ResourceAbsenceInfo?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = connectionFactory.CreateOrgConnection(orgContext);
        return await conn.QuerySingleOrDefaultAsync(
            $"SELECT {Cols} FROM resource_absences WHERE id = @id",
            p => p.AddWithValue("id", id),
            SchedulingMapper.MapResourceAbsenceFromReader, ct);
    }

    public async Task<ResourceAbsenceInfo> CreateAsync(Guid resourceId, CreateResourceAbsenceRequest request, CancellationToken ct = default)
    {
        await using var conn = connectionFactory.CreateOrgConnection(orgContext);

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

    public async Task<ResourceAbsenceInfo?> UpdateAsync(Guid id, UpdateResourceAbsenceRequest request, CancellationToken ct = default)
    {
        await using var conn = connectionFactory.CreateOrgConnection(orgContext);

        var existing = await conn.QuerySingleOrDefaultAsync(
            $"SELECT {Cols} FROM resource_absences WHERE id = @id",
            p => p.AddWithValue("id", id),
            SchedulingMapper.MapResourceAbsenceFromReader, ct);
        if (existing is null) return null;

        var isRecurring = request.IsRecurring ?? existing.IsRecurring;
        var recurrenceRule = isRecurring ? (request.RecurrenceRule ?? existing.RecurrenceRule) : null;

        return await conn.QuerySingleOrDefaultAsync($@"
            UPDATE resource_absences SET
                absence_type    = @absenceType,
                title           = @title,
                notes           = @notes,
                start_ts        = @startTs,
                end_ts          = @endTs,
                is_recurring    = @isRecurring,
                recurrence_rule = @recurrenceRule,
                enabled         = @enabled
            WHERE id = @id
            RETURNING {Cols}",
            p =>
            {
                p.AddWithValue("id", id);
                p.AddWithValue("absenceType", EnumMapper.ToDbValue(request.AbsenceType ?? existing.AbsenceType));
                p.AddWithValue("title", request.Title ?? existing.Title);
                p.AddNullable("notes", request.Notes ?? existing.Notes);
                p.AddWithValue("startTs", request.StartTs ?? existing.StartTs);
                p.AddWithValue("endTs", request.EndTs ?? existing.EndTs);
                p.AddWithValue("isRecurring", isRecurring);
                p.AddNullable("recurrenceRule", recurrenceRule);
                p.AddWithValue("enabled", request.Enabled ?? existing.Enabled);
            }, SchedulingMapper.MapResourceAbsenceFromReader, ct);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = connectionFactory.CreateOrgConnection(orgContext);
        return await conn.ExecuteAsync("DELETE FROM resource_absences WHERE id = @id",
            p => p.AddWithValue("id", id), ct) > 0;
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

        var map = new Dictionary<Guid, List<ResourceAbsenceInfo>>();
        foreach (var absence in absences)
        {
            if (!map.TryGetValue(absence.ResourceId, out var list))
            {
                list = [];
                map[absence.ResourceId] = list;
            }
            list.Add(absence);
        }
        return map;
    }
}
