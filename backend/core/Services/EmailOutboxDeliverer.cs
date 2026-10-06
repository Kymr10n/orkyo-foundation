using Api.Repositories;
using MimeKit;
using Orkyo.Shared;

namespace Api.Services;

/// <summary>
/// Takes mail out of the outbox and hands it to the transport. Two callers: the API right
/// after it enqueues a mail (so a healthy relay delivers within the request's own
/// background work), and the worker's <c>email-outbox</c> job, which retries what the first
/// attempt could not deliver and prunes settled rows.
///
/// Every attempt starts with a claim, so the two callers never deliver the same row twice.
/// The transport makes one attempt per call; the outbox is the retry.
/// </summary>
public sealed class EmailOutboxDeliverer
{
    /// <summary>
    /// At most five concurrent transport sends per process. Protects the relay during bulk
    /// runs such as lifecycle warnings and announcement broadcasts.
    /// </summary>
    private static readonly SemaphoreSlim SendThrottle = new(5, 5);

    private readonly IEmailOutboxRepository _outbox;
    private readonly IEmailTransport _transport;
    private readonly ILogger<EmailOutboxDeliverer> _logger;
    private readonly TimeProvider _time;

    public EmailOutboxDeliverer(
        IEmailOutboxRepository outbox,
        IEmailTransport transport,
        ILogger<EmailOutboxDeliverer> logger,
        TimeProvider time)
    {
        _outbox = outbox;
        _transport = transport;
        _logger = logger;
        _time = time;
    }

    /// <summary>
    /// One attempt at the row with this id, if it is pending and unclaimed. Returns true when
    /// the mail went out; false when it stays in the outbox for the worker.
    /// </summary>
    public async Task<bool> TryDeliverNowAsync(Guid id, CancellationToken ct = default)
    {
        var message = await _outbox.ClaimAsync(id, ct);
        return message is not null && await DeliverAsync(message, ct);
    }

    /// <summary>
    /// The worker pass: deliver every due row in batches until none is left, then prune
    /// settled rows. Returns how many mails went out.
    /// </summary>
    public async Task<int> DeliverPendingAsync(CancellationToken ct = default)
    {
        var delivered = 0;
        while (!ct.IsCancellationRequested)
        {
            var batch = await _outbox.ClaimDueAsync(EmailOutboxPolicy.DrainBatchSize, ct);
            if (batch.Count == 0) break;

            foreach (var message in batch)
            {
                if (ct.IsCancellationRequested) break;
                if (await DeliverAsync(message, ct)) delivered++;
            }

            if (batch.Count < EmailOutboxPolicy.DrainBatchSize) break;
        }

        var now = _time.GetUtcNow().UtcDateTime;
        var pruned = await _outbox.PruneAsync(
            now - EmailOutboxPolicy.SentRetention, now - EmailOutboxPolicy.DeadRetention, ct);
        if (delivered > 0 || pruned > 0)
            _logger.LogInformation("Email outbox: delivered {Delivered}, pruned {Pruned}", delivered, pruned);
        return delivered;
    }

    private async Task<bool> DeliverAsync(OutboxMessage message, CancellationToken ct)
    {
        await SendThrottle.WaitAsync(ct);
        try
        {
            // The sender is the transport's: it is SMTP identity, and log-only mail has none.
            var mime = new MimeMessage();
            mime.To.Add(new MailboxAddress(message.ToName, message.ToEmail));
            mime.Subject = message.Subject;
            mime.Body = new BodyBuilder { HtmlBody = message.HtmlBody, TextBody = message.TextBody }.ToMessageBody();

            await _transport.SendAsync(mime, ct);
            await _outbox.MarkSentAsync(message.Id, ct);
            _logger.LogInformation("Email sent (subject: {Subject})", message.Subject);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var attemptsMade = message.Attempts + 1;
            var dead = attemptsMade >= EmailOutboxPolicy.MaxAttempts;
            var next = _time.GetUtcNow().UtcDateTime + EmailOutboxPolicy.RetryDelay(attemptsMade);
            await _outbox.MarkFailedAsync(message.Id, ex.Message, next, dead, ct);

            if (dead)
                _logger.LogError(ex, "Email given up after {Attempts} attempts (subject: {Subject}) via {Transport}",
                    attemptsMade, message.Subject, _transport.GetType().Name);
            else
                _logger.LogWarning(ex, "Email attempt {Attempt}/{Max} failed (subject: {Subject}); next try at {Next:u}",
                    attemptsMade, EmailOutboxPolicy.MaxAttempts, message.Subject, next);
            return false;
        }
        finally
        {
            SendThrottle.Release();
        }
    }
}
