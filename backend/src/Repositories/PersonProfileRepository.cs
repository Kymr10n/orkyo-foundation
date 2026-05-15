using Api.Models;
using Api.Services;
using Npgsql;

namespace Api.Repositories;

public interface IPersonProfileRepository
{
    Task<PersonProfileInfo?> GetByResourceIdAsync(Guid resourceId);
    Task<PersonProfileInfo?> GetByLinkedUserIdAsync(Guid userId);
    Task<PersonProfileInfo> UpsertAsync(Guid resourceId, UpsertPersonProfileRequest request);
    Task<bool> LinkUserAsync(Guid resourceId, Guid userId);
    Task<bool> UnlinkUserAsync(Guid resourceId);
}

public class PersonProfileRepository(OrgContext orgContext, IOrgDbConnectionFactory connectionFactory)
    : IPersonProfileRepository
{
    // email is CITEXT; cast to text in SELECT so Npgsql's default mapper reads it as a string.
    // (Npgsql 8+ does not bundle a CITEXT type handler.)
    private const string SelectColumns =
        "resource_id, email::text AS email, job_title, department, linked_user_id, notes, created_at, updated_at";

    public async Task<PersonProfileInfo?> GetByResourceIdAsync(Guid resourceId)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        await db.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            $"SELECT {SelectColumns} FROM person_profiles WHERE resource_id = @resourceId", db);
        cmd.Parameters.AddWithValue("resourceId", resourceId);

        await using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? Map(reader) : null;
    }

    public async Task<PersonProfileInfo?> GetByLinkedUserIdAsync(Guid userId)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        await db.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            $"SELECT {SelectColumns} FROM person_profiles WHERE linked_user_id = @userId", db);
        cmd.Parameters.AddWithValue("userId", userId);

        await using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? Map(reader) : null;
    }

    public async Task<PersonProfileInfo> UpsertAsync(Guid resourceId, UpsertPersonProfileRequest request)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        await db.OpenAsync();

        // Single round-trip: INSERT, or UPDATE on conflict. Preserves linked_user_id
        // on update (link/unlink are managed through their own endpoints).
        await using var cmd = new NpgsqlCommand(@$"
            INSERT INTO person_profiles
                (resource_id, email, job_title, department, notes, created_at, updated_at)
            VALUES
                (@resourceId, @email, @jobTitle, @department, @notes, NOW(), NOW())
            ON CONFLICT (resource_id) DO UPDATE SET
                email      = EXCLUDED.email,
                job_title  = EXCLUDED.job_title,
                department = EXCLUDED.department,
                notes      = EXCLUDED.notes,
                updated_at = NOW()
            RETURNING {SelectColumns}", db);

        cmd.Parameters.AddWithValue("resourceId", resourceId);
        cmd.Parameters.AddWithValue("email", NullableParam(request.Email));
        cmd.Parameters.AddWithValue("jobTitle", NullableParam(request.JobTitle));
        cmd.Parameters.AddWithValue("department", NullableParam(request.Department));
        cmd.Parameters.AddWithValue("notes", NullableParam(request.Notes));

        await using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? Map(reader) : throw new InvalidOperationException("Upsert failed");
    }

    private static object NullableParam(object? value) => value ?? DBNull.Value;

    public async Task<bool> LinkUserAsync(Guid resourceId, Guid userId)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        await db.OpenAsync();

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

        var rowsAffected = await cmd.ExecuteNonQueryAsync();
        return rowsAffected > 0;
    }

    public async Task<bool> UnlinkUserAsync(Guid resourceId)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        await db.OpenAsync();

        await using var cmd = new NpgsqlCommand(@"
            UPDATE person_profiles SET linked_user_id = NULL, updated_at = NOW()
            WHERE resource_id = @resourceId AND linked_user_id IS NOT NULL", db);

        cmd.Parameters.AddWithValue("resourceId", resourceId);

        var rowsAffected = await cmd.ExecuteNonQueryAsync();
        return rowsAffected > 0;
    }

    private static PersonProfileInfo Map(NpgsqlDataReader reader) =>
        new PersonProfileInfo
        {
            ResourceId = reader.GetGuid(0),
            Email = reader.IsDBNull(1) ? null : reader.GetString(1),
            JobTitle = reader.IsDBNull(2) ? null : reader.GetString(2),
            Department = reader.IsDBNull(3) ? null : reader.GetString(3),
            LinkedUserId = reader.IsDBNull(4) ? null : reader.GetGuid(4),
            Notes = reader.IsDBNull(5) ? null : reader.GetString(5),
            CreatedAt = reader.GetDateTime(6),
            UpdatedAt = reader.GetDateTime(7)
        };
}
