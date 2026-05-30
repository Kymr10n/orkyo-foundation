using Api.Configuration;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using Orkyo.Shared;

namespace Api.Services;

public interface IEmailService
{
    Task<bool> SendEmailAsync(string toEmail, string toName, string subject, string htmlBody, string textBody, CancellationToken ct = default);
    Task<bool> SendVerificationEmailAsync(string toEmail, string displayName, string verificationToken, CancellationToken ct = default);
    Task<bool> SendPasswordResetEmailAsync(string toEmail, string displayName, string resetToken, CancellationToken ct = default);
    Task<bool> SendWelcomeEmailAsync(string toEmail, string displayName, CancellationToken ct = default);
    Task<bool> SendInvitationEmailAsync(string toEmail, string token, DateTime expiresAt, CancellationToken ct = default);
    Task<bool> SendLifecycleWarningEmailAsync(string toEmail, string displayName, string confirmToken, int warningNumber, CancellationToken ct = default);
    Task<bool> SendDormancyNoticeEmailAsync(string toEmail, string displayName, CancellationToken ct = default);
    Task<bool> SendEmailChangeConfirmationAsync(string toEmail, string displayName, string confirmationToken, CancellationToken ct = default);
    Task SendNewUserAlertAsync(string userEmail, string displayName, CancellationToken ct = default);
    Task SendNewTenantAlertAsync(string tenantSlug, string tenantDisplayName, string ownerEmail, CancellationToken ct = default);
}

public class EmailService : IEmailService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<EmailService> _logger;
    private readonly ITenantSettingsService _settingsService;

    public EmailService(
        IConfiguration configuration,
        ILogger<EmailService> logger,
        ITenantSettingsService settingsService)
    {
        _configuration = configuration;
        _logger = logger;
        _settingsService = settingsService;
    }

    private async Task<EmailBranding> GetBrandingAsync()
    {
        try
        {
            var settings = await _settingsService.GetSettingsAsync();
            return settings.ToEmailBranding();
        }
        catch
        {
            // Fallback to defaults if tenant context is unavailable (e.g., pre-auth flows)
            return EmailBranding.Default;
        }
    }

    /// <summary>
    /// Resolve the current branding once, build the template with it, and dispatch — the shared
    /// shape of every branded transactional email.
    /// </summary>
    private async Task<bool> SendTemplatedAsync(
        string toEmail,
        string toName,
        Func<EmailBranding, (string subject, string htmlBody, string textBody)> build,
        CancellationToken ct)
    {
        var (subject, htmlBody, textBody) = build(await GetBrandingAsync());
        return await SendEmailAsync(toEmail, toName, subject, htmlBody, textBody, ct);
    }

    public async Task<bool> SendEmailAsync(string toEmail, string toName, string subject, string htmlBody, string textBody, CancellationToken ct = default)
    {
        try
        {
            // SMTP settings - all from .env, no defaults
            var smtpHost = _configuration.GetRequired(ConfigKeys.SmtpHost);
            var smtpPort = _configuration.GetRequiredInt(ConfigKeys.SmtpPort);
            var smtpUseSsl = _configuration.GetRequiredBool(ConfigKeys.SmtpUseSsl);
            var smtpUsername = _configuration.GetOptionalString(ConfigKeys.SmtpUsername); // legitimately optional
            var smtpPassword = _configuration.GetOptionalString(ConfigKeys.SmtpPassword); // legitimately optional
            var fromEmail = _configuration.GetRequired(ConfigKeys.SmtpFromEmail);
            var fromName = _configuration.GetRequired(ConfigKeys.SmtpFromName);

            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(fromName, fromEmail));
            message.To.Add(new MailboxAddress(toName, toEmail));
            message.Subject = subject;

            var bodyBuilder = new BodyBuilder
            {
                HtmlBody = htmlBody,
                TextBody = textBody
            };
            message.Body = bodyBuilder.ToMessageBody();

            using var client = new SmtpClient();

            _logger.LogInformation("Attempting to send email via {Host}:{Port} (SSL: {UseSsl})",
                smtpHost, smtpPort, smtpUseSsl);

            // Connect to SMTP server
            // MailHog doesn't support SSL/TLS, so we need to use None for local development
            var secureSocketOptions = smtpUseSsl ? SecureSocketOptions.StartTls : SecureSocketOptions.None;
            await client.ConnectAsync(smtpHost, smtpPort, secureSocketOptions);

            _logger.LogDebug("Connected to SMTP server");

            // Authenticate if credentials are provided
            if (!string.IsNullOrEmpty(smtpUsername) && !string.IsNullOrEmpty(smtpPassword))
            {
                await client.AuthenticateAsync(smtpUsername, smtpPassword);
                _logger.LogDebug("SMTP authentication successful");
            }

            // Send email
            await client.SendAsync(message);
            await client.DisconnectAsync(true);

            _logger.LogInformation("Email sent successfully (subject: {Subject})", subject);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email (subject: {Subject}) via SMTP {Host}:{Port}",
                subject, _configuration[ConfigKeys.SmtpHost], _configuration[ConfigKeys.SmtpPort]);
            return false;
        }
    }

    private string BuildTokenLink(string path, string queryParam, string token) =>
        EmailTokenLinkBuilder.Build(
            _configuration.GetRequired(ConfigKeys.AppBaseUrl), path, queryParam, token);

    public async Task<bool> SendVerificationEmailAsync(string toEmail, string displayName, string verificationToken, CancellationToken ct = default)
    {
        var verificationLink = BuildTokenLink("verify-email", "token", verificationToken);
        return await SendTemplatedAsync(toEmail, displayName,
            b => EmailTemplates.GetVerificationEmail(displayName, verificationLink, b), ct);
    }

    public async Task<bool> SendPasswordResetEmailAsync(string toEmail, string displayName, string resetToken, CancellationToken ct = default)
    {
        var resetLink = BuildTokenLink("reset-password", "token", resetToken);
        return await SendTemplatedAsync(toEmail, displayName,
            b => EmailTemplates.GetPasswordResetEmail(displayName, resetLink, b), ct);
    }

    public async Task<bool> SendWelcomeEmailAsync(string toEmail, string displayName, CancellationToken ct = default)
    {
        return await SendTemplatedAsync(toEmail, displayName,
            b => EmailTemplates.GetWelcomeEmail(displayName, b), ct);
    }

    public async Task<bool> SendInvitationEmailAsync(string toEmail, string token, DateTime expiresAt, CancellationToken ct = default)
    {
        var signupLink = BuildTokenLink("signup", "invitation", token);
        return await SendTemplatedAsync(toEmail, toEmail,
            b => EmailTemplates.GetInvitationEmail(signupLink, expiresAt, b), ct);
    }

    public async Task<bool> SendLifecycleWarningEmailAsync(
        string toEmail, string displayName, string confirmToken, int warningNumber, CancellationToken ct = default)
    {
        var confirmLink = BuildTokenLink("api/account/confirm-activity", "token", confirmToken);
        return await SendTemplatedAsync(toEmail, displayName,
            b => EmailTemplates.GetLifecycleWarningEmail(displayName, confirmLink, warningNumber, b), ct);
    }

    public async Task<bool> SendDormancyNoticeEmailAsync(string toEmail, string displayName, CancellationToken ct = default)
    {
        return await SendTemplatedAsync(toEmail, displayName,
            b => EmailTemplates.GetDormancyNoticeEmail(displayName, b), ct);
    }

    public async Task<bool> SendEmailChangeConfirmationAsync(string toEmail, string displayName, string confirmationToken, CancellationToken ct = default)
    {
        var confirmUrl = BuildTokenLink("api/account/confirm-email", "token", confirmationToken);
        return await SendTemplatedAsync(toEmail, displayName,
            b => EmailTemplates.GetEmailChangeConfirmationEmail(displayName, confirmUrl, b), ct);
    }

    public Task SendNewUserAlertAsync(string userEmail, string displayName, CancellationToken ct = default) =>
        SendAdminAlertAsync(
            EmailTemplates.GetNewUserAlertEmail(userEmail, displayName, EmailBranding.Default),
            logContext: userEmail);

    public Task SendNewTenantAlertAsync(string tenantSlug, string tenantDisplayName, string ownerEmail, CancellationToken ct = default) =>
        SendAdminAlertAsync(
            EmailTemplates.GetNewTenantAlertEmail(tenantSlug, tenantDisplayName, ownerEmail, EmailBranding.Default),
            logContext: tenantSlug);

    private async Task SendAdminAlertAsync((string subject, string htmlBody, string textBody) template, string logContext)
    {
        var adminEmail = _configuration.GetOptionalString(ConfigKeys.AlertEmailTo);
        if (string.IsNullOrEmpty(adminEmail))
            return;

        try
        {
            await SendEmailAsync(adminEmail, "Admin", template.subject, template.htmlBody, template.textBody);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send admin alert for {Context}", logContext);
        }
    }
}
