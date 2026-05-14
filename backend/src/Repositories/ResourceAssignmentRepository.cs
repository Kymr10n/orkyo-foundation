using Api.Constants;
using Api.Models;
using Api.Services;
using Npgsql;

namespace Api.Repositories;

public interface IResourceAssignmentRepository
{
    Task<ResourceAssignmentInfo> CreateAsync(CreateResourceAssignmentRequest request);
    Task<ResourceAssignmentInfo?> GetByIdAsync(Guid id);
    Task<List<ResourceAssignmentInfo>> GetByRequestAsync(Guid requestId);
    Task<List<ResourceAssignmentInfo>> GetByResourceAsync(Guid resourceId, DateTime fromUtc, DateTime toUtc);
    Task<List<ResourceAssignmentInfo>> GetOverlappingActiveAsync(Guid resourceId, DateTime startUtc, DateTime endUtc, Guid? excludeAssignmentId = null);
    Task<decimal> GetTotalAllocatedPercentAsync(Guid resourceId, DateTime startUtc, DateTime endUtc, Guid? excludeAssignmentId = null);
    Task<bool> CancelAsync(Guid id);
}

public class ResourceAssignmentRepository(OrgContext orgContext, IOrgDbConnectionFactory connectionFactory)
    : IResourceAssignmentRepository
{
    private const string SelectColumns =
        "id, request_id, resource_id, start_utc, end_utc, " +
        "allocation_percent, allocation_units, assignment_status, created_at, updated_at";

    public async Task<ResourceAssignmentInfo> CreateAsync(CreateResourceAssignmentRequest request)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        await db.OpenAsync();

        await using var cmd = new NpgsqlCommand(@"
            INSERT INTO resource_assignments
                (request_id, resource_id, start_utc, end_utc,
                 allocation_percent, allocation_units)
            VALUES
                (@requestId, @resourceId, @startUtc, @endUtc,
                 @allocationPercent, @allocationUnits)
            RETURNING id, assignment_status, created_at, updated_at", db);

        cmd.Parameters.AddWithValue("requestId", request.RequestId);
        cmd.Parameters.AddWithValue("resourceId", request.ResourceId);
        cmd.Parameters.AddWithValue("startUtc", request.StartUtc);
        cmd.Parameters.AddWithValue("endUtc", request.EndUtc);
        cmd.Parameters.AddWithValue("allocationPercent", (object?)request.AllocationPercent ?? DBNull.Value);
        cmd.Parameters.AddWithValue("allocationUnits", (object?)request.AllocationUnits ?? DBNull.Value);

        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();

        return new ResourceAssignmentInfo
        {
            Id = reader.GetGuid(reader.GetOrdinal("id")),
            RequestId = request.RequestId,
            ResourceId = request.ResourceId,
            StartUtc = request.StartUtc,
            EndUtc = request.EndUtc,
            AllocationPercent = request.AllocationPercent,
            AllocationUnits = request.AllocationUnits,
            AssignmentStatus = reader.GetString(reader.GetOrdinal("assignment_status")),
            CreatedAt = reader.GetDateTime(reader.GetOrdinal("created_at")),
            UpdatedAt = reader.GetDateTime(reader.GetOrdinal("updated_at")),
        };
    }

    public async Task<ResourceAssignmentInfo?> GetByIdAsync(Guid id)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        await db.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            $"SELECT {SelectColumns} FROM resource_assignments WHERE id = @id", db);
        cmd.Parameters.AddWithValue("id", id);

        await using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? Map(reader) : null;
    }

    public async Task<List<ResourceAssignmentInfo>> GetByRequestAsync(Guid requestId)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        await db.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            $"SELECT {SelectColumns} FROM resource_assignments " +
            "WHERE request_id = @requestId ORDER BY start_utc", db);
        cmd.Parameters.AddWithValue("requestId", requestId);

        return await ReadAllAsync(cmd);
    }

    public async Task<List<ResourceAssignmentInfo>> GetByResourceAsync(
        Guid resourceId, DateTime fromUtc, DateTime toUtc)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        await db.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            $"SELECT {SelectColumns} FROM resource_assignments " +
            "WHERE resource_id = @resourceId " +
            "  AND assignment_status != @cancelled " +
            "  AND start_utc < @toUtc AND end_utc > @fromUtc " +
            "ORDER BY start_utc", db);
        cmd.Parameters.AddWithValue("resourceId", resourceId);
        cmd.Parameters.AddWithValue("cancelled", AssignmentStatuses.Cancelled);
        cmd.Parameters.AddWithValue("fromUtc", fromUtc);
        cmd.Parameters.AddWithValue("toUtc", toUtc);

        return await ReadAllAsync(cmd);
    }

    public async Task<List<ResourceAssignmentInfo>> GetOverlappingActiveAsync(
        Guid resourceId, DateTime startUtc, DateTime endUtc, Guid? excludeAssignmentId = null)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        await db.OpenAsync();

        var excludeClause = excludeAssignmentId.HasValue ? "AND id != @excludeId" : "";
        await using var cmd = new NpgsqlCommand(
            $"SELECT {SelectColumns} FROM resource_assignments " +
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

        return await ReadAllAsync(cmd);
    }

    public async Task<decimal> GetTotalAllocatedPercentAsync(
        Guid resourceId, DateTime startUtc, DateTime endUtc, Guid? excludeAssignmentId = null)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        await db.OpenAsync();

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

        return (decimal)(await cmd.ExecuteScalarAsync() ?? 0m);
    }

    public async Task<bool> CancelAsync(Guid id)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        await db.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "UPDATE resource_assignments " +
            "SET assignment_status = @cancelled, updated_at = NOW() " +
            "WHERE id = @id AND assignment_status != @cancelled", db);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("cancelled", AssignmentStatuses.Cancelled);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    private static async Task<List<ResourceAssignmentInfo>> ReadAllAsync(NpgsqlCommand cmd)
    {
        var result = new List<ResourceAssignmentInfo>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            result.Add(Map(reader));
        return result;
    }

    private static ResourceAssignmentInfo Map(NpgsqlDataReader r) => new()
    {
        Id = r.GetGuid(r.GetOrdinal("id")),
        RequestId = r.GetGuid(r.GetOrdinal("request_id")),
        ResourceId = r.GetGuid(r.GetOrdinal("resource_id")),
        StartUtc = r.GetDateTime(r.GetOrdinal("start_utc")),
        EndUtc = r.GetDateTime(r.GetOrdinal("end_utc")),
        AllocationPercent = r.IsDBNull(r.GetOrdinal("allocation_percent")) ? null : r.GetDecimal(r.GetOrdinal("allocation_percent")),
        AllocationUnits = r.IsDBNull(r.GetOrdinal("allocation_units")) ? null : r.GetInt32(r.GetOrdinal("allocation_units")),
        AssignmentStatus = r.GetString(r.GetOrdinal("assignment_status")),
        CreatedAt = r.GetDateTime(r.GetOrdinal("created_at")),
        UpdatedAt = r.GetDateTime(r.GetOrdinal("updated_at")),
    };
}
