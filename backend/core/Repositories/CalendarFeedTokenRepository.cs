using Api.Models;
using Api.Services;
using Npgsql;

namespace Api.Repositories;

public interface ICalendarFeedTokenRepository
{
    /// <summary>A user's live (non-revoked) subscriptions, newest first.</summary>
    Task<List<CalendarFeedTokenInfo>> GetByUserAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Stores a new subscription. The caller keeps the plaintext token; only its hash lands here.</summary>
    Task<CalendarFeedTokenInfo> CreateAsync(Guid userId, string tokenHash, string? label, Guid? siteId, CancellationToken ct = default);

    /// <summary>Resolves a presented token. Returns null when unknown or revoked — the caller must not distinguish the two.</summary>
    Task<CalendarFeedTokenInfo?> FindActiveByHashAsync(string tokenHash, CancellationToken ct = default);

    /// <summary>Records that a client polled, so an unused subscription is recognisable as such.</summary>
    Task TouchAsync(Guid id, CancellationToken ct = default);

    /// <summary>Revokes one of the user's own subscriptions. Returns false when it is not theirs.</summary>
    Task<bool> RevokeAsync(Guid id, Guid userId, CancellationToken ct = default);
}

public class CalendarFeedTokenRepository(OrgContext orgContext, IOrgDbConnectionFactory connectionFactory)
    : ICalendarFeedTokenRepository
{
    private const string SelectColumns =
        "id, user_id, label, site_id, created_at, last_used_at, revoked_at";

    public async Task<List<CalendarFeedTokenInfo>> GetByUserAsync(Guid userId, CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        return await db.QueryListAsync(
            $@"SELECT {SelectColumns} FROM calendar_feed_tokens
               WHERE user_id = @userId AND revoked_at IS NULL
               ORDER BY created_at DESC",
            p => p.AddWithValue("userId", userId), Map, ct);
    }

    public async Task<CalendarFeedTokenInfo> CreateAsync(
        Guid userId, string tokenHash, string? label, Guid? siteId, CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        return (await db.QuerySingleOrDefaultAsync(
            $@"INSERT INTO calendar_feed_tokens (user_id, token_hash, label, site_id)
               VALUES (@userId, @tokenHash, @label, @siteId)
               RETURNING {SelectColumns}",
            p =>
            {
                p.AddWithValue("userId", userId);
                p.AddWithValue("tokenHash", tokenHash);
                p.AddNullable("label", label);
                p.AddNullable("siteId", siteId);
            }, Map, ct))!;
    }

    public async Task<CalendarFeedTokenInfo?> FindActiveByHashAsync(string tokenHash, CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        return await db.QuerySingleOrDefaultAsync(
            $@"SELECT {SelectColumns} FROM calendar_feed_tokens
               WHERE token_hash = @tokenHash AND revoked_at IS NULL",
            p => p.AddWithValue("tokenHash", tokenHash), Map, ct);
    }

    public async Task TouchAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        await db.ExecuteAsync(
            "UPDATE calendar_feed_tokens SET last_used_at = NOW() WHERE id = @id",
            p => p.AddWithValue("id", id), ct);
    }

    public async Task<bool> RevokeAsync(Guid id, Guid userId, CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        // The user_id predicate is the authorization: one user cannot revoke another's feed.
        var affected = await db.ExecuteAsync(
            @"UPDATE calendar_feed_tokens SET revoked_at = NOW()
              WHERE id = @id AND user_id = @userId AND revoked_at IS NULL",
            p =>
            {
                p.AddWithValue("id", id);
                p.AddWithValue("userId", userId);
            }, ct);
        return affected > 0;
    }

    private static CalendarFeedTokenInfo Map(NpgsqlDataReader r) => new()
    {
        Id = r.GetGuid(r.GetOrdinal("id")),
        UserId = r.GetGuid(r.GetOrdinal("user_id")),
        Label = r.IsDBNull(r.GetOrdinal("label")) ? null : r.GetString(r.GetOrdinal("label")),
        SiteId = r.IsDBNull(r.GetOrdinal("site_id")) ? null : r.GetGuid(r.GetOrdinal("site_id")),
        CreatedAt = r.GetDateTime(r.GetOrdinal("created_at")),
        LastUsedAt = r.IsDBNull(r.GetOrdinal("last_used_at")) ? null : r.GetDateTime(r.GetOrdinal("last_used_at")),
        RevokedAt = r.IsDBNull(r.GetOrdinal("revoked_at")) ? null : r.GetDateTime(r.GetOrdinal("revoked_at")),
    };
}
