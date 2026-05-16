using Api.Models;
using Api.Services;
using Npgsql;

namespace Api.Repositories;

public class JobTitleRepository(OrgContext orgContext, IOrgDbConnectionFactory connectionFactory)
    : IJobTitleRepository
{
    private const string SelectColumns =
        "id, name, description, is_active, created_at, updated_at";

    public async Task<List<JobTitleInfo>> GetAllAsync(bool includeInactive = false)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        await db.OpenAsync();

        var sql = includeInactive
            ? $"SELECT {SelectColumns} FROM job_titles ORDER BY name"
            : $"SELECT {SelectColumns} FROM job_titles WHERE is_active ORDER BY name";

        await using var cmd = new NpgsqlCommand(sql, db);
        var rows = new List<JobTitleInfo>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) rows.Add(Map(reader));
        return rows;
    }

    public async Task<JobTitleInfo?> GetByIdAsync(Guid id)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        await db.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            $"SELECT {SelectColumns} FROM job_titles WHERE id = @id", db);
        cmd.Parameters.AddWithValue("id", id);

        await using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? Map(reader) : null;
    }

    public async Task<JobTitleInfo> CreateAsync(CreateJobTitleRequest request)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        await db.OpenAsync();

        // INSERT … ON CONFLICT DO NOTHING — single round-trip; catches the
        // unique(name) violation as a "no row returned" outcome rather than an
        // exception, matching the pattern in CriteriaRepository.
        await using var cmd = new NpgsqlCommand(
            $@"INSERT INTO job_titles (name, description)
               VALUES (@name, @description)
               ON CONFLICT (name) DO NOTHING
               RETURNING {SelectColumns}", db);
        cmd.Parameters.AddWithValue("name", request.Name);
        cmd.Parameters.AddWithValue("description", NullableParam(request.Description));

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            throw new InvalidOperationException("A job title with this name already exists");
        return Map(reader);
    }

    public async Task<JobTitleInfo?> UpdateAsync(Guid id, UpdateJobTitleRequest request)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        await db.OpenAsync();

        var sets = new List<string>();
        await using var cmd = new NpgsqlCommand { Connection = db };
        cmd.Parameters.AddWithValue("id", id);

        if (request.Name is not null)
        {
            sets.Add("name = @name");
            cmd.Parameters.AddWithValue("name", request.Name);
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
            $"UPDATE job_titles SET {string.Join(", ", sets)} WHERE id = @id RETURNING {SelectColumns}";

        await using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? Map(reader) : null;
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        await db.OpenAsync();

        await using var cmd = new NpgsqlCommand("DELETE FROM job_titles WHERE id = @id", db);
        cmd.Parameters.AddWithValue("id", id);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    private static object NullableParam(object? value) => value ?? DBNull.Value;

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
