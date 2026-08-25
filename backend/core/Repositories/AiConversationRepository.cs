using Api.Helpers;
using Api.Services;

namespace Api.Repositories;

/// <summary>One saved conversation, without its bodies — what the list needs and no more.</summary>
public sealed record AiConversationSummary
{
    public required Guid Id { get; init; }
    public required string Title { get; init; }
    public required DateTime UpdatedAt { get; init; }
}

/// <summary>A saved conversation in full. The two JSON blobs are opaque to the server.</summary>
public sealed record AiConversationRow
{
    public required Guid Id { get; init; }
    public required string Title { get; init; }
    /// <summary>What the panel renders.</summary>
    public required System.Text.Json.JsonElement Entries { get; init; }
    /// <summary>What the model reads on the next turn.</summary>
    public required System.Text.Json.JsonElement Transcript { get; init; }
    public required DateTime UpdatedAt { get; init; }
}

/// <summary>
/// Saved conversations, always scoped to one person.
///
/// Every method takes the owner and filters on it. A transcript quotes workspace data the
/// reader may not otherwise be entitled to, and it is somebody's working notes either way,
/// so "belongs to someone else" is treated exactly like "does not exist".
/// </summary>
public interface IAiConversationRepository
{
    Task<IReadOnlyList<AiConversationSummary>> ListAsync(Guid userId, CancellationToken ct = default);

    Task<AiConversationRow?> GetAsync(Guid userId, Guid id, CancellationToken ct = default);

    /// <summary>
    /// Writes the conversation. Returns false when the id belongs to someone else — the
    /// guard rejects the row silently at the SQL level, so the caller has to be told.
    /// </summary>
    Task<bool> UpsertAsync(Guid userId, Guid id, string title, string entriesJson, string transcriptJson,
        CancellationToken ct = default);

    Task<bool> DeleteAsync(Guid userId, Guid id, CancellationToken ct = default);

    /// <summary>
    /// Deletes this person's conversations beyond the newest <paramref name="keep"/>.
    /// Returns how many went. Called on write, so the cap is enforced by the path that
    /// creates the pressure rather than by a job that can fail unnoticed.
    /// </summary>
    Task<int> TrimAsync(Guid userId, int keep, CancellationToken ct = default);
}

public sealed class AiConversationRepository(OrgContext orgContext, IOrgDbConnectionFactory connectionFactory)
    : IAiConversationRepository
{
    public async Task<IReadOnlyList<AiConversationSummary>> ListAsync(Guid userId, CancellationToken ct = default)
    {
        // Bodies are deliberately absent: a list of twenty transcripts would be megabytes
        // to render a column of titles.
        await using var conn = connectionFactory.CreateOrgConnection(orgContext);
        return await conn.QueryListAsync(
            @"SELECT id, title, updated_at
              FROM ai_conversations
              WHERE user_id = @userId
              ORDER BY updated_at DESC",
            p => p.AddWithValue("userId", userId),
            r => new AiConversationSummary
            {
                Id = r.GetGuid(0),
                Title = r.GetString(1),
                UpdatedAt = r.GetDateTime(2),
            }, ct);
    }

    public async Task<AiConversationRow?> GetAsync(Guid userId, Guid id, CancellationToken ct = default)
    {
        await using var conn = connectionFactory.CreateOrgConnection(orgContext);
        return await conn.QuerySingleOrDefaultAsync(
            @"SELECT id, title, entries, transcript, updated_at
              FROM ai_conversations
              WHERE user_id = @userId AND id = @id",
            p =>
            {
                p.AddWithValue("userId", userId);
                p.AddWithValue("id", id);
            },
            r => new AiConversationRow
            {
                Id = r.GetGuid(0),
                Title = r.GetString(1),
                // GetJsonElement clones off the rented document, so nothing is pinned and
                // the buffer goes back to the pool — see ReaderExtensions.
                Entries = r.GetJsonElement(2),
                Transcript = r.GetJsonElement(3),
                UpdatedAt = r.GetDateTime(4),
            }, ct);
    }

    public async Task<bool> UpsertAsync(Guid userId, Guid id, string title, string entriesJson, string transcriptJson,
        CancellationToken ct = default)
    {
        // The whole conversation is rewritten each turn. It is bounded by the transcript
        // caps, so this stays small, and a whole-blob write cannot leave a half-applied
        // conversation the way an append of deltas could.
        //
        // The user_id in the WHERE is what stops one person overwriting another's row by
        // guessing an id: a conflicting id that belongs to someone else updates nothing.
        await using var conn = connectionFactory.CreateOrgConnection(orgContext);
        var affected = await conn.ExecuteAsync(
            @"INSERT INTO ai_conversations (id, user_id, title, entries, transcript)
              VALUES (@id, @userId, @title, @entries::jsonb, @transcript::jsonb)
              ON CONFLICT (id) DO UPDATE
                 SET title      = EXCLUDED.title,
                     entries    = EXCLUDED.entries,
                     transcript = EXCLUDED.transcript,
                     updated_at = NOW()
               WHERE ai_conversations.user_id = @userId",
            p =>
            {
                p.AddWithValue("id", id);
                p.AddWithValue("userId", userId);
                p.AddWithValue("title", title);
                p.AddWithValue("entries", entriesJson);
                p.AddWithValue("transcript", transcriptJson);
            }, ct);

        // Zero means the WHERE guard refused: the id exists and is not this person's.
        // Postgres raises nothing for that, so silence here would read as success.
        return affected > 0;
    }

    public async Task<bool> DeleteAsync(Guid userId, Guid id, CancellationToken ct = default)
    {
        await using var conn = connectionFactory.CreateOrgConnection(orgContext);
        var affected = await conn.ExecuteAsync(
            "DELETE FROM ai_conversations WHERE user_id = @userId AND id = @id",
            p =>
            {
                p.AddWithValue("userId", userId);
                p.AddWithValue("id", id);
            }, ct);
        return affected > 0;
    }

    public async Task<int> TrimAsync(Guid userId, int keep, CancellationToken ct = default)
    {
        await using var conn = connectionFactory.CreateOrgConnection(orgContext);
        return await conn.ExecuteAsync(
            @"DELETE FROM ai_conversations
              WHERE user_id = @userId
                AND id NOT IN (
                    SELECT id FROM ai_conversations
                    WHERE user_id = @userId
                    -- id breaks ties: two rows saved in the same microsecond would
                    -- otherwise make which one survives arbitrary.
                    ORDER BY updated_at DESC, id DESC
                    LIMIT @keep
                )",
            p =>
            {
                p.AddWithValue("userId", userId);
                p.AddWithValue("keep", keep);
            }, ct);
    }
}
