using Api.Models;
using Api.Services;

using static Api.Repositories.RequestSql;

namespace Api.Repositories;

/// <inheritdoc cref="IRequestTreeRepository" />
public class RequestTreeRepository : IRequestTreeRepository
{
    private readonly OrgContext _orgContext;
    private readonly IOrgDbConnectionFactory _connectionFactory;

    public RequestTreeRepository(OrgContext orgContext, IOrgDbConnectionFactory connectionFactory)
    {
        _orgContext = orgContext;
        _connectionFactory = connectionFactory;
    }

    public async Task<List<RequestInfo>> GetChildrenAsync(Guid parentId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);

        return await conn.QueryListAsync(
            $"SELECT {SelectFromView} FROM v_requests_with_assignments WHERE parent_request_id = @parent_id ORDER BY sort_order, created_at",
            p => p.AddWithValue("parent_id", parentId),
            RequestMapper.MapFromReader,
            ct);
    }

    public async Task<RequestInfo?> MoveAsync(Guid id, Guid? newParentId, int sortOrder, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);

        var updatedId = await conn.ExecuteScalarAsync<Guid?>(
            $@"UPDATE requests
               SET parent_request_id = @parent_id, sort_order = @sort_order, updated_at = NOW()
               WHERE id = @id
               RETURNING id",
            p =>
            {
                p.AddWithValue("id", id);
                p.AddNullable("parent_id", newParentId);
                p.AddWithValue("sort_order", sortOrder);
            }, ct);
        if (!updatedId.HasValue)
            return null;

        // Re-read from view to get full object with assignments
        return await ReadByIdAsync(conn, updatedId.Value, ct);
    }

    public async Task<int> GetDescendantCountAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);

        return await conn.ExecuteScalarAsync<int>(
            @"WITH RECURSIVE subtree AS (
                SELECT id FROM requests WHERE parent_request_id = @id
                UNION ALL
                SELECT r.id FROM requests r JOIN subtree s ON r.parent_request_id = s.id
              )
              SELECT COUNT(*)::int FROM subtree",
            p => p.AddWithValue("id", id), ct);
    }

    public async Task<bool> WouldCreateCycleAsync(Guid requestId, Guid newParentId, CancellationToken ct = default)
    {
        if (requestId == newParentId) return true;

        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);

        return await conn.ExecuteScalarAsync<bool>(
            @"WITH RECURSIVE ancestors AS (
                SELECT parent_request_id FROM requests WHERE id = @new_parent_id
                UNION ALL
                SELECT r.parent_request_id FROM requests r JOIN ancestors a ON r.id = a.parent_request_id
                WHERE r.parent_request_id IS NOT NULL
              )
              SELECT EXISTS(SELECT 1 FROM ancestors WHERE parent_request_id = @request_id)",
            p =>
            {
                p.AddWithValue("request_id", requestId);
                p.AddWithValue("new_parent_id", newParentId);
            }, ct);
    }

    public async Task<bool> HasChildrenAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);

        return await conn.ExecuteScalarAsync<bool>(
            "SELECT EXISTS(SELECT 1 FROM requests WHERE parent_request_id = @id)",
            p => p.AddWithValue("id", id), ct);
    }

    public async Task<int> DeleteSubtreeAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);

        // One statement: count the subtree (root + descendants, snapshotted before the delete)
        // and delete the root — children go via the FK cascade. Returns 0 when the root is absent.
        return await conn.ExecuteScalarAsync<int>(
            @"WITH RECURSIVE subtree AS (
                SELECT id FROM requests WHERE id = @id
                UNION ALL
                SELECT r.id FROM requests r JOIN subtree s ON r.parent_request_id = s.id
              ),
              deleted AS (
                DELETE FROM requests WHERE id = @id RETURNING id
              )
              SELECT CASE WHEN EXISTS (SELECT 1 FROM deleted)
                          THEN (SELECT COUNT(*)::int FROM subtree)
                          ELSE 0 END",
            p => p.AddWithValue("id", id), ct);
    }
}
