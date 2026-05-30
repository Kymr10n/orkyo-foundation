using Api.Helpers;
using Api.Models;
using Api.Services;
using Npgsql;

namespace Api.Repositories;

public class DepartmentRepository(OrgContext orgContext, IOrgDbConnectionFactory connectionFactory)
    : IDepartmentRepository
{
    private const string SelectColumns =
        "id, parent_department_id, name, code, description, is_active, created_at, updated_at";

    public async Task<List<DepartmentInfo>> GetAllAsync(bool includeInactive = false, CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);

        var sql = includeInactive
            ? $"SELECT {SelectColumns} FROM departments ORDER BY name"
            : $"SELECT {SelectColumns} FROM departments WHERE is_active ORDER BY name";

        return await db.QueryListAsync(sql, null, Map, ct);
    }

    public async Task<List<DepartmentTreeNode>> GetTreeAsync(bool includeInactive = false, CancellationToken ct = default)
    {
        // Single-query fetch + in-memory tree assembly. Cheaper than recursive
        // CTE round-trips when the whole tree fits in memory (always, for org
        // hierarchies of realistic size).
        var flat = await GetAllAsync(includeInactive, ct);
        var byId = flat.ToDictionary(d => d.Id, ToTreeNode);
        var roots = new List<DepartmentTreeNode>();
        foreach (var node in byId.Values)
        {
            if (node.ParentDepartmentId is { } parentId && byId.TryGetValue(parentId, out var parent))
                parent.Children.Add(node);
            else
                roots.Add(node);
        }
        return roots;
    }

    public async Task<DepartmentInfo?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        return await db.QuerySingleOrDefaultAsync(
            $"SELECT {SelectColumns} FROM departments WHERE id = @id",
            p => p.AddWithValue("id", id), Map, ct);
    }

    public async Task<DepartmentInfo> CreateAsync(CreateDepartmentRequest request, CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);

        try
        {
            // INSERT … RETURNING always yields a row on success.
            return (await db.QuerySingleOrDefaultAsync(
                $@"INSERT INTO departments (parent_department_id, name, code, description)
                   VALUES (@parent, @name, @code, @description)
                   RETURNING {SelectColumns}",
                p =>
                {
                    p.AddNullable("parent", request.ParentDepartmentId);
                    p.AddWithValue("name", request.Name);
                    p.AddNullable("code", request.Code);
                    p.AddNullable("description", request.Description);
                }, Map, ct))!;
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            // ux_departments_root_name or ux_departments_sibling_name violation
            throw new ConflictException("A department with this name already exists under the same parent");
        }
        catch (PostgresException ex) when (ex.SqlState == "23503")
        {
            // FK violation (parent does not exist)
            throw new ArgumentException("Parent department does not exist");
        }
    }

    public async Task<DepartmentInfo?> UpdateAsync(Guid id, UpdateDepartmentRequest request, CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);

        // If the caller wants to reparent, validate first. Self-parent is rejected
        // by the CHECK constraint at the DB layer; circular hierarchies require an
        // explicit ancestor walk because the FK alone can't catch them.
        if (request.ChangeParent && request.ParentDepartmentId is { } proposedParent)
        {
            if (proposedParent == id)
                throw new ConflictException("A department cannot be its own parent");
            if (await WouldCreateCycleAsync(id, proposedParent, db, ct))
                throw new ConflictException("Reparenting would create a circular hierarchy");
        }

        var update = new UpdateBuilder();
        update.SetIfNotNull("name", request.Name);
        if (request.ChangeParent)
            update.Set("parent_department_id", request.ParentDepartmentId ?? (object)DBNull.Value);
        update.SetIfNotNull("code", request.Code);
        update.SetIfNotNull("description", request.Description);
        if (request.IsActive is not null)
            update.Set("is_active", request.IsActive.Value);

        if (update.IsEmpty) return await GetByIdAsync(id, ct);

        try
        {
            return await db.QuerySingleOrDefaultAsync(
                $"UPDATE departments SET {update.SetClause} WHERE id = @id RETURNING {SelectColumns}",
                p =>
                {
                    p.AddWithValue("id", id);
                    update.Apply(p);
                }, Map, ct);
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            throw new ConflictException("A department with this name already exists under the same parent");
        }
        catch (PostgresException ex) when (ex.SqlState == "23503")
        {
            throw new ArgumentException("Parent department does not exist");
        }
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);

        try
        {
            return await db.ExecuteAsync("DELETE FROM departments WHERE id = @id",
                p => p.AddWithValue("id", id), ct) > 0;
        }
        catch (PostgresException ex) when (ex.SqlState == "23503")
        {
            // FK RESTRICT from a child department blocks the delete.
            throw new ConflictException("Cannot delete a department that has child departments. Move or delete the children first.");
        }
    }

    /// <summary>
    /// Walks the ancestor chain of the proposed parent. If we encounter the moved
    /// node anywhere in that chain, reparenting would create a cycle. Mirrors the
    /// pattern in <see cref="RequestRepository.WouldCreateCycleAsync"/>.
    /// </summary>
    private static async Task<bool> WouldCreateCycleAsync(Guid id, Guid proposedParentId, NpgsqlConnection db, CancellationToken ct = default)
        => await db.ExecuteScalarAsync<bool>(
            @"WITH RECURSIVE ancestors AS (
                SELECT parent_department_id FROM departments WHERE id = @new_parent_id
                UNION ALL
                SELECT d.parent_department_id FROM departments d
                  JOIN ancestors a ON d.id = a.parent_department_id
                WHERE d.parent_department_id IS NOT NULL
              )
              SELECT EXISTS(SELECT 1 FROM ancestors WHERE parent_department_id = @id)",
            p =>
            {
                p.AddWithValue("id", id);
                p.AddWithValue("new_parent_id", proposedParentId);
            }, ct);

    private static DepartmentInfo Map(NpgsqlDataReader reader) => new()
    {
        Id = reader.GetGuid(0),
        ParentDepartmentId = reader.IsDBNull(1) ? null : reader.GetGuid(1),
        Name = reader.GetString(2),
        Code = reader.IsDBNull(3) ? null : reader.GetString(3),
        Description = reader.IsDBNull(4) ? null : reader.GetString(4),
        IsActive = reader.GetBoolean(5),
        CreatedAt = reader.GetDateTime(6),
        UpdatedAt = reader.GetDateTime(7),
    };

    private static DepartmentTreeNode ToTreeNode(DepartmentInfo d) => new()
    {
        Id = d.Id,
        ParentDepartmentId = d.ParentDepartmentId,
        Name = d.Name,
        Code = d.Code,
        Description = d.Description,
        IsActive = d.IsActive,
        Children = [],
    };
}
