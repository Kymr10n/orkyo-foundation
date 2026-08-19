using Api.Models;
using Api.Security.Encryption;
using Api.Services;
using Npgsql;

namespace Api.Repositories;

public interface IPersonProfileRepository
{
    Task<PersonProfileInfo?> GetByResourceIdAsync(Guid resourceId, CancellationToken ct = default);
    /// <summary>Bulk lookup for many directory resources in one round-trip, in place of a per-row
    /// fan-out. Mirrors <see cref="GetByResourceIdAsync"/> including note decryption.</summary>
    Task<List<PersonProfileInfo>> GetByResourceIdsAsync(IReadOnlyList<Guid> resourceIds, CancellationToken ct = default);
    Task<PersonProfileInfo?> GetByLinkedUserIdAsync(Guid userId, CancellationToken ct = default);
    Task<PersonProfileInfo> UpsertAsync(Guid resourceId, UpsertPersonProfileRequest request, CancellationToken ct = default);
    Task<bool> LinkUserAsync(Guid resourceId, Guid userId, CancellationToken ct = default);
    Task<bool> UnlinkUserAsync(Guid resourceId, CancellationToken ct = default);
}

public class PersonProfileRepository(
    OrgContext orgContext,
    IOrgDbConnectionFactory connectionFactory,
    IEncryptionService encryption)
    : IPersonProfileRepository
{
    // notes is confidential free-text → encrypted at rest. email
    // stays plaintext (lookup/display field; encrypting needs a blind index — deferred).
    private PersonProfileInfo Dec(PersonProfileInfo p) => p with
    {
        Notes = encryption.UnprotectString(p.Notes, orgContext.OrgId),
    };

    // SELECT for both single-row and linked-user lookups. The profile columns live on
    // resources now; has_directory_profile is what makes a resource a person, replacing the
    // presence of a person_profiles row. A person who has filled nothing in therefore reads
    // back as an empty profile rather than a 404 — the side table could distinguish "no row"
    // from "row of nulls", and one table cannot. An unknown resource still yields no row.
    // email is CITEXT — Npgsql 8+ has no CITEXT handler, so we cast to text.
    //
    // No joins left: job title and department became organization lists in 1820, so the recursive
    // department-path CTE and the job_titles join went with them. What a person's title is now
    // reads from custom_fields like any other list lookup.
    private const string SelectSqlBody =
        @"SELECT p.id AS resource_id, p.email::text AS email,
                 p.linked_user_id, p.notes, p.created_at, p.updated_at
          FROM resources p
          JOIN resource_types rt ON rt.id = p.resource_type_id AND rt.has_directory_profile";

    public async Task<PersonProfileInfo?> GetByResourceIdAsync(Guid resourceId, CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        var profile = await db.QuerySingleOrDefaultAsync(
            $"{SelectSqlBody} WHERE p.id = @resourceId",
            p => p.AddWithValue("resourceId", resourceId), Map, ct);
        return profile is null ? null : Dec(profile);
    }

    public async Task<List<PersonProfileInfo>> GetByResourceIdsAsync(IReadOnlyList<Guid> resourceIds, CancellationToken ct = default)
    {
        if (resourceIds.Count == 0) return [];
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        var profiles = await db.QueryListAsync(
            $"{SelectSqlBody} WHERE p.id = ANY(@resourceIds)",
            p => p.AddWithValue("resourceIds", resourceIds.ToArray()), Map, ct);
        return profiles.Select(Dec).ToList();
    }

    public async Task<PersonProfileInfo?> GetByLinkedUserIdAsync(Guid userId, CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        var profile = await db.QuerySingleOrDefaultAsync(
            $"{SelectSqlBody} WHERE p.linked_user_id = @userId",
            p => p.AddWithValue("userId", userId), Map, ct);
        return profile is null ? null : Dec(profile);
    }

    public async Task<PersonProfileInfo> UpsertAsync(Guid resourceId, UpsertPersonProfileRequest request, CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);

        // A plain UPDATE: the row is the resource, which already exists, so there is no
        // insert-or-conflict case left. linked_user_id is untouched (link/unlink have their own
        // endpoints). No foreign keys are written here any more, so 23503 has nothing to catch.
        await db.ExecuteAsync(@"
            UPDATE resources p SET
                email = @email,
                notes = @notes
            FROM resource_types rt
            WHERE rt.id = p.resource_type_id AND rt.has_directory_profile
              AND p.id = @resourceId",
            p =>
            {
                p.AddWithValue("resourceId", resourceId);
                p.AddNullable("email", request.Email);
                p.AddNullable("notes", encryption.ProtectString(request.Notes, orgContext.OrgId));
            }, ct);

        var saved = await GetByResourceIdAsync(resourceId, ct);
        return saved ?? throw new InvalidOperationException("Upsert failed");
    }

    public async Task<bool> LinkUserAsync(Guid resourceId, Guid userId, CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);

        // First check if the user is already linked to any profile in this tenant
        var existingLink = await GetByLinkedUserIdAsync(userId, ct);
        if (existingLink is not null)
            return false;

        // The guard that was an ON CONFLICT ... WHERE is now a plain predicate: claim the
        // link only if it is unclaimed, or already claimed by this same user (idempotent).
        return await db.ExecuteAsync(@"
            UPDATE resources p SET linked_user_id = @userId
            FROM resource_types rt
            WHERE rt.id = p.resource_type_id AND rt.has_directory_profile
              AND p.id = @resourceId
              AND (p.linked_user_id IS NULL OR p.linked_user_id = @userId)",
            p =>
            {
                p.AddWithValue("resourceId", resourceId);
                p.AddWithValue("userId", userId);
            }, ct) > 0;
    }

    public async Task<bool> UnlinkUserAsync(Guid resourceId, CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        return await db.ExecuteAsync(@"
            UPDATE resources SET linked_user_id = NULL
            WHERE id = @resourceId AND linked_user_id IS NOT NULL",
            p => p.AddWithValue("resourceId", resourceId), ct) > 0;
    }

    private static PersonProfileInfo Map(NpgsqlDataReader reader) => new()
    {
        ResourceId = reader.GetGuid(0),
        Email = reader.IsDBNull(1) ? null : reader.GetString(1),
        LinkedUserId = reader.IsDBNull(2) ? null : reader.GetGuid(2),
        Notes = reader.IsDBNull(3) ? null : reader.GetString(3),
        CreatedAt = reader.GetDateTime(4),
        UpdatedAt = reader.GetDateTime(5),
    };
}
