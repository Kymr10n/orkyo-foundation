using Api.Constants;
using Api.Helpers;
using Api.Models;
using Api.Services;
using Npgsql;

namespace Api.Repositories;

public interface IResourceAssignmentRepository
{
    /// <summary>Books a resource for a request and changes the request's row version, because its assignments are part of it.</summary>
    Task<ResourceAssignmentInfo> CreateAsync(CreateResourceAssignmentRequest request, CancellationToken ct = default);
    Task<ResourceAssignmentInfo?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<List<ResourceAssignmentInfo>> GetByRequestAsync(Guid requestId, CancellationToken ct = default);
    Task<List<ResourceAssignmentInfo>> GetByResourceAsync(Guid resourceId, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default);
    Task<List<ResourceAssignmentInfo>> GetByResourceTypeAsync(string resourceTypeKey, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default);
    /// <summary>Active (non-cancelled) assignments for many resources overlapping the window — for batch validation.</summary>
    Task<List<ResourceAssignmentInfo>> GetActiveByResourcesAsync(IReadOnlyList<Guid> resourceIds, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default);
    Task<List<ResourceAssignmentInfo>> GetOverlappingActiveAsync(Guid resourceId, DateTime startUtc, DateTime endUtc, Guid? excludeAssignmentId = null, CancellationToken ct = default);
    /// <summary>Cancels an assignment and changes its request's row version. False when not found or already cancelled.</summary>
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

    // A booking occupies its resource unless it or its request is cancelled
    // (docs/scheduling-integrity-spec.md, section 4). Done work still occupies.
    private static readonly string Occupying =
        "ra.assignment_status != @cancelled AND NOT EXISTS (SELECT 1 FROM requests q " +
        $"WHERE q.id = ra.request_id AND q.status = '{EnumMapper.ToDbValue(RequestStatus.Cancelled)}')";

    private const string FromJoin =
        "FROM resource_assignments ra " +
        "JOIN resources res     ON res.id = ra.resource_id " +
        "JOIN resource_types rt ON rt.id  = res.resource_type_id";

    // A request's assignments are part of it, so creating or cancelling one also updates the
    // request row, in the same statement: its row version moves, and an editor who read the
    // request before cannot save over the change unseen.
    public async Task<ResourceAssignmentInfo> CreateAsync(CreateResourceAssignmentRequest request, CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);

        return (await db.QuerySingleOrDefaultAsync(@"
            WITH ins AS (
                INSERT INTO resource_assignments
                    (request_id, resource_id, start_utc, end_utc,
                     allocation_percent, allocation_units)
                VALUES
                    (@requestId, @resourceId, @startUtc, @endUtc,
                     @allocationPercent, @allocationUnits)
                RETURNING id, resource_id, assignment_status, created_at, updated_at),
            touched AS (UPDATE requests SET updated_at = NOW() WHERE id = @requestId)
            SELECT ins.id, ins.assignment_status, ins.created_at, ins.updated_at,
                   rt.key AS resource_type_key
            FROM ins
            JOIN resources res     ON res.id = ins.resource_id
            JOIN resource_types rt ON rt.id  = res.resource_type_id",
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

    public async Task<List<ResourceAssignmentInfo>> GetByResourceTypeAsync(
        string resourceTypeKey, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        return await db.QueryListAsync(
            $"SELECT {SelectColumns} {FromJoin} " +
            "WHERE rt.key = @typeKey " +
            "  AND ra.assignment_status != @cancelled " +
            "  AND ra.start_utc < @toUtc AND ra.end_utc > @fromUtc " +
            "ORDER BY ra.resource_id, ra.start_utc",
            p =>
            {
                p.AddWithValue("typeKey", resourceTypeKey);
                p.AddWithValue("cancelled", AssignmentStatuses.Cancelled);
                p.AddWithValue("fromUtc", fromUtc);
                p.AddWithValue("toUtc", toUtc);
            }, Map, ct);
    }

    public async Task<List<ResourceAssignmentInfo>> GetActiveByResourcesAsync(
        IReadOnlyList<Guid> resourceIds, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default)
    {
        if (resourceIds.Count == 0) return [];
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        return await db.QueryListAsync(
            $"SELECT {SelectColumns} {FromJoin} " +
            "WHERE ra.resource_id = ANY(@ids) " +
            $"  AND {Occupying} " +
            "  AND ra.start_utc < @toUtc AND ra.end_utc > @fromUtc " +
            "ORDER BY ra.resource_id, ra.start_utc",
            p =>
            {
                p.AddWithValue("ids", resourceIds.ToArray());
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
            $"  AND {Occupying} " +
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

    public async Task<bool> CancelAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        // The request is touched only when the cancel took effect (see CreateAsync). The count
        // is of requests touched, so 1 or 0 as before.
        return await db.ExecuteAsync(
            "WITH cancelled AS (" +
            "UPDATE resource_assignments " +
            "SET assignment_status = @cancelled, updated_at = NOW() " +
            "WHERE id = @id AND assignment_status != @cancelled " +
            "RETURNING request_id) " +
            "UPDATE requests SET updated_at = NOW() WHERE id IN (SELECT request_id FROM cancelled)",
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
