using Api.Models;
using Api.Services;
using Npgsql;

namespace Api.Repositories;

public class DepartmentRepository(OrgContext orgContext, IOrgDbConnectionFactory connectionFactory)
    : IDepartmentRepository
{
    private const string SelectColumns =
        "id, parent_department_id, name, code, description, is_active, created_at, updated_at";

    public async Task<List<DepartmentInfo>> GetAllAsync(bool includeInactive = false)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        await db.OpenAsync();

        var sql = includeInactive
            ? $"SELECT {SelectColumns} FROM departments ORDER BY name"
            : $"SELECT {SelectColumns} FROM departments WHERE is_active ORDER BY name";

        await using var cmd = new NpgsqlCommand(sql, db);
        var rows = new List<DepartmentInfo>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) rows.Add(Map(reader));
        return rows;
    }

    public async Task<List<DepartmentTreeNode>> GetTreeAsync(bool includeInactive = false)
    {
        // Single-query fetch + in-memory tree assembly. Cheaper than recursive
        // CTE round-trips when the whole tree fits in memory (always, for org
        // hierarchies of realistic size).
        var flat = await GetAllAsync(includeInactive);
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

    public async Task<DepartmentInfo?> GetByIdAsync(Guid id)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        await db.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            $"SELECT {SelectColumns} FROM departments WHERE id = @id", db);
        cmd.Parameters.AddWithValue("id", id);

        await using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? Map(reader) : null;
    }

    public async Task<DepartmentInfo> CreateAsync(CreateDepartmentRequest request)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        await db.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            $@"INSERT INTO departments (parent_department_id, name, code, description)
               VALUES (@parent, @name, @code, @description)
               RETURNING {SelectColumns}", db);
        cmd.Parameters.AddWithValue("parent", NullableParam(request.ParentDepartmentId));
        cmd.Parameters.AddWithValue("name", request.Name);
        cmd.Parameters.AddWithValue("code", NullableParam(request.Code));
        cmd.Parameters.AddWithValue("description", NullableParam(request.Description));

        try
        {
            await using var reader = await cmd.ExecuteReaderAsync();
            await reader.ReadAsync();
            return Map(reader);
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            // ux_departments_root_name or ux_departments_sibling_name violation
            throw new InvalidOperationException(
                "A department with this name already exists under the same parent");
        }
        catch (PostgresException ex) when (ex.SqlState == "23503")
        {
            // FK violation (parent does not exist)
            throw new InvalidOperationException("Parent department does not exist");
        }
    }

    public async Task<DepartmentInfo?> UpdateAsync(Guid id, UpdateDepartmentRequest request)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        await db.OpenAsync();

        // If the caller wants to reparent, validate first. Self-parent is rejected
        // by the CHECK constraint at the DB layer; circular hierarchies require an
        // explicit ancestor walk because the FK alone can't catch them.
        if (request.ChangeParent && request.ParentDepartmentId is { } proposedParent)
        {
            if (proposedParent == id)
                throw new InvalidOperationException("A department cannot be its own parent");
            if (await WouldCreateCycleAsync(id, proposedParent, db))
                throw new InvalidOperationException(
                    "Reparenting would create a circular hierarchy");
        }

        var sets = new List<string>();
        await using var cmd = new NpgsqlCommand { Connection = db };
        cmd.Parameters.AddWithValue("id", id);

        if (request.Name is not null)
        {
            sets.Add("name = @name");
            cmd.Parameters.AddWithValue("name", request.Name);
        }
        if (request.ChangeParent)
        {
            sets.Add("parent_department_id = @parent");
            cmd.Parameters.AddWithValue("parent", NullableParam(request.ParentDepartmentId));
        }
        if (request.Code is not null)
        {
            sets.Add("code = @code");
            cmd.Parameters.AddWithValue("code", request.Code);
        }
        if (request.Description is not null)
        {
            sets.Add("description = @description");
            cmd.Parameters.AddWithValue("description", request.Description);
        }
        if (request.IsActive is not null)
        {
            sets.Add("is_active = @isActive");
            cmd.Parameters.AddWithValue("isActive", request.IsActive.Value);
        }

        if (sets.Count == 0) return await GetByIdAsync(id);

        cmd.CommandText =
            $"UPDATE departments SET {string.Join(", ", sets)} WHERE id = @id RETURNING {SelectColumns}";

        try
        {
            await using var reader = await cmd.ExecuteReaderAsync();
            return await reader.ReadAsync() ? Map(reader) : null;
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            throw new InvalidOperationException(
                "A department with this name already exists under the same parent");
        }
        catch (PostgresException ex) when (ex.SqlState == "23503")
        {
            throw new InvalidOperationException("Parent department does not exist");
        }
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        await db.OpenAsync();

        try
        {
            await using var cmd = new NpgsqlCommand("DELETE FROM departments WHERE id = @id", db);
            cmd.Parameters.AddWithValue("id", id);
            return await cmd.ExecuteNonQueryAsync() > 0;
        }
        catch (PostgresException ex) when (ex.SqlState == "23503")
        {
            // FK RESTRICT from a child department blocks the delete.
            throw new InvalidOperationException(
                "Cannot delete a department that has child departments. Move or delete the children first.");
        }
    }

    /// <summary>
    /// Walks the ancestor chain of the proposed parent. If we encounter the moved
    /// node anywhere in that chain, reparenting would create a cycle. Mirrors the
    /// pattern in <see cref="RequestRepository.WouldCreateCycleAsync"/>.
    /// </summary>
    private static async Task<bool> WouldCreateCycleAsync(Guid id, Guid proposedParentId, NpgsqlConnection db)
    {
        await using var cmd = new NpgsqlCommand(
            @"WITH RECURSIVE ancestors AS (
                SELECT parent_department_id FROM departments WHERE id = @new_parent_id
                UNION ALL
                SELECT d.parent_department_id FROM departments d
                  JOIN ancestors a ON d.id = a.parent_department_id
                WHERE d.parent_department_id IS NOT NULL
              )
              SELECT EXISTS(SELECT 1 FROM ancestors WHERE parent_department_id = @id)", db);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("new_parent_id", proposedParentId);
        return (bool)(await cmd.ExecuteScalarAsync())!;
    }

    private static object NullableParam(object? value) => value ?? DBNull.Value;

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
