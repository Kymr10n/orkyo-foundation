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

        return (await db.QuerySingleOrDefaultAsync(@"
            INSERT INTO resource_assignments
                (request_id, resource_id, start_utc, end_utc,
                 allocation_percent, allocation_units)
            VALUES
                (@requestId, @resourceId, @startUtc, @endUtc,
                 @allocationPercent, @allocationUnits)
            RETURNING id, assignment_status, created_at, updated_at,
                      (SELECT rt.key FROM resources res
                       JOIN resource_types rt ON rt.id = res.resource_type_id
                       WHERE res.id = resource_id) AS resource_type_key",
            p =>
            {
                p.AddWithValue("requestId", request.RequestId);
                p.AddWithValue("resourceId", request.ResourceId);
                p.AddWithValue("startUtc", request.StartUtc);
                p.AddWithValue("endUtc", request.EndUtc);
                p.AddNullable("allocationPercent", request.AllocationPercent);
                p.AddNullable("allocationUnits", request.AllocationUnits);
            },
            r => new ResourceAssignmentInfo
            {
                Id = r.GetGuid(r.GetOrdinal("id")),
                RequestId = request.RequestId,
                ResourceId = request.ResourceId,
                ResourceTypeKey = r.GetString(r.GetOrdinal("resource_type_key")),
                StartUtc = request.StartUtc,
                EndUtc = request.EndUtc,
                AllocationPercent = request.AllocationPercent,
                AllocationUnits = request.AllocationUnits,
                AssignmentStatus = r.GetString(r.GetOrdinal("assignment_status")),
                CreatedAt = r.GetDateTime(r.GetOrdinal("created_at")),
                UpdatedAt = r.GetDateTime(r.GetOrdinal("updated_at")),
            }, ct))!;
    }

    public async Task<ResourceAssignmentInfo?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        return await db.QuerySingleOrDefaultAsync(
            $"SELECT {SelectColumns} {FromJoin} WHERE ra.id = @id",
            p => p.AddWithValue("id", id), Map, ct);
    }

    public async Task<List<ResourceAssignmentInfo>> GetByRequestAsync(Guid requestId, CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        return await db.QueryListAsync(
            $"SELECT {SelectColumns} {FromJoin} WHERE ra.request_id = @requestId ORDER BY ra.start_utc",
            p => p.AddWithValue("requestId", requestId), Map, ct);
    }

    public async Task<List<ResourceAssignmentInfo>> GetByResourceAsync(
        Guid resourceId, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        return await db.QueryListAsync(
            $"SELECT {SelectColumns} {FromJoin} " +
            "WHERE ra.resource_id = @resourceId " +
            "  AND ra.assignment_status != @cancelled " +
            "  AND ra.start_utc < @toUtc AND ra.end_utc > @fromUtc " +
            "ORDER BY ra.start_utc",
            p =>
            {
                p.AddWithValue("resourceId", resourceId);
                p.AddWithValue("cancelled", AssignmentStatuses.Cancelled);
                p.AddWithValue("fromUtc", fromUtc);
                p.AddWithValue("toUtc", toUtc);
            }, Map, ct);
    }

    public async Task<List<ResourceAssignmentInfo>> GetOverlappingActiveAsync(
        Guid resourceId, DateTime startUtc, DateTime endUtc, Guid? excludeAssignmentId = null, CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        var excludeClause = excludeAssignmentId.HasValue ? "AND ra.id != @excludeId" : "";
        return await db.QueryListAsync(
            $"SELECT {SelectColumns} {FromJoin} " +
            "WHERE ra.resource_id = @resourceId " +
            $"  AND ra.assignment_status != @cancelled " +
            $"  AND ra.start_utc < @endUtc AND ra.end_utc > @startUtc " +
            $"  {excludeClause}",
            p =>
            {
                p.AddWithValue("resourceId", resourceId);
                p.AddWithValue("cancelled", AssignmentStatuses.Cancelled);
                p.AddWithValue("startUtc", startUtc);
                p.AddWithValue("endUtc", endUtc);
                if (excludeAssignmentId.HasValue) p.AddWithValue("excludeId", excludeAssignmentId.Value);
            }, Map, ct);
    }

    public async Task<decimal> GetTotalAllocatedPercentAsync(
        Guid resourceId, DateTime startUtc, DateTime endUtc, Guid? excludeAssignmentId = null, CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        var excludeClause = excludeAssignmentId.HasValue ? "AND id != @excludeId" : "";
        return await db.ExecuteScalarAsync<decimal>(
            "SELECT COALESCE(SUM(allocation_percent), 0) FROM resource_assignments " +
            "WHERE resource_id = @resourceId " +
            $"  AND assignment_status != @cancelled " +
            $"  AND start_utc < @endUtc AND end_utc > @startUtc " +
            $"  {excludeClause}",
            p =>
            {
                p.AddWithValue("resourceId", resourceId);
                p.AddWithValue("cancelled", AssignmentStatuses.Cancelled);
                p.AddWithValue("startUtc", startUtc);
                p.AddWithValue("endUtc", endUtc);
                if (excludeAssignmentId.HasValue) p.AddWithValue("excludeId", excludeAssignmentId.Value);
            }, ct);
    }

    public async Task<bool> CancelAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        return await db.ExecuteAsync(
            "UPDATE resource_assignments " +
            "SET assignment_status = @cancelled, updated_at = NOW() " +
            "WHERE id = @id AND assignment_status != @cancelled",
            p =>
            {
                p.AddWithValue("id", id);
                p.AddWithValue("cancelled", AssignmentStatuses.Cancelled);
            }, ct) > 0;
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
