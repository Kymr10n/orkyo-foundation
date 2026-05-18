using Api.Models;
using Api.Services;
using Npgsql;

namespace Api.Repositories;

public interface IPersonProfileRepository
{
    Task<PersonProfileInfo?> GetByResourceIdAsync(Guid resourceId, CancellationToken ct = default);
    Task<PersonProfileInfo?> GetByLinkedUserIdAsync(Guid userId, CancellationToken ct = default);
    Task<PersonProfileInfo> UpsertAsync(Guid resourceId, UpsertPersonProfileRequest request, CancellationToken ct = default);
    Task<bool> LinkUserAsync(Guid resourceId, Guid userId, CancellationToken ct = default);
    Task<bool> UnlinkUserAsync(Guid resourceId, CancellationToken ct = default);
}

public class PersonProfileRepository(OrgContext orgContext, IOrgDbConnectionFactory connectionFactory)
    : IPersonProfileRepository
{
    // SELECT for both single-row and linked-user lookups. Builds two derived
    // display-only fields:
    //   - job_title_name: simple LEFT JOIN to job_titles
    //   - department_path: recursive CTE walking from the person's department up
    //     to the root, joined back as a "/"-separated string (e.g. "Operations / Maintenance / Electrical").
    // email is CITEXT — Npgsql 8+ has no CITEXT handler, so we cast to text.
    private const string SelectSqlBody =
        @"WITH RECURSIVE dept_chain AS (
              -- Anchor: every department is its own chain of length 1, tagged with its
              -- original id (start_id) so we can group by it after the recursion.
              SELECT id AS start_id, parent_department_id, ARRAY[name]::text[] AS path_parts
              FROM departments
              UNION ALL
              -- Walk one step up the tree, PREPENDING the parent's name. Building the
              -- array in root-to-leaf order from the start avoids needing array_reverse
              -- (a Postgres 17+ feature; we target 16).
              SELECT dc.start_id, d.parent_department_id, ARRAY[d.name]::text[] || dc.path_parts
              FROM departments d JOIN dept_chain dc ON d.id = dc.parent_department_id
          ),
          dept_path AS (
              -- The fully-walked chain is the row where the recursion hit the root
              -- (parent IS NULL). For root departments, the anchor row already matches.
              SELECT start_id AS id, array_to_string(path_parts, ' / ') AS path
              FROM dept_chain
              WHERE parent_department_id IS NULL
          )
          SELECT p.resource_id, p.email::text AS email,
                 p.job_title_id, p.department_id,
                 p.linked_user_id, p.notes, p.created_at, p.updated_at,
                 jt.name AS job_title_name,
                 dp.path AS department_path
          FROM person_profiles p
          LEFT JOIN job_titles jt ON jt.id = p.job_title_id
          LEFT JOIN dept_path dp  ON dp.id = p.department_id";

    public async Task<PersonProfileInfo?> GetByResourceIdAsync(Guid resourceId, CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        await db.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(
            $"{SelectSqlBody} WHERE p.resource_id = @resourceId", db);
        cmd.Parameters.AddWithValue("resourceId", resourceId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Map(reader) : null;
    }

    public async Task<PersonProfileInfo?> GetByLinkedUserIdAsync(Guid userId, CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        await db.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(
            $"{SelectSqlBody} WHERE p.linked_user_id = @userId", db);
        cmd.Parameters.AddWithValue("userId", userId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Map(reader) : null;
    }

    public async Task<PersonProfileInfo> UpsertAsync(Guid resourceId, UpsertPersonProfileRequest request, CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        await db.OpenAsync(ct);

        // Single round-trip: INSERT, or UPDATE on conflict. Preserves linked_user_id
        // on update (link/unlink are managed through their own endpoints). FK
        // violations on job_title_id / department_id surface as PostgresException
        // 23503 → mapped to BadRequest by EndpointHelpers via ArgumentException.
        await using var cmd = new NpgsqlCommand(@"
            INSERT INTO person_profiles
                (resource_id, email, job_title_id, department_id, notes, created_at, updated_at)
            VALUES
                (@resourceId, @email, @jobTitleId, @departmentId, @notes, NOW(), NOW())
            ON CONFLICT (resource_id) DO UPDATE SET
                email         = EXCLUDED.email,
                job_title_id  = EXCLUDED.job_title_id,
                department_id = EXCLUDED.department_id,
                notes         = EXCLUDED.notes,
                updated_at    = NOW()
            RETURNING resource_id", db);

        cmd.Parameters.AddWithValue("resourceId", resourceId);
        cmd.Parameters.AddWithValue("email", NullableParam(request.Email));
        cmd.Parameters.AddWithValue("jobTitleId", NullableParam(request.JobTitleId));
        cmd.Parameters.AddWithValue("departmentId", NullableParam(request.DepartmentId));
        cmd.Parameters.AddWithValue("notes", NullableParam(request.Notes));

        try
        {
            await cmd.ExecuteScalarAsync(ct);
        }
        catch (PostgresException ex) when (ex.SqlState == "23503")
        {
            throw new ArgumentException(
                "job_title_id or department_id references a row that does not exist");
        }

        // Re-read with the JOIN to populate resolved names.
        var saved = await GetByResourceIdAsync(resourceId);
        return saved ?? throw new InvalidOperationException("Upsert failed");
    }

    public async Task<bool> LinkUserAsync(Guid resourceId, Guid userId, CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        await db.OpenAsync(ct);

        // First check if the user is already linked to any profile in this tenant
        var existingLink = await GetByLinkedUserIdAsync(userId);
        if (existingLink is not null)
        {
            // User is already linked to a different person profile
            return false;
        }

        // Upsert: if no profile row exists yet for this resource, create one with just the link.
        // This makes "link a user" work as a single admin action without requiring a prior profile save.
        await using var cmd = new NpgsqlCommand(@"
            INSERT INTO person_profiles (resource_id, linked_user_id, created_at, updated_at)
            VALUES (@resourceId, @userId, NOW(), NOW())
            ON CONFLICT (resource_id) DO UPDATE
                SET linked_user_id = @userId, updated_at = NOW()
                WHERE person_profiles.linked_user_id IS NULL
                   OR person_profiles.linked_user_id = @userId", db);

        cmd.Parameters.AddWithValue("resourceId", resourceId);
        cmd.Parameters.AddWithValue("userId", userId);

        var rowsAffected = await cmd.ExecuteNonQueryAsync(ct);
        return rowsAffected > 0;
    }

    public async Task<bool> UnlinkUserAsync(Guid resourceId, CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        await db.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(@"
            UPDATE person_profiles SET linked_user_id = NULL, updated_at = NOW()
            WHERE resource_id = @resourceId AND linked_user_id IS NOT NULL", db);

        cmd.Parameters.AddWithValue("resourceId", resourceId);

        var rowsAffected = await cmd.ExecuteNonQueryAsync(ct);
        return rowsAffected > 0;
    }

    private static object NullableParam(object? value) => value ?? DBNull.Value;

    private static PersonProfileInfo Map(NpgsqlDataReader reader) =>
        new()
        {
            ResourceId = reader.GetGuid(0),
            Email = reader.IsDBNull(1) ? null : reader.GetString(1),
            JobTitleId = reader.IsDBNull(2) ? null : reader.GetGuid(2),
            DepartmentId = reader.IsDBNull(3) ? null : reader.GetGuid(3),
            LinkedUserId = reader.IsDBNull(4) ? null : reader.GetGuid(4),
            Notes = reader.IsDBNull(5) ? null : reader.GetString(5),
            CreatedAt = reader.GetDateTime(6),
            UpdatedAt = reader.GetDateTime(7),
            JobTitleName = reader.IsDBNull(8) ? null : reader.GetString(8),
            DepartmentPath = reader.IsDBNull(9) ? null : reader.GetString(9),
        };
}
