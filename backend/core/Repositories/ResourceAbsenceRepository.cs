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
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(
            $"SELECT {Cols} FROM resource_absences WHERE resource_id = @resourceId ORDER BY start_ts", conn);
        cmd.Parameters.AddWithValue("resourceId", resourceId);

        var list = new List<ResourceAbsenceInfo>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            list.Add(SchedulingMapper.MapResourceAbsenceFromReader(reader));
        return list;
    }

    public async Task<ResourceAbsenceInfo?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = connectionFactory.CreateOrgConnection(orgContext);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(
            $"SELECT {Cols} FROM resource_absences WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", id);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return SchedulingMapper.MapResourceAbsenceFromReader(reader);
    }

    public async Task<ResourceAbsenceInfo> CreateAsync(Guid resourceId, CreateResourceAbsenceRequest request, CancellationToken ct = default)
    {
        await using var conn = connectionFactory.CreateOrgConnection(orgContext);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($@"
            INSERT INTO resource_absences
                (resource_id, absence_type, title, notes, start_ts, end_ts,
                 is_recurring, recurrence_rule, enabled)
            VALUES
                (@resourceId, @absenceType, @title, @notes, @startTs, @endTs,
                 @isRecurring, @recurrenceRule, @enabled)
            RETURNING {Cols}", conn);

        cmd.Parameters.AddWithValue("resourceId", resourceId);
        cmd.Parameters.AddWithValue("absenceType", EnumMapper.ToDbValue(request.AbsenceType));
        cmd.Parameters.AddWithValue("title", request.Title);
        cmd.Parameters.AddWithValue("notes", (object?)request.Notes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("startTs", request.StartTs);
        cmd.Parameters.AddWithValue("endTs", request.EndTs);
        cmd.Parameters.AddWithValue("isRecurring", request.IsRecurring);
        cmd.Parameters.AddWithValue("recurrenceRule", (object?)request.RecurrenceRule ?? DBNull.Value);
        cmd.Parameters.AddWithValue("enabled", request.Enabled);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return SchedulingMapper.MapResourceAbsenceFromReader(reader);
    }

    public async Task<ResourceAbsenceInfo?> UpdateAsync(Guid id, UpdateResourceAbsenceRequest request, CancellationToken ct = default)
    {
        await using var conn = connectionFactory.CreateOrgConnection(orgContext);
        await conn.OpenAsync(ct);

        await using var getCmd = new NpgsqlCommand(
            $"SELECT {Cols} FROM resource_absences WHERE id = @id", conn);
        getCmd.Parameters.AddWithValue("id", id);
        await using var getReader = await getCmd.ExecuteReaderAsync(ct);
        if (!await getReader.ReadAsync(ct)) return null;
        var existing = SchedulingMapper.MapResourceAbsenceFromReader(getReader);
        getReader.Close();

        var isRecurring = request.IsRecurring ?? existing.IsRecurring;
        var recurrenceRule = isRecurring ? (request.RecurrenceRule ?? existing.RecurrenceRule) : null;

        await using var cmd = new NpgsqlCommand($@"
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
            RETURNING {Cols}", conn);

        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("absenceType", EnumMapper.ToDbValue(request.AbsenceType ?? existing.AbsenceType));
        cmd.Parameters.AddWithValue("title", request.Title ?? existing.Title);
        cmd.Parameters.AddWithValue("notes", (object?)(request.Notes ?? existing.Notes) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("startTs", request.StartTs ?? existing.StartTs);
        cmd.Parameters.AddWithValue("endTs", request.EndTs ?? existing.EndTs);
        cmd.Parameters.AddWithValue("isRecurring", isRecurring);
        cmd.Parameters.AddWithValue("recurrenceRule", (object?)recurrenceRule ?? DBNull.Value);
        cmd.Parameters.AddWithValue("enabled", request.Enabled ?? existing.Enabled);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return SchedulingMapper.MapResourceAbsenceFromReader(reader);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = connectionFactory.CreateOrgConnection(orgContext);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand("DELETE FROM resource_absences WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", id);
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    public async Task<Dictionary<Guid, List<ResourceAbsenceInfo>>> GetEnabledByResourcesAsync(
        IReadOnlyList<Guid> resourceIds, CancellationToken ct = default)
    {
        if (resourceIds.Count == 0) return [];

        await using var conn = connectionFactory.CreateOrgConnection(orgContext);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(
            $"SELECT {Cols} FROM resource_absences WHERE resource_id = ANY(@ids) AND enabled = true ORDER BY start_ts",
            conn);
        cmd.Parameters.AddWithValue("ids", resourceIds.ToArray());

        var map = new Dictionary<Guid, List<ResourceAbsenceInfo>>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var absence = SchedulingMapper.MapResourceAbsenceFromReader(reader);
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
