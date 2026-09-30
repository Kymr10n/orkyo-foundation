using MimeKit;

namespace Api.Services;

/// <summary>
/// The delivery step of an outbound email. <see cref="EmailService"/> owns the branding, the
/// template, the throttle, and the retry; a transport only delivers one already-built message.
///
/// The implementation is selected from configuration at registration: a set <c>SMTP_HOST</c>
/// gives <see cref="SmtpEmailTransport"/>, an unset one gives <see cref="LogOnlyEmailTransport"/>.
/// </summary>
public interface IEmailTransport
{
    /// <summary>
    /// Deliver one message, or throw. The caller retries, so this makes a single attempt.
    /// The sender address belongs to the transport: it is SMTP identity configuration, and a
    /// log-only deployment has none.
    /// </summary>
    Task SendAsync(MimeMessage message, CancellationToken ct = default);
}
