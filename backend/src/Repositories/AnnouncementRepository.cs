using Api.Models;
using Api.Services;
using Npgsql;

namespace Api.Repositories;

public interface IAnnouncementRepository
{
    Task<List<AnnouncementDto>> GetAllAsync(bool includeExpired = false);
    Task<AnnouncementDto?> GetByIdAsync(Guid id);
    Task<AnnouncementDto> CreateAsync(Announcement announcement);
    Task<AnnouncementDto?> UpdateAsync(Guid id, string title, string body, bool isImportant, DateTime? expiresAt, Guid updatedByUserId);
    Task<bool> DeleteAsync(Guid id);
    Task<List<UserAnnouncementDto>> GetActiveForUserAsync(Guid userId);
    Task<int> GetUnreadCountAsync(Guid userId);
    Task MarkReadAsync(Guid announcementId, Guid userId);
}

public class AnnouncementRepository : IAnnouncementRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public AnnouncementRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    private const string SelectColumns = @"
        a.id, a.title, a.body, a.is_important, a.revision,
        a.created_at, a.updated_at, a.expires_at,
        cu.email AS created_by_email,
        uu.email AS updated_by_email";

    private const string FromJoins = @"
        FROM announcements a
        LEFT JOIN users cu ON cu.id = a.created_by_user_id
        LEFT JOIN users uu ON uu.id = a.updated_by_user_id";

    public async Task<List<AnnouncementDto>> GetAllAsync(bool includeExpired = false)
    {
        await using var conn = _connectionFactory.CreateControlPlaneConnection();
        await conn.OpenAsync();

        var sql = $"SELECT {SelectColumns} {FromJoins} "
            + (includeExpired ? "" : "WHERE a.expires_at > now() ")
            + "ORDER BY a.created_at DESC LIMIT 500";

        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync();

        var results = new List<AnnouncementDto>();
        while (await reader.ReadAsync()) results.Add(MapDto(reader));
        return results;
    }

    public async Task<AnnouncementDto?> GetByIdAsync(Guid id)
    {
        await using var conn = _connectionFactory.CreateControlPlaneConnection();
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            $"SELECT {SelectColumns} {FromJoins} WHERE a.id = @id", conn);
        cmd.Parameters.AddWithValue("id", id);
        await using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? MapDto(reader) : null;
    }

    public async Task<AnnouncementDto> CreateAsync(Announcement announcement)
    {
        await using var conn = _connectionFactory.CreateControlPlaneConnection();
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(@"
            WITH inserted AS (
                INSERT INTO announcements (id, title, body, is_important, revision,
                                           created_by_user_id, updated_by_user_id, expires_at)
                VALUES (@id, @title, @body, @isImportant, 1, @userId, @userId, @expiresAt)
                RETURNING *
            )
            SELECT i.id, i.title, i.body, i.is_important, i.revision,
                   i.created_at, i.updated_at, i.expires_at,
                   cu.email AS created_by_email, uu.email AS updated_by_email
            FROM inserted i
            LEFT JOIN users cu ON cu.id = i.created_by_user_id
            LEFT JOIN users uu ON uu.id = i.updated_by_user_id", conn);

        cmd.Parameters.AddWithValue("id", announcement.Id);
        cmd.Parameters.AddWithValue("title", announcement.Title);
        cmd.Parameters.AddWithValue("body", announcement.Body);
        cmd.Parameters.AddWithValue("isImportant", announcement.IsImportant);
        cmd.Parameters.AddWithValue("userId", announcement.CreatedByUserId);
        cmd.Parameters.AddWithValue("expiresAt", announcement.ExpiresAt);

        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        return MapDto(reader);
    }

    public async Task<AnnouncementDto?> UpdateAsync(Guid id, string title, string body, bool isImportant, DateTime? expiresAt, Guid updatedByUserId)
    {
        await using var conn = _connectionFactory.CreateControlPlaneConnection();
        await conn.OpenAsync();

        var sql = @"
            WITH updated AS (
                UPDATE announcements
                SET title = @title, body = @body, is_important = @isImportant,
                    revision = revision + 1, updated_by_user_id = @userId"
            + (expiresAt.HasValue ? ", expires_at = @expiresAt" : "") + @"
                WHERE id = @id
                RETURNING *
            )
            SELECT u.id, u.title, u.body, u.is_important, u.revision,
                   u.created_at, u.updated_at, u.expires_at,
                   cu.email AS created_by_email, uu.email AS updated_by_email
            FROM updated u
            LEFT JOIN users cu ON cu.id = u.created_by_user_id
            LEFT JOIN users uu ON uu.id = u.updated_by_user_id";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("title", title);
        cmd.Parameters.AddWithValue("body", body);
        cmd.Parameters.AddWithValue("isImportant", isImportant);
        cmd.Parameters.AddWithValue("userId", updatedByUserId);
        if (expiresAt.HasValue) cmd.Parameters.AddWithValue("expiresAt", expiresAt.Value);

        await using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? MapDto(reader) : null;
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        await using var conn = _connectionFactory.CreateControlPlaneConnection();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("DELETE FROM announcements WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", id);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    public async Task<List<UserAnnouncementDto>> GetActiveForUserAsync(Guid userId)
    {
        await using var conn = _connectionFactory.CreateControlPlaneConnection();
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(@"
            SELECT a.id, a.title, a.body, a.is_important, a.revision,
                   a.created_at, a.updated_at,
                   COALESCE(ar.read_revision >= a.revision, FALSE) AS is_read
            FROM announcements a
            LEFT JOIN announcement_reads ar ON ar.announcement_id = a.id AND ar.user_id = @userId
            WHERE a.expires_at > now()
            ORDER BY a.created_at DESC", conn);

        cmd.Parameters.AddWithValue("userId", userId);
        await using var reader = await cmd.ExecuteReaderAsync();

        var results = new List<UserAnnouncementDto>();
        while (await reader.ReadAsync())
        {
            results.Add(new UserAnnouncementDto
            {
                Id = reader.GetGuid(reader.GetOrdinal("id")),
                Title = reader.GetString(reader.GetOrdinal("title")),
                Body = reader.GetString(reader.GetOrdinal("body")),
                IsImportant = reader.GetBoolean(reader.GetOrdinal("is_important")),
                CreatedAt = reader.GetDateTime(reader.GetOrdinal("created_at")),
                UpdatedAt = reader.GetDateTime(reader.GetOrdinal("updated_at")),
                IsRead = reader.GetBoolean(reader.GetOrdinal("is_read")),
            });
        }
        return results;
    }

    public async Task<int> GetUnreadCountAsync(Guid userId)
    {
        await using var conn = _connectionFactory.CreateControlPlaneConnection();
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(@"
            SELECT COUNT(*)::int
            FROM announcements a
            LEFT JOIN announcement_reads ar ON ar.announcement_id = a.id AND ar.user_id = @userId
            WHERE a.expires_at > now() AND COALESCE(ar.read_revision < a.revision, TRUE)", conn);

        cmd.Parameters.AddWithValue("userId", userId);
        var result = await cmd.ExecuteScalarAsync();
        return result is int count ? count : 0;
    }

    public async Task MarkReadAsync(Guid announcementId, Guid userId)
    {
        await using var conn = _connectionFactory.CreateControlPlaneConnection();
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(@"
            INSERT INTO announcement_reads (announcement_id, user_id, read_revision, read_at)
            SELECT @announcementId, @userId, a.revision, now()
            FROM announcements a WHERE a.id = @announcementId
            ON CONFLICT (announcement_id, user_id)
            DO UPDATE SET read_revision = EXCLUDED.read_revision, read_at = now()", conn);

        cmd.Parameters.AddWithValue("announcementId", announcementId);
        cmd.Parameters.AddWithValue("userId", userId);
        await cmd.ExecuteNonQueryAsync();
    }

    private static AnnouncementDto MapDto(NpgsqlDataReader reader) => new()
    {
        Id = reader.GetGuid(reader.GetOrdinal("id")),
        Title = reader.GetString(reader.GetOrdinal("title")),
        Body = reader.GetString(reader.GetOrdinal("body")),
        IsImportant = reader.GetBoolean(reader.GetOrdinal("is_important")),
        Revision = reader.GetInt32(reader.GetOrdinal("revision")),
        CreatedAt = reader.GetDateTime(reader.GetOrdinal("created_at")),
        UpdatedAt = reader.GetDateTime(reader.GetOrdinal("updated_at")),
        ExpiresAt = reader.GetDateTime(reader.GetOrdinal("expires_at")),
        CreatedByEmail = reader.IsDBNull(reader.GetOrdinal("created_by_email")) ? null : reader.GetString(reader.GetOrdinal("created_by_email")),
        UpdatedByEmail = reader.IsDBNull(reader.GetOrdinal("updated_by_email")) ? null : reader.GetString(reader.GetOrdinal("updated_by_email")),
    };
}
