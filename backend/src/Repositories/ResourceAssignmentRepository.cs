using Api.Constants;
using Api.Models;
using Api.Services;
using Npgsql;

namespace Api.Repositories;

public interface IResourceAssignmentRepository
{
    Task<ResourceAssignmentInfo> CreateAsync(CreateResourceAssignmentRequest request, CancellationToken ct = default);
    Task<ResourceAssignmentInfo?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<List<ResourceAssignmentInfo>> GetByRequestAsync(Guid requestId, CancellationToken ct = default);
    Task<List<ResourceAssignmentInfo>> GetByResourceAsync(Guid resourceId, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default);
    Task<List<ResourceAssignmentInfo>> GetOverlappingActiveAsync(Guid resourceId, DateTime startUtc, DateTime endUtc, Guid? excludeAssignmentId = null, CancellationToken ct = default);
    Task<decimal> GetTotalAllocatedPercentAsync(Guid resourceId, DateTime startUtc, DateTime endUtc, Guid? excludeAssignmentId = null, CancellationToken ct = default);
    Task<bool> CancelAsync(Guid id, CancellationToken ct = default);
}

public class ResourceAssignmentRepository(OrgContext orgContext, IOrgDbConnectionFactory connectionFactory)
    : IResourceAssignmentRepository
{
    private const string SelectColumns =
        "ra.id, ra.request_id, ra.resource_id, rt.key AS resource_type_key, " +
        "ra.start_utc, ra.end_utc, " +
        "ra.allocation_percent, ra.allocation_units, ra.assignment_status, " +
        "ra.created_at, ra.updated_at";

    private const string FromJoin =
        "FROM resource_assignments ra " +
        "JOIN resources res     ON res.id = ra.resource_id " +
        "JOIN resource_types rt ON rt.id  = res.resource_type_id";

    public async Task<ResourceAssignmentInfo> CreateAsync(CreateResourceAssignmentRequest request, CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        await db.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(@"
            INSERT INTO resource_assignments
                (request_id, resource_id, start_utc, end_utc,
                 allocation_percent, allocation_units)
            VALUES
                (@requestId, @resourceId, @startUtc, @endUtc,
                 @allocationPercent, @allocationUnits)
            RETURNING id, assignment_status, created_at, updated_at,
                      (SELECT rt.key FROM resources res
                       JOIN resource_types rt ON rt.id = res.resource_type_id
                       WHERE res.id = resource_id) AS resource_type_key", db);

        cmd.Parameters.AddWithValue("requestId", request.RequestId);
        cmd.Parameters.AddWithValue("resourceId", request.ResourceId);
        cmd.Parameters.AddWithValue("startUtc", request.StartUtc);
        cmd.Parameters.AddWithValue("endUtc", request.EndUtc);
        cmd.Parameters.AddWithValue("allocationPercent", (object?)request.AllocationPercent ?? DBNull.Value);
        cmd.Parameters.AddWithValue("allocationUnits", (object?)request.AllocationUnits ?? DBNull.Value);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);

        return new ResourceAssignmentInfo
        {
            Id = reader.GetGuid(reader.GetOrdinal("id")),
            RequestId = request.RequestId,
            ResourceId = request.ResourceId,
            ResourceTypeKey = reader.GetString(reader.GetOrdinal("resource_type_key")),
            StartUtc = request.StartUtc,
            EndUtc = request.EndUtc,
            AllocationPercent = request.AllocationPercent,
            AllocationUnits = request.AllocationUnits,
            AssignmentStatus = reader.GetString(reader.GetOrdinal("assignment_status")),
            CreatedAt = reader.GetDateTime(reader.GetOrdinal("created_at")),
            UpdatedAt = reader.GetDateTime(reader.GetOrdinal("updated_at")),
        };
    }

    public async Task<ResourceAssignmentInfo?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        await db.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(
            $"SELECT {SelectColumns} {FromJoin} WHERE ra.id = @id", db);
        cmd.Parameters.AddWithValue("id", id);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Map(reader) : null;
    }

    public async Task<List<ResourceAssignmentInfo>> GetByRequestAsync(Guid requestId, CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        await db.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(
            $"SELECT {SelectColumns} {FromJoin} " +
            "WHERE ra.request_id = @requestId ORDER BY ra.start_utc", db);
        cmd.Parameters.AddWithValue("requestId", requestId);

        return await ReadAllAsync(cmd);
    }

    public async Task<List<ResourceAssignmentInfo>> GetByResourceAsync(
        Guid resourceId, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        await db.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(
            $"SELECT {SelectColumns} {FromJoin} " +
            "WHERE ra.resource_id = @resourceId " +
            "  AND ra.assignment_status != @cancelled " +
            "  AND ra.start_utc < @toUtc AND ra.end_utc > @fromUtc " +
            "ORDER BY ra.start_utc", db);
        cmd.Parameters.AddWithValue("resourceId", resourceId);
        cmd.Parameters.AddWithValue("cancelled", AssignmentStatuses.Cancelled);
        cmd.Parameters.AddWithValue("fromUtc", fromUtc);
        cmd.Parameters.AddWithValue("toUtc", toUtc);

        return await ReadAllAsync(cmd);
    }

    public async Task<List<ResourceAssignmentInfo>> GetOverlappingActiveAsync(
        Guid resourceId, DateTime startUtc, DateTime endUtc, Guid? excludeAssignmentId = null, CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        await db.OpenAsync(ct);

        var excludeClause = excludeAssignmentId.HasValue ? "AND ra.id != @excludeId" : "";
        await using var cmd = new NpgsqlCommand(
            $"SELECT {SelectColumns} {FromJoin} " +
            "WHERE ra.resource_id = @resourceId " +
            $"  AND ra.assignment_status != @cancelled " +
            $"  AND ra.start_utc < @endUtc AND ra.end_utc > @startUtc " +
            $"  {excludeClause}", db);
        cmd.Parameters.AddWithValue("resourceId", resourceId);
        cmd.Parameters.AddWithValue("cancelled", AssignmentStatuses.Cancelled);
        cmd.Parameters.AddWithValue("startUtc", startUtc);
        cmd.Parameters.AddWithValue("endUtc", endUtc);
        if (excludeAssignmentId.HasValue)
            cmd.Parameters.AddWithValue("excludeId", excludeAssignmentId.Value);

        return await ReadAllAsync(cmd);
    }

    public async Task<decimal> GetTotalAllocatedPercentAsync(
        Guid resourceId, DateTime startUtc, DateTime endUtc, Guid? excludeAssignmentId = null, CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        await db.OpenAsync(ct);

        var excludeClause = excludeAssignmentId.HasValue ? "AND id != @excludeId" : "";
        await using var cmd = new NpgsqlCommand(
            "SELECT COALESCE(SUM(allocation_percent), 0) FROM resource_assignments " +
            "WHERE resource_id = @resourceId " +
            $"  AND assignment_status != @cancelled " +
            $"  AND start_utc < @endUtc AND end_utc > @startUtc " +
            $"  {excludeClause}", db);
        cmd.Parameters.AddWithValue("resourceId", resourceId);
        cmd.Parameters.AddWithValue("cancelled", AssignmentStatuses.Cancelled);
        cmd.Parameters.AddWithValue("startUtc", startUtc);
        cmd.Parameters.AddWithValue("endUtc", endUtc);
        if (excludeAssignmentId.HasValue)
            cmd.Parameters.AddWithValue("excludeId", excludeAssignmentId.Value);

        return (decimal)(await cmd.ExecuteScalarAsync(ct) ?? 0m);
    }

    public async Task<bool> CancelAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        await db.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(
            "UPDATE resource_assignments " +
            "SET assignment_status = @cancelled, updated_at = NOW() " +
            "WHERE id = @id AND assignment_status != @cancelled", db);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("cancelled", AssignmentStatuses.Cancelled);
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    private static async Task<List<ResourceAssignmentInfo>> ReadAllAsync(NpgsqlCommand cmd, CancellationToken ct = default)
    {
        var result = new List<ResourceAssignmentInfo>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            result.Add(Map(reader));
        return result;
    }

    private static ResourceAssignmentInfo Map(NpgsqlDataReader r) => new()
    {
        Id = r.GetGuid(r.GetOrdinal("id")),
        RequestId = r.GetGuid(r.GetOrdinal("request_id")),
        ResourceId = r.GetGuid(r.GetOrdinal("resource_id")),
        ResourceTypeKey = r.GetString(r.GetOrdinal("resource_type_key")),
        StartUtc = r.GetDateTime(r.GetOrdinal("start_utc")),
        EndUtc = r.GetDateTime(r.GetOrdinal("end_utc")),
        AllocationPercent = r.IsDBNull(r.GetOrdinal("allocation_percent")) ? null : r.GetDecimal(r.GetOrdinal("allocation_percent")),
        AllocationUnits = r.IsDBNull(r.GetOrdinal("allocation_units")) ? null : r.GetInt32(r.GetOrdinal("allocation_units")),
        AssignmentStatus = r.GetString(r.GetOrdinal("assignment_status")),
        CreatedAt = r.GetDateTime(r.GetOrdinal("created_at")),
        UpdatedAt = r.GetDateTime(r.GetOrdinal("updated_at")),
    };
}
