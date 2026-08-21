using Api.Models;
using Api.Services;

namespace Api.Repositories;

/// <summary>One user's stored allowance row. Absence of a row is itself meaningful — it means no access.</summary>
public sealed record AiAllowanceRow
{
    public Guid UserId { get; init; }
    public long? MonthlyTokenLimit { get; init; }
}

/// <summary>One user's token spend within a single calendar month.</summary>
public sealed record AiUsageRow
{
    public Guid UserId { get; init; }
    public long InputTokens { get; init; }
    public long OutputTokens { get; init; }
    public int Turns { get; init; }

    public long TotalTokens => InputTokens + OutputTokens;
}

public interface IAiAllowanceRepository
{
    Task<AiAllowanceRow?> GetAllowanceAsync(Guid userId, CancellationToken ct = default);
    Task UpsertAllowanceAsync(Guid userId, long? monthlyTokenLimit, Guid? actorUserId, CancellationToken ct = default);
    Task<bool> DeleteAllowanceAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Every workspace member with their grant and this month's spend — the admin table in
    /// one query. Members with no grant come back with <c>Granted = false</c> so the admin
    /// sees who still has no access.
    /// </summary>
    Task<IReadOnlyList<AiUserAllowance>> ListMemberAllowancesAsync(DateOnly month, CancellationToken ct = default);

    Task<AiUsageRow?> GetUsageAsync(Guid userId, DateOnly month, CancellationToken ct = default);
    Task RecordUsageAsync(Guid userId, DateOnly month, long inputTokens, long outputTokens, CancellationToken ct = default);
}

/// <summary>
/// Per-user assistant grants and monthly token spend, in the workspace's own database.
/// The <c>month</c> key is the reset mechanism: a new calendar month simply lands on a
/// new row, so nothing has to run on a schedule and nothing can fail to reset.
/// </summary>
public sealed class AiAllowanceRepository(OrgContext orgContext, IOrgDbConnectionFactory connectionFactory)
    : IAiAllowanceRepository
{
    public async Task<AiAllowanceRow?> GetAllowanceAsync(Guid userId, CancellationToken ct = default)
    {
        await using var conn = connectionFactory.CreateOrgConnection(orgContext);
        return await conn.QuerySingleOrDefaultAsync(
            "SELECT user_id, monthly_token_limit FROM ai_user_allowances WHERE user_id = @userId",
            p => p.AddWithValue("userId", userId),
            r => new AiAllowanceRow
            {
                UserId = r.GetGuid(0),
                MonthlyTokenLimit = r.IsDBNull(1) ? null : r.GetInt64(1),
            }, ct);
    }

    public async Task<IReadOnlyList<AiUserAllowance>> ListMemberAllowancesAsync(DateOnly month, CancellationToken ct = default)
    {
        // The tenant database carries its own `users` mirror, so members, grants, and
        // spend all join locally — no cross-service call and no second round-trip.
        await using var conn = connectionFactory.CreateOrgConnection(orgContext);
        return await conn.QueryListAsync(@"
            SELECT u.id, u.display_name, u.email,
                   a.user_id IS NOT NULL AS granted,
                   a.monthly_token_limit,
                   COALESCE(g.input_tokens, 0), COALESCE(g.output_tokens, 0), COALESCE(g.turns, 0)
            FROM users u
            LEFT JOIN ai_user_allowances a ON a.user_id = u.id
            LEFT JOIN ai_usage g ON g.user_id = u.id AND g.month = @month
            ORDER BY LOWER(COALESCE(u.display_name, u.email))",
            p => p.AddWithValue("month", month),
            r => new AiUserAllowance
            {
                UserId = r.GetGuid(0),
                DisplayName = r.IsDBNull(1) ? null : r.GetString(1),
                Email = r.IsDBNull(2) ? null : r.GetString(2),
                Granted = r.GetBoolean(3),
                MonthlyTokenLimit = r.IsDBNull(4) ? null : r.GetInt64(4),
                UsedInputTokens = r.GetInt64(5),
                UsedOutputTokens = r.GetInt64(6),
                UsedTurns = r.GetInt32(7),
            }, ct);
    }

    public async Task UpsertAllowanceAsync(Guid userId, long? monthlyTokenLimit, Guid? actorUserId, CancellationToken ct = default)
    {
        await using var conn = connectionFactory.CreateOrgConnection(orgContext);
        await conn.ExecuteAsync(@"
            INSERT INTO ai_user_allowances (user_id, monthly_token_limit, updated_at, updated_by_user_id)
            VALUES (@userId, @limit, NOW(), @actor)
            ON CONFLICT (user_id) DO UPDATE SET
                monthly_token_limit = @limit,
                updated_at          = NOW(),
                updated_by_user_id  = @actor",
            p =>
            {
                p.AddWithValue("userId", userId);
                p.AddWithValue("limit", monthlyTokenLimit.HasValue ? monthlyTokenLimit.Value : DBNull.Value);
                p.AddWithValue("actor", actorUserId.HasValue ? actorUserId.Value : DBNull.Value);
            }, ct);
    }

    public async Task<bool> DeleteAllowanceAsync(Guid userId, CancellationToken ct = default)
    {
        await using var conn = connectionFactory.CreateOrgConnection(orgContext);
        var rows = await conn.ExecuteAsync(
            "DELETE FROM ai_user_allowances WHERE user_id = @userId",
            p => p.AddWithValue("userId", userId), ct);
        return rows > 0;
    }

    public async Task<AiUsageRow?> GetUsageAsync(Guid userId, DateOnly month, CancellationToken ct = default)
    {
        await using var conn = connectionFactory.CreateOrgConnection(orgContext);
        return await conn.QuerySingleOrDefaultAsync(@"
            SELECT user_id, input_tokens, output_tokens, turns
            FROM ai_usage WHERE user_id = @userId AND month = @month",
            p =>
            {
                p.AddWithValue("userId", userId);
                p.AddWithValue("month", month);
            },
            r => new AiUsageRow
            {
                UserId = r.GetGuid(0),
                InputTokens = r.GetInt64(1),
                OutputTokens = r.GetInt64(2),
                Turns = r.GetInt32(3),
            }, ct);
    }

    public async Task RecordUsageAsync(Guid userId, DateOnly month, long inputTokens, long outputTokens, CancellationToken ct = default)
    {
        await using var conn = connectionFactory.CreateOrgConnection(orgContext);
        await conn.ExecuteAsync(@"
            INSERT INTO ai_usage (user_id, month, input_tokens, output_tokens, turns)
            VALUES (@userId, @month, @input, @output, 1)
            ON CONFLICT (user_id, month) DO UPDATE SET
                input_tokens  = ai_usage.input_tokens  + @input,
                output_tokens = ai_usage.output_tokens + @output,
                turns         = ai_usage.turns + 1",
            p =>
            {
                p.AddWithValue("userId", userId);
                p.AddWithValue("month", month);
                p.AddWithValue("input", inputTokens);
                p.AddWithValue("output", outputTokens);
            }, ct);
    }
}
