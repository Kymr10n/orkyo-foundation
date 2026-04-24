using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using Api.Configuration;
using Orkyo.Shared;

namespace Api.Services;

public interface IEmailService
{
    Task<bool> SendEmailAsync(string toEmail, string toName, string subject, string htmlBody, string textBody);
    Task<bool> SendVerificationEmailAsync(string toEmail, string displayName, string verificationToken);
    Task<bool> SendPasswordResetEmailAsync(string toEmail, string displayName, string resetToken);
    Task<bool> SendWelcomeEmailAsync(string toEmail, string displayName);
    Task<bool> SendInvitationEmailAsync(string toEmail, string token, DateTime expiresAt);
    Task<bool> SendLifecycleWarningEmailAsync(string toEmail, string displayName, string confirmToken, int warningNumber);
    Task<bool> SendDormancyNoticeEmailAsync(string toEmail, string displayName);
    Task SendNewUserAlertAsync(string userEmail, string displayName);
    Task SendNewTenantAlertAsync(string tenantSlug, string tenantDisplayName, string ownerEmail);
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

    public async Task<bool> SendEmailAsync(string toEmail, string toName, string subject, string htmlBody, string textBody)
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

            _logger.LogInformation("Attempting to send email to {Email} via {Host}:{Port} (SSL: {UseSsl})",
                toEmail, smtpHost, smtpPort, smtpUseSsl);

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

            _logger.LogInformation("Email sent successfully to {Email} with subject: {Subject}", toEmail, subject);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {Email} with subject: {Subject}. SMTP: {Host}:{Port}",
                toEmail, subject, _configuration[ConfigKeys.SmtpHost], _configuration[ConfigKeys.SmtpPort]);
            return false;
        }
    }

    private string BuildTokenLink(string path, string queryParam, string token) =>
        EmailTokenLinkBuilder.Build(
            _configuration.GetRequired(ConfigKeys.AppBaseUrl), path, queryParam, token);

    public async Task<bool> SendVerificationEmailAsync(string toEmail, string displayName, string verificationToken)
    {
        var verificationLink = BuildTokenLink("verify-email", "token", verificationToken);
        var (subject, htmlBody, textBody) = EmailTemplates.GetVerificationEmail(displayName, verificationLink, await GetBrandingAsync());
        return await SendEmailAsync(toEmail, displayName, subject, htmlBody, textBody);
    }

    public async Task<bool> SendPasswordResetEmailAsync(string toEmail, string displayName, string resetToken)
    {
        var resetLink = BuildTokenLink("reset-password", "token", resetToken);
        var (subject, htmlBody, textBody) = EmailTemplates.GetPasswordResetEmail(displayName, resetLink, await GetBrandingAsync());
        return await SendEmailAsync(toEmail, displayName, subject, htmlBody, textBody);
    }

    public async Task<bool> SendWelcomeEmailAsync(string toEmail, string displayName)
    {
        var (subject, htmlBody, textBody) = EmailTemplates.GetWelcomeEmail(displayName, await GetBrandingAsync());

        return await SendEmailAsync(toEmail, displayName, subject, htmlBody, textBody);
    }

    public async Task<bool> SendInvitationEmailAsync(string toEmail, string token, DateTime expiresAt)
    {
        var signupLink = BuildTokenLink("signup", "invitation", token);
        var (subject, htmlBody, textBody) = EmailTemplates.GetInvitationEmail(signupLink, expiresAt, await GetBrandingAsync());
        return await SendEmailAsync(toEmail, toEmail, subject, htmlBody, textBody);
    }

    public async Task<bool> SendLifecycleWarningEmailAsync(
        string toEmail, string displayName, string confirmToken, int warningNumber)
    {
        var confirmLink = BuildTokenLink("api/account/confirm-activity", "token", confirmToken);
        var (subject, htmlBody, textBody) = EmailTemplates.GetLifecycleWarningEmail(
            displayName, confirmLink, warningNumber, await GetBrandingAsync());
        return await SendEmailAsync(toEmail, displayName, subject, htmlBody, textBody);
    }

    public async Task<bool> SendDormancyNoticeEmailAsync(string toEmail, string displayName)
    {
        var (subject, htmlBody, textBody) = EmailTemplates.GetDormancyNoticeEmail(displayName, await GetBrandingAsync());
        return await SendEmailAsync(toEmail, displayName, subject, htmlBody, textBody);
    }

    public Task SendNewUserAlertAsync(string userEmail, string displayName) =>
        SendAdminAlertAsync(
            EmailTemplates.GetNewUserAlertEmail(userEmail, displayName, EmailBranding.Default),
            logContext: userEmail);

    public Task SendNewTenantAlertAsync(string tenantSlug, string tenantDisplayName, string ownerEmail) =>
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
