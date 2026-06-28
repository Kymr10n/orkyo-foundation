using Api.Repositories;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Api.Services;

/// <summary>
/// Sends the email channel of platform announcements to all registered users. Runs in the worker
/// (never inline in the create request): for each announcement with the 'email' channel whose
/// broadcast hasn't run yet, it emails every active, non-opted-out user — throttled — then marks
/// the announcement's broadcast complete so it isn't re-sent.
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
                try
                {
                    await _emailService.SendAnnouncementEmailAsync(
                        user.Email, user.DisplayName, announcement.Title, announcement.Body,
                        announcement.IsImportant, user.UnsubscribeToken, token);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to email announcement {AnnouncementId} to {Email}",
                        announcement.Id, user.Email);
                }
            });

            // Mark complete even if some individual sends failed — announcements are not re-broadcast.
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
                reader.GetGuid(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                reader.GetGuid(3),
                reader.GetBoolean(4)));
        }
        return results;
    }

    private readonly record struct Recipient(Guid Id, string Email, string DisplayName, Guid UnsubscribeToken, bool OptedOut);
}
