using Api.Configuration;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using Orkyo.Shared;

namespace Api.Services;

/// <summary>
/// Delivers mail over SMTP with MailKit. Registered only when <c>SMTP_HOST</c> is set, so
/// every key it reads is present: <see cref="ConfigurationValidator"/> requires the whole SMTP
/// block once the host is set.
///
/// The values are read once here, not per send. It takes <c>IConfiguration</c> rather than
/// <see cref="DeploymentConfig"/> because the workers do not register the latter: it requires
/// API-only keys (OIDC_AUTHORITY, the Postgres path) that a worker environment does not set.
/// </summary>
public sealed class SmtpEmailTransport : IEmailTransport
{
    private readonly string _host;
    private readonly int _port;
    private readonly bool _useSsl;
    private readonly string _username; // legitimately optional
    private readonly string _password; // legitimately optional
    private readonly MailboxAddress _from;
    private readonly ILogger<SmtpEmailTransport> _logger;

    public SmtpEmailTransport(IConfiguration configuration, ILogger<SmtpEmailTransport> logger)
    {
        _host = configuration.GetRequired(ConfigKeys.SmtpHost);
        _port = configuration.GetRequiredInt(ConfigKeys.SmtpPort);
        _useSsl = configuration.GetRequiredBool(ConfigKeys.SmtpUseSsl);
        _username = configuration.GetOptionalString(ConfigKeys.SmtpUsername);
        _password = configuration.GetOptionalString(ConfigKeys.SmtpPassword);
        _from = new MailboxAddress(
            configuration.GetRequired(ConfigKeys.SmtpFromName),
            configuration.GetRequired(ConfigKeys.SmtpFromEmail));
        _logger = logger;
    }

    public async Task SendAsync(MimeMessage message, CancellationToken ct = default)
    {
        // Cleared first: the caller retries with the same MimeMessage, and a bare Add would
        // put one more sender on the message for every attempt.
        message.From.Clear();
        message.From.Add(_from);

        using var client = new SmtpClient();

        _logger.LogInformation("Attempting to send email via {Host}:{Port} (SSL: {UseSsl})",
            _host, _port, _useSsl);

        // MailHog doesn't support SSL/TLS, so we need to use None for local development
        var secureSocketOptions = _useSsl ? SecureSocketOptions.StartTls : SecureSocketOptions.None;
        await client.ConnectAsync(_host, _port, secureSocketOptions, ct);

        _logger.LogDebug("Connected to SMTP server");

        // Authenticate if credentials are provided
        if (!string.IsNullOrEmpty(_username) && !string.IsNullOrEmpty(_password))
        {
            await client.AuthenticateAsync(_username, _password, ct);
            _logger.LogDebug("SMTP authentication successful");
        }

        await client.SendAsync(message, ct);
        await client.DisconnectAsync(true, ct);
    }
}
