using Api.Helpers;
using Api.Repositories;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Api.Services;

/// <summary>
/// Sends the email channel of platform announcements to all registered users. Runs in the worker
/// (never inline in the create request): for each announcement with the 'email' channel whose
/// broadcast hasn't run yet, it queues one outbox row per active, non-opted-out user — throttled —
/// then marks the announcement's broadcast complete. Each row carries the idempotency key
/// <c>announcement:{id}:{userId}</c>, so a broadcast interrupted by a deploy or a crash is simply
/// re-run: already-queued recipients are no-ops. A recipient that cannot be queued fails the job
/// (the coordinator backs off and retries) instead of being silently skipped.
/// </summary>
public interface IAnnouncementBroadcastService
{
    Task ProcessPendingBroadcastsAsync(CancellationToken ct = default);
}

public sealed class AnnouncementBroadcastService : IAnnouncementBroadcastService
{
    private const int MaxConcurrentSends = 5;

    private readonly IAnnouncementRepository _repository;
    private readonly IEmailService _emailService;
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ILogger<AnnouncementBroadcastService> _logger;

    public AnnouncementBroadcastService(
        IAnnouncementRepository repository,
        IEmailService emailService,
        IDbConnectionFactory connectionFactory,
        ILogger<AnnouncementBroadcastService> logger)
    {
        _repository = repository;
        _emailService = emailService;
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task ProcessPendingBroadcastsAsync(CancellationToken ct = default)
    {
        var pending = await _repository.GetPendingEmailBroadcastsAsync(ct);
        if (pending.Count == 0) return;

        var recipients = await GetActiveRecipientsAsync(ct);
        _logger.LogInformation(
            "Announcement broadcast: {Announcements} pending, {Recipients} recipient(s)",
            pending.Count, recipients.Count);

        foreach (var announcement in pending)
        {
            if (ct.IsCancellationRequested) break;

            // Important announcements are mandatory: they email every active user, including those who
            // opted out. Normal announcements respect the opt-out.
            var audience = announcement.IsImportant ? recipients : recipients.Where(r => !r.OptedOut);

            var options = new ParallelOptions { MaxDegreeOfParallelism = MaxConcurrentSends, CancellationToken = ct };
            await Parallel.ForEachAsync(audience, options, async (user, token) =>
            {
                var queued = await _emailService.SendAnnouncementEmailAsync(
                    user.Email, user.DisplayName, announcement.Title, announcement.Body,
                    announcement.IsImportant, user.UnsubscribeToken,
                    dedupeKey: $"announcement:{announcement.Id}:{user.Id}", token);
                if (!queued)
                    throw new InvalidOperationException(
                        $"Announcement {announcement.Id}: could not queue mail for user {user.Id}");
            });

            // Every recipient now has an outbox row (delivery and retries are the email-outbox
            // job's); marking the announcement complete stops it being re-broadcast.
            await _repository.MarkEmailSentAsync(announcement.Id, ct);
            _logger.LogInformation("Announcement {AnnouncementId} email broadcast complete", announcement.Id);
        }
    }

    private async Task<List<Recipient>> GetActiveRecipientsAsync(CancellationToken ct)
    {
        await using var db = _connectionFactory.CreateControlPlaneConnection();
        await db.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(@"
            SELECT id, email, display_name, unsubscribe_token, announcement_email_opt_out
            FROM users
            WHERE status = 'active'
            ORDER BY id", db);

        var results = new List<Recipient>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(new Recipient(
                reader.GetGuid("id"),
                reader.GetString("email"),
                reader.GetNullableString("display_name") ?? string.Empty,
                reader.GetGuid("unsubscribe_token"),
                reader.GetBoolean("announcement_email_opt_out")));
        }
        return results;
    }

    private readonly record struct Recipient(Guid Id, string Email, string DisplayName, Guid UnsubscribeToken, bool OptedOut);
}
