using Api.Configuration;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using Orkyo.Shared;

namespace Api.Services;

/// <summary>
/// Delivers mail over SMTP with MailKit. Registered only when <c>SMTP_HOST</c> is set, so
/// every key it reads is guaranteed present: <see cref="Api.Configuration.ConfigurationValidator"/>
/// requires the whole SMTP block once the host is set.
/// </summary>
public sealed class SmtpEmailTransport : IEmailTransport
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<SmtpEmailTransport> _logger;

    public SmtpEmailTransport(IConfiguration configuration, ILogger<SmtpEmailTransport> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task SendAsync(MimeMessage message, CancellationToken ct = default)
    {
        var smtpHost = _configuration.GetRequired(ConfigKeys.SmtpHost);
        var smtpPort = _configuration.GetRequiredInt(ConfigKeys.SmtpPort);
        var smtpUseSsl = _configuration.GetRequiredBool(ConfigKeys.SmtpUseSsl);
        var smtpUsername = _configuration.GetOptionalString(ConfigKeys.SmtpUsername); // legitimately optional
        var smtpPassword = _configuration.GetOptionalString(ConfigKeys.SmtpPassword); // legitimately optional

        // Cleared first: the caller retries with the same MimeMessage, and a bare Add would
        // put one more sender on the message for every attempt.
        message.From.Clear();
        message.From.Add(new MailboxAddress(
            _configuration.GetRequired(ConfigKeys.SmtpFromName),
            _configuration.GetRequired(ConfigKeys.SmtpFromEmail)));

        using var client = new SmtpClient();

        _logger.LogInformation("Attempting to send email via {Host}:{Port} (SSL: {UseSsl})",
            smtpHost, smtpPort, smtpUseSsl);

        // MailHog doesn't support SSL/TLS, so we need to use None for local development
        var secureSocketOptions = smtpUseSsl ? SecureSocketOptions.StartTls : SecureSocketOptions.None;
        await client.ConnectAsync(smtpHost, smtpPort, secureSocketOptions, ct);

        _logger.LogDebug("Connected to SMTP server");

        // Authenticate if credentials are provided
        if (!string.IsNullOrEmpty(smtpUsername) && !string.IsNullOrEmpty(smtpPassword))
        {
            await client.AuthenticateAsync(smtpUsername, smtpPassword, ct);
            _logger.LogDebug("SMTP authentication successful");
        }

        await client.SendAsync(message, ct);
        await client.DisconnectAsync(true, ct);
    }
}
