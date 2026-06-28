using Api.Models;
using Api.Services;
using Npgsql;

namespace Api.Repositories;

public interface IAnnouncementRepository
{
    Task<List<AnnouncementDto>> GetAllAsync(bool includeExpired = false, CancellationToken ct = default);
    Task<AnnouncementDto?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<AnnouncementDto> CreateAsync(Announcement announcement, CancellationToken ct = default);
    Task<AnnouncementDto?> UpdateAsync(Guid id, string title, string body, bool isImportant, DateTime? expiresAt, Guid updatedByUserId, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
    Task<List<UserAnnouncementDto>> GetActiveForUserAsync(Guid userId, CancellationToken ct = default);
    Task<int> GetUnreadCountAsync(Guid userId, CancellationToken ct = default);
    Task MarkReadAsync(Guid announcementId, Guid userId, CancellationToken ct = default);

    /// <summary>Active announcements with the 'email' channel whose broadcast hasn't run yet.</summary>
    Task<List<AnnouncementDto>> GetPendingEmailBroadcastsAsync(CancellationToken ct = default);
    /// <summary>Marks an announcement's email broadcast as completed (sets email_sent_at).</summary>
    Task MarkEmailSentAsync(Guid announcementId, CancellationToken ct = default);
}

public class AnnouncementRepository : IAnnouncementRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public AnnouncementRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    private const string SelectColumns = @"
        a.id, a.title, a.body, a.is_important, a.channels, a.revision,
        a.created_at, a.updated_at, a.expires_at,
        cu.email AS created_by_email,
        uu.email AS updated_by_email";

    private const string FromJoins = @"
        FROM announcements a
        LEFT JOIN users cu ON cu.id = a.created_by_user_id
        LEFT JOIN users uu ON uu.id = a.updated_by_user_id";

    public async Task<List<AnnouncementDto>> GetAllAsync(bool includeExpired = false, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateControlPlaneConnection();

        var sql = $"SELECT {SelectColumns} {FromJoins} "
            + (includeExpired ? "" : "WHERE a.expires_at > now() ")
            + "ORDER BY a.created_at DESC LIMIT 500";

        return await conn.QueryListAsync(sql, null, MapDto, ct);
    }

    public async Task<AnnouncementDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateControlPlaneConnection();
        return await conn.QuerySingleOrDefaultAsync(
            $"SELECT {SelectColumns} {FromJoins} WHERE a.id = @id",
            p => p.AddWithValue("id", id), MapDto, ct);
    }

    public async Task<AnnouncementDto> CreateAsync(Announcement announcement, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateControlPlaneConnection();

        // INSERT … RETURNING always yields a row on success.
        return (await conn.QuerySingleOrDefaultAsync(@"
            WITH inserted AS (
                INSERT INTO announcements (id, title, body, is_important, channels, revision,
                                           created_by_user_id, updated_by_user_id, expires_at)
                VALUES (@id, @title, @body, @isImportant, @channels, 1, @userId, @userId, @expiresAt)
                RETURNING *
            )
            SELECT i.id, i.title, i.body, i.is_important, i.channels, i.revision,
                   i.created_at, i.updated_at, i.expires_at,
                   cu.email AS created_by_email, uu.email AS updated_by_email
            FROM inserted i
            LEFT JOIN users cu ON cu.id = i.created_by_user_id
            LEFT JOIN users uu ON uu.id = i.updated_by_user_id",
            p =>
            {
                p.AddWithValue("id", announcement.Id);
                p.AddWithValue("title", announcement.Title);
                p.AddWithValue("body", announcement.Body);
                p.AddWithValue("isImportant", announcement.IsImportant);
                p.AddWithValue("channels", announcement.Channels);
                p.AddWithValue("userId", announcement.CreatedByUserId);
                p.AddWithValue("expiresAt", announcement.ExpiresAt);
            }, MapDto, ct))!;
    }

    public async Task<AnnouncementDto?> UpdateAsync(Guid id, string title, string body, bool isImportant, DateTime? expiresAt, Guid updatedByUserId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateControlPlaneConnection();

        var sql = @"
            WITH updated AS (
                UPDATE announcements
                SET title = @title, body = @body, is_important = @isImportant,
                    revision = revision + 1, updated_by_user_id = @userId"
            + (expiresAt.HasValue ? ", expires_at = @expiresAt" : "") + @"
                WHERE id = @id
                RETURNING *
            )
            SELECT u.id, u.title, u.body, u.is_important, u.channels, u.revision,
                   u.created_at, u.updated_at, u.expires_at,
                   cu.email AS created_by_email, uu.email AS updated_by_email
            FROM updated u
            LEFT JOIN users cu ON cu.id = u.created_by_user_id
            LEFT JOIN users uu ON uu.id = u.updated_by_user_id";

        return await conn.QuerySingleOrDefaultAsync(sql, p =>
        {
            p.AddWithValue("id", id);
            p.AddWithValue("title", title);
            p.AddWithValue("body", body);
            p.AddWithValue("isImportant", isImportant);
            p.AddWithValue("userId", updatedByUserId);
            if (expiresAt.HasValue) p.AddWithValue("expiresAt", expiresAt.Value);
        }, MapDto, ct);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateControlPlaneConnection();
        return await conn.ExecuteAsync("DELETE FROM announcements WHERE id = @id",
            p => p.AddWithValue("id", id), ct) > 0;
    }

    public async Task<List<UserAnnouncementDto>> GetActiveForUserAsync(Guid userId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateControlPlaneConnection();
        return await conn.QueryListAsync(@"
            SELECT a.id, a.title, a.body, a.is_important, a.revision,
                   a.created_at, a.updated_at,
                   COALESCE(ar.read_revision >= a.revision, FALSE) AS is_read
            FROM announcements a
            LEFT JOIN announcement_reads ar ON ar.announcement_id = a.id AND ar.user_id = @userId
            WHERE a.expires_at > now() AND 'site' = ANY(a.channels)
            ORDER BY a.created_at DESC",
            p => p.AddWithValue("userId", userId), MapUserAnnouncement, ct);
    }

    public async Task<int> GetUnreadCountAsync(Guid userId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateControlPlaneConnection();
        return await conn.ExecuteScalarAsync<int>(@"
            SELECT COUNT(*)::int
            FROM announcements a
            LEFT JOIN announcement_reads ar ON ar.announcement_id = a.id AND ar.user_id = @userId
            WHERE a.expires_at > now() AND 'site' = ANY(a.channels)
              AND COALESCE(ar.read_revision < a.revision, TRUE)",
            p => p.AddWithValue("userId", userId), ct);
    }

    public async Task MarkReadAsync(Guid announcementId, Guid userId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateControlPlaneConnection();
        await conn.ExecuteAsync(@"
            INSERT INTO announcement_reads (announcement_id, user_id, read_revision, read_at)
            SELECT @announcementId, @userId, a.revision, now()
            FROM announcements a WHERE a.id = @announcementId
            ON CONFLICT (announcement_id, user_id)
            DO UPDATE SET read_revision = EXCLUDED.read_revision, read_at = now()",
            p =>
            {
                p.AddWithValue("announcementId", announcementId);
                p.AddWithValue("userId", userId);
            }, ct);
    }

    public async Task<List<AnnouncementDto>> GetPendingEmailBroadcastsAsync(CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateControlPlaneConnection();
        return await conn.QueryListAsync(
            $"SELECT {SelectColumns} {FromJoins} "
            + "WHERE 'email' = ANY(a.channels) AND a.email_sent_at IS NULL AND a.expires_at > now() "
            + "ORDER BY a.created_at",
            null, MapDto, ct);
    }

    public async Task MarkEmailSentAsync(Guid announcementId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateControlPlaneConnection();
        await conn.ExecuteAsync(
            "UPDATE announcements SET email_sent_at = now() WHERE id = @id",
            p => p.AddWithValue("id", announcementId), ct);
    }

    private static UserAnnouncementDto MapUserAnnouncement(NpgsqlDataReader reader) => new()
    {
        Id = reader.GetGuid(reader.GetOrdinal("id")),
        Title = reader.GetString(reader.GetOrdinal("title")),
        Body = reader.GetString(reader.GetOrdinal("body")),
        IsImportant = reader.GetBoolean(reader.GetOrdinal("is_important")),
        CreatedAt = reader.GetDateTime(reader.GetOrdinal("created_at")),
        UpdatedAt = reader.GetDateTime(reader.GetOrdinal("updated_at")),
        IsRead = reader.GetBoolean(reader.GetOrdinal("is_read")),
    };

    private static AnnouncementDto MapDto(NpgsqlDataReader reader) => new()
    {
        Id = reader.GetGuid(reader.GetOrdinal("id")),
        Title = reader.GetString(reader.GetOrdinal("title")),
        Body = reader.GetString(reader.GetOrdinal("body")),
        IsImportant = reader.GetBoolean(reader.GetOrdinal("is_important")),
        Channels = reader.GetFieldValue<string[]>(reader.GetOrdinal("channels")),
        Revision = reader.GetInt32(reader.GetOrdinal("revision")),
        CreatedAt = reader.GetDateTime(reader.GetOrdinal("created_at")),
        UpdatedAt = reader.GetDateTime(reader.GetOrdinal("updated_at")),
        ExpiresAt = reader.GetDateTime(reader.GetOrdinal("expires_at")),
        CreatedByEmail = reader.IsDBNull(reader.GetOrdinal("created_by_email")) ? null : reader.GetString(reader.GetOrdinal("created_by_email")),
        UpdatedByEmail = reader.IsDBNull(reader.GetOrdinal("updated_by_email")) ? null : reader.GetString(reader.GetOrdinal("updated_by_email")),
    };
}
