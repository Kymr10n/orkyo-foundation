using MimeKit;

namespace Api.Services;

/// <summary>
/// Writes mail to the log instead of delivering it. Registered when <c>SMTP_HOST</c> is unset,
/// which lets a self-host deployment start with no mail configuration at all.
///
/// Evaluation only: invitation and confirmation links carry live tokens, so anyone who reads the
/// log can use them. Each host warns about this at startup — see
/// <see cref="EmailTransportRegistration.LogOnlyWarning"/>.
/// </summary>
public sealed class LogOnlyEmailTransport : IEmailTransport
{
    private readonly ILogger<LogOnlyEmailTransport> _logger;

    public LogOnlyEmailTransport(ILogger<LogOnlyEmailTransport> logger)
    {
        _logger = logger;
    }

    public Task SendAsync(MimeMessage message, CancellationToken ct = default)
    {
        // The text body carries the same links as the HTML one and stays readable in a log.
        _logger.LogInformation(
            "Email (log-only) to {Recipients} | subject: {Subject}\n{TextBody}",
            string.Join(", ", message.To.Mailboxes.Select(m => m.Address)),
            message.Subject,
            message.TextBody ?? "");

        return Task.CompletedTask;
    }
}
