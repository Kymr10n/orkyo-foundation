using Api.Models;
using Api.Services;
using Npgsql;

namespace Api.Repositories;

public class RequestDependencyRepository : IRequestDependencyRepository
{
    private readonly OrgContext _orgContext;
    private readonly IOrgDbConnectionFactory _connectionFactory;

    public RequestDependencyRepository(OrgContext orgContext, IOrgDbConnectionFactory connectionFactory)
    {
        _orgContext = orgContext;
        _connectionFactory = connectionFactory;
    }

    // Every read returns the peer names alongside the ids: a list of edges is always rendered
    // as names, and joining here costs nothing next to a second round trip per row.
    private const string SelectSql = @"
        SELECT d.id, d.predecessor_request_id, d.successor_request_id,
               p.name AS predecessor_name, s.name AS successor_name,
               d.dependency_type, d.lag_minutes, d.created_at
          FROM request_dependencies d
          JOIN requests p ON p.id = d.predecessor_request_id
          JOIN requests s ON s.id = d.successor_request_id";

    private static RequestDependencyInfo Map(NpgsqlDataReader r) => new()
    {
        Id = r.GetGuid(0),
        PredecessorRequestId = r.GetGuid(1),
        SuccessorRequestId = r.GetGuid(2),
        PredecessorName = r.GetString(3),
        SuccessorName = r.GetString(4),
        DependencyType = r.GetString(5),
        LagMinutes = r.GetInt32(6),
        CreatedAt = r.GetDateTime(7)
    };

    public async Task<List<RequestDependencyInfo>> GetAllAsync(Guid? siteId, CancellationToken ct = default)
    {
        await using var db = _connectionFactory.CreateOrgConnection(_orgContext);

        // Site filtering follows the successor: the question a site view asks is "what is
        // blocking work here", and a predecessor at another site is part of that answer.
        var sql = SelectSql
            + (siteId.HasValue ? " WHERE s.site_id = @site_id" : "")
            + " ORDER BY s.name, p.name";

        return await db.QueryListAsync(sql, p =>
        {
            if (siteId.HasValue) p.AddWithValue("site_id", siteId.Value);
        }, Map, ct);
    }

    public async Task<RequestDependencies> GetForRequestAsync(Guid requestId, CancellationToken ct = default)
    {
        await using var db = _connectionFactory.CreateOrgConnection(_orgContext);

        var rows = await db.QueryListAsync(
            SelectSql + " WHERE d.successor_request_id = @id OR d.predecessor_request_id = @id ORDER BY d.created_at",
            p => p.AddWithValue("id", requestId), Map, ct);

        return new RequestDependencies
        {
            Predecessors = rows.Where(e => e.SuccessorRequestId == requestId).ToList(),
            Successors = rows.Where(e => e.PredecessorRequestId == requestId).ToList()
        };
    }

    public async Task<List<RequestDependencyInfo>> GetBySuccessorsAsync(
        IReadOnlyCollection<Guid> successorIds, CancellationToken ct = default)
    {
        if (successorIds.Count == 0) return [];

        await using var db = _connectionFactory.CreateOrgConnection(_orgContext);

        return await db.QueryListAsync(
            SelectSql + " WHERE d.successor_request_id = ANY(@ids)",
            p => p.AddWithValue("ids", successorIds.ToArray()), Map, ct);
    }

    public async Task<RequestDependencyInfo?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = _connectionFactory.CreateOrgConnection(_orgContext);

        return await db.QuerySingleOrDefaultAsync(
            SelectSql + " WHERE d.id = @id",
            p => p.AddWithValue("id", id), Map, ct);
    }

    public async Task<RequestDependencyInfo> CreateAsync(
        Guid predecessorId, Guid successorId, string dependencyType, int lagMinutes,
        CancellationToken ct = default)
    {
        await using var db = _connectionFactory.CreateOrgConnection(_orgContext);

        var id = await db.ExecuteScalarAsync<Guid>(
            @"INSERT INTO request_dependencies
                  (predecessor_request_id, successor_request_id, dependency_type, lag_minutes)
              VALUES (@predecessor_id, @successor_id, @dependency_type, @lag_minutes)
              RETURNING id",
            p =>
            {
                p.AddWithValue("predecessor_id", predecessorId);
                p.AddWithValue("successor_id", successorId);
                p.AddWithValue("dependency_type", dependencyType);
                p.AddWithValue("lag_minutes", lagMinutes);
            }, ct);

        var created = await db.QuerySingleOrDefaultAsync(
            SelectSql + " WHERE d.id = @id",
            p => p.AddWithValue("id", id), Map, ct);

        // The row was just inserted on this connection, so absence is not a normal outcome.
        return created ?? throw new InvalidOperationException("Dependency vanished after insert");
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = _connectionFactory.CreateOrgConnection(_orgContext);

        var affected = await db.ExecuteAsync(
            "DELETE FROM request_dependencies WHERE id = @id",
            p => p.AddWithValue("id", id), ct);

        return affected > 0;
    }

    public async Task<bool> ExistsAsync(Guid predecessorId, Guid successorId, CancellationToken ct = default)
    {
        await using var db = _connectionFactory.CreateOrgConnection(_orgContext);

        return await db.ExecuteScalarAsync<bool>(
            @"SELECT EXISTS(SELECT 1 FROM request_dependencies
                             WHERE predecessor_request_id = @predecessor_id
                               AND successor_request_id = @successor_id)",
            p =>
            {
                p.AddWithValue("predecessor_id", predecessorId);
                p.AddWithValue("successor_id", successorId);
            }, ct);
    }

    public async Task<bool> WouldCreateCycleAsync(Guid predecessorId, Guid successorId, CancellationToken ct = default)
    {
        if (predecessorId == successorId) return true;

        await using var db = _connectionFactory.CreateOrgConnection(_orgContext);

        // Walk forward from the proposed successor. If the proposed predecessor is already
        // downstream of it, the new edge would close a loop. Same shape as the reparent
        // ancestor walk in RequestRepository, following edges instead of parents.
        return await db.ExecuteScalarAsync<bool>(
            @"WITH RECURSIVE downstream AS (
                SELECT successor_request_id AS id
                  FROM request_dependencies
                 WHERE predecessor_request_id = @successor_id
                UNION
                SELECT d.successor_request_id
                  FROM request_dependencies d
                  JOIN downstream x ON d.predecessor_request_id = x.id
              )
              SELECT EXISTS(SELECT 1 FROM downstream WHERE id = @predecessor_id)",
            p =>
            {
                p.AddWithValue("predecessor_id", predecessorId);
                p.AddWithValue("successor_id", successorId);
            }, ct);
    }

    public async Task<bool> HasAnyForRequestAsync(Guid requestId, CancellationToken ct = default)
    {
        await using var db = _connectionFactory.CreateOrgConnection(_orgContext);

        return await db.ExecuteScalarAsync<bool>(
            @"SELECT EXISTS(SELECT 1 FROM request_dependencies
                             WHERE predecessor_request_id = @id OR successor_request_id = @id)",
            p => p.AddWithValue("id", requestId), ct);
    }
}
