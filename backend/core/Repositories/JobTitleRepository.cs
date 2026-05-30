using Api.Helpers;
using Api.Models;
using Api.Services;
using Npgsql;

namespace Api.Repositories;

public class JobTitleRepository(OrgContext orgContext, IOrgDbConnectionFactory connectionFactory)
    : IJobTitleRepository
{
    private const string SelectColumns =
        "id, name, description, is_active, created_at, updated_at";

    public async Task<List<JobTitleInfo>> GetAllAsync(bool includeInactive = false, CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);

        var sql = includeInactive
            ? $"SELECT {SelectColumns} FROM job_titles ORDER BY name"
            : $"SELECT {SelectColumns} FROM job_titles WHERE is_active ORDER BY name";

        return await db.QueryListAsync(sql, null, Map, ct);
    }

    public async Task<JobTitleInfo?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        return await db.QuerySingleOrDefaultAsync(
            $"SELECT {SelectColumns} FROM job_titles WHERE id = @id",
            p => p.AddWithValue("id", id), Map, ct);
    }

    public async Task<JobTitleInfo> CreateAsync(CreateJobTitleRequest request, CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);

        // INSERT … ON CONFLICT DO NOTHING — single round-trip; catches the
        // unique(name) violation as a "no row returned" outcome rather than an
        // exception, matching the pattern in CriteriaRepository.
        var created = await db.QuerySingleOrDefaultAsync(
            $@"INSERT INTO job_titles (name, description)
               VALUES (@name, @description)
               ON CONFLICT (name) DO NOTHING
               RETURNING {SelectColumns}",
            p =>
            {
                p.AddWithValue("name", request.Name);
                p.AddNullable("description", request.Description);
            }, Map, ct);

        return created ?? throw new ConflictException("A job title with this name already exists");
    }

    public async Task<JobTitleInfo?> UpdateAsync(Guid id, UpdateJobTitleRequest request, CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);

        var update = new UpdateBuilder();
        update.SetIfNotNull("name", request.Name);
        update.SetIfNotNull("description", request.Description);
        if (request.IsActive is not null)
            update.Set("is_active", request.IsActive.Value);

        if (update.IsEmpty) return await GetByIdAsync(id, ct);

        return await db.QuerySingleOrDefaultAsync(
            $"UPDATE job_titles SET {update.SetClause} WHERE id = @id RETURNING {SelectColumns}",
            p =>
            {
                p.AddWithValue("id", id);
                update.Apply(p);
            }, Map, ct);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        return await db.ExecuteAsync("DELETE FROM job_titles WHERE id = @id",
            p => p.AddWithValue("id", id), ct) > 0;
    }

    private static JobTitleInfo Map(NpgsqlDataReader reader) => new()
    {
        Id = reader.GetGuid(0),
        Name = reader.GetString(1),
        Description = reader.IsDBNull(2) ? null : reader.GetString(2),
        IsActive = reader.GetBoolean(3),
        CreatedAt = reader.GetDateTime(4),
        UpdatedAt = reader.GetDateTime(5),
    };
}
