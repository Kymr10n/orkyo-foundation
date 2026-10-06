using Api.Helpers;
using Api.Services;
using Npgsql;
using Orkyo.Shared;

namespace Api.Repositories;

/// <summary>One mail waiting in the outbox, as a sender needs it.</summary>
/// <param name="Attempts">Delivery attempts made before this one.</param>
public sealed record OutboxMessage(
    Guid Id,
    string ToEmail,
    string ToName,
    string Subject,
    string HtmlBody,
    string TextBody,
    int Attempts);

/// <summary>
/// The mail outbox: every outbound mail is written here, fully rendered, before any
/// delivery attempt, so a crash between "decided to send" and "sent" loses nothing.
/// </summary>
public interface IEmailOutboxRepository
{
    /// <summary>Stores a rendered mail as pending and due now. Returns its id.</summary>
    Task<Guid> EnqueueAsync(string toEmail, string toName, string subject, string htmlBody, string textBody, CancellationToken ct = default);

    /// <summary>
    /// Claims one pending row by id for <see cref="EmailOutboxPolicy.ClaimLease"/>. Null when
    /// the row is not pending or another sender holds it.
    /// </summary>
    Task<OutboxMessage?> ClaimAsync(Guid id, CancellationToken ct = default);

    /// <summary>Claims up to <paramref name="batchSize"/> due pending rows, oldest first.</summary>
    Task<IReadOnlyList<OutboxMessage>> ClaimDueAsync(int batchSize, CancellationToken ct = default);

    /// <summary>Marks the row sent and clears its bodies, which carry live tokens.</summary>
    Task MarkSentAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Records a failed attempt. The row stays pending and becomes due at
    /// <paramref name="nextAttemptAtUtc"/>, or is marked dead when <paramref name="dead"/>.
    /// </summary>
    Task MarkFailedAsync(Guid id, string error, DateTime nextAttemptAtUtc, bool dead, CancellationToken ct = default);

    /// <summary>Deletes sent rows older than <paramref name="sentOlderThanUtc"/> and dead rows
    /// older than <paramref name="deadOlderThanUtc"/>. Returns how many went.</summary>
    Task<int> PruneAsync(DateTime sentOlderThanUtc, DateTime deadOlderThanUtc, CancellationToken ct = default);
}

/// <summary>
/// Control-plane scoped: mail is not tenant data, and the worker that drains the outbox
/// runs outside any tenant. Branding is applied when the mail is rendered, before it is
/// stored, so delivery needs no tenant context.
/// </summary>
public sealed class EmailOutboxRepository : IEmailOutboxRepository
{
    private const string Columns = "id, to_email, to_name, subject, html_body, text_body, attempts";

    private readonly IDbConnectionFactory _connectionFactory;

    public EmailOutboxRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Guid> EnqueueAsync(string toEmail, string toName, string subject, string htmlBody, string textBody, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateControlPlaneConnection();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(@"
            INSERT INTO email_outbox (to_email, to_name, subject, html_body, text_body)
            VALUES (@toEmail, @toName, @subject, @html, @text)
            RETURNING id", conn);
        cmd.Parameters.AddWithValue("toEmail", toEmail);
        cmd.Parameters.AddWithValue("toName", toName);
        cmd.Parameters.AddWithValue("subject", subject);
        cmd.Parameters.AddWithValue("html", htmlBody);
        cmd.Parameters.AddWithValue("text", textBody);
        return (Guid)(await cmd.ExecuteScalarAsync(ct))!;
    }

    public async Task<OutboxMessage?> ClaimAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateControlPlaneConnection();
        await conn.OpenAsync(ct);
        // The lease is the claim: moving next_attempt_at forward hides the row from every
        // other claimer until the sender has reported back or the lease has run out.
        await using var cmd = new NpgsqlCommand($@"
            UPDATE email_outbox
            SET next_attempt_at = now() + @lease
            WHERE id = @id AND status = 'pending' AND next_attempt_at <= now()
            RETURNING {Columns}", conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("lease", EmailOutboxPolicy.ClaimLease);
        var rows = await ReadAsync(cmd, ct);
        return rows.Count == 0 ? null : rows[0];
    }

    public async Task<IReadOnlyList<OutboxMessage>> ClaimDueAsync(int batchSize, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateControlPlaneConnection();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand($@"
            UPDATE email_outbox
            SET next_attempt_at = now() + @lease
            WHERE id IN (
                SELECT id FROM email_outbox
                WHERE status = 'pending' AND next_attempt_at <= now()
                ORDER BY next_attempt_at
                LIMIT @batch
                FOR UPDATE SKIP LOCKED)
            RETURNING {Columns}", conn);
        cmd.Parameters.AddWithValue("lease", EmailOutboxPolicy.ClaimLease);
        cmd.Parameters.AddWithValue("batch", batchSize);
        return await ReadAsync(cmd, ct);
    }

    public async Task MarkSentAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateControlPlaneConnection();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(@"
            UPDATE email_outbox
            SET status = 'sent', sent_at = now(), attempts = attempts + 1,
                html_body = NULL, text_body = NULL, last_error = NULL
            WHERE id = @id AND status = 'pending'", conn);
        cmd.Parameters.AddWithValue("id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task MarkFailedAsync(Guid id, string error, DateTime nextAttemptAtUtc, bool dead, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateControlPlaneConnection();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(@"
            UPDATE email_outbox
            SET attempts = attempts + 1,
                last_error = @error,
                next_attempt_at = @next,
                status = CASE WHEN @dead THEN 'dead' ELSE 'pending' END
            WHERE id = @id AND status = 'pending'", conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("error", error);
        cmd.Parameters.AddWithValue("next", DateTime.SpecifyKind(nextAttemptAtUtc, DateTimeKind.Utc));
        cmd.Parameters.AddWithValue("dead", dead);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<int> PruneAsync(DateTime sentOlderThanUtc, DateTime deadOlderThanUtc, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateControlPlaneConnection();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(@"
            DELETE FROM email_outbox
            WHERE (status = 'sent' AND sent_at < @sentBefore)
               OR (status = 'dead' AND created_at < @deadBefore)", conn);
        cmd.Parameters.AddWithValue("sentBefore", DateTime.SpecifyKind(sentOlderThanUtc, DateTimeKind.Utc));
        cmd.Parameters.AddWithValue("deadBefore", DateTime.SpecifyKind(deadOlderThanUtc, DateTimeKind.Utc));
        return await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<IReadOnlyList<OutboxMessage>> ReadAsync(NpgsqlCommand cmd, CancellationToken ct)
    {
        var rows = new List<OutboxMessage>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            rows.Add(new OutboxMessage(
                reader.GetGuid("id"),
                reader.GetString("to_email"),
                reader.GetString("to_name"),
                reader.GetString("subject"),
                reader.GetNullableString("html_body") ?? string.Empty,
                reader.GetNullableString("text_body") ?? string.Empty,
                reader.GetInt32("attempts")));
        }
        return rows;
    }
}
