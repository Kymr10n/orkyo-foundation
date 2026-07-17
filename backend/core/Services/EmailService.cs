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

    // Tenant lifecycle (worker) + owner welcome
    Task<bool> SendTenantInactivityWarningAsync(string toEmail, string tenantName, string loginUrl, int daysUntilSuspend, CancellationToken ct = default);
    Task<bool> SendTenantSuspendedAsync(string toEmail, string tenantName, string reactivateUrl, int deleteAfterDays, CancellationToken ct = default);
    Task<bool> SendTenantDeletingWarningAsync(string toEmail, string tenantName, string restoreUrl, int daysUntilDelete, CancellationToken ct = default);
    Task<bool> SendTenantDeletedAsync(string toEmail, string tenantName, CancellationToken ct = default);
    Task<bool> SendTenantReactivatedAsync(string toEmail, string tenantName, string appUrl, CancellationToken ct = default);
    Task<bool> SendTenantWelcomeAsync(string toEmail, string tenantName, string appUrl, CancellationToken ct = default);

    // Membership / role / ownership / quota / tier
    Task<bool> SendRoleChangedAsync(string toEmail, string tenantName, string newRole, string appUrl, CancellationToken ct = default);
    Task<bool> SendMemberRemovedAsync(string toEmail, string tenantName, CancellationToken ct = default);
    Task<bool> SendOwnershipReceivedAsync(string toEmail, string tenantName, string appUrl, CancellationToken ct = default);
    Task<bool> SendOwnershipTransferredAsync(string toEmail, string tenantName, string newOwnerEmail, CancellationToken ct = default);
    Task<bool> SendQuotaLimitReachedAsync(string toEmail, string tenantName, string resourceLabel, long limit, string manageUrl, CancellationToken ct = default);
    Task<bool> SendTierChangedAsync(string toEmail, string tenantName, string newPlan, string appUrl, CancellationToken ct = default);

    // Security events
    Task<bool> SendPasswordChangedAsync(string toEmail, string displayName, CancellationToken ct = default);
    Task<bool> SendMfaChangedAsync(string toEmail, string displayName, bool enabled, CancellationToken ct = default);
    Task<bool> SendEmailChangeRequestedOldAddressAsync(string toEmail, string displayName, string newEmail, CancellationToken ct = default);
    Task<bool> SendEmailChangedAsync(string toEmail, string displayName, string newEmail, CancellationToken ct = default);

    // Platform announcements
    Task<bool> SendAnnouncementEmailAsync(string toEmail, string displayName, string title, string body, bool isImportant, Guid unsubscribeToken, CancellationToken ct = default);
}

/// <summary>
/// Retry policy for outbound SMTP sends. The default matches the behavior ported from the former
/// private UserLifecycleService email path: 3 attempts with exponential backoff (2^attempt seconds).
/// Tests pass a zero-backoff instance to keep failure paths fast.
/// </summary>
public sealed record EmailSendOptions(int MaxAttempts, Func<int, TimeSpan> Backoff)
{
    public static EmailSendOptions Default { get; } = new(3, attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)));
}

public class EmailService : IEmailService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<EmailService> _logger;
    private readonly ITenantSettingsService _settingsService;
    private readonly EmailSendOptions _sendOptions;

    /// <summary>
    /// At most 5 concurrent SMTP sends process-wide — protects the SMTP server during bulk
    /// runs (e.g. lifecycle warnings). Ported from the former private UserLifecycleService path.
    /// </summary>
    private static readonly SemaphoreSlim SendThrottle = new(5, 5);

    public EmailService(
        IConfiguration configuration,
        ILogger<EmailService> logger,
        ITenantSettingsService settingsService,
        EmailSendOptions? sendOptions = null)
    {
        _configuration = configuration;
        _logger = logger;
        _settingsService = settingsService;
        _sendOptions = sendOptions ?? EmailSendOptions.Default;
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

            await SendThrottle.WaitAsync(ct);
            try
            {
                for (var attempt = 1; attempt <= _sendOptions.MaxAttempts; attempt++)
                {
                    try
                    {
                        using var client = new SmtpClient();

                        _logger.LogInformation("Attempting to send email via {Host}:{Port} (SSL: {UseSsl})",
                            smtpHost, smtpPort, smtpUseSsl);

                        // Connect to SMTP server
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

                        // Send email
                        await client.SendAsync(message);
                        await client.DisconnectAsync(true, ct);

                        _logger.LogInformation("Email sent successfully (subject: {Subject})", subject);
                        return true;
                    }
                    catch (Exception ex) when (attempt < _sendOptions.MaxAttempts)
                    {
                        _logger.LogWarning(ex, "Email send attempt {Attempt}/{MaxAttempts} failed (subject: {Subject}); retrying",
                            attempt, _sendOptions.MaxAttempts, subject);
                        await Task.Delay(_sendOptions.Backoff(attempt), ct);
                    }
                }

                return false; // unreachable — the final attempt either returns true or throws
            }
            finally
            {
                SendThrottle.Release();
            }
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

    public async Task<bool> SendAnnouncementEmailAsync(string toEmail, string displayName, string title, string body, bool isImportant, Guid unsubscribeToken, CancellationToken ct = default)
    {
        // APP_BASE_URL proxies /api/* to the backend, so the unsubscribe link can target it directly.
        var appBaseUrl = _configuration.GetRequired(ConfigKeys.AppBaseUrl).TrimEnd('/');
        var unsubscribeUrl = $"{appBaseUrl}/api/announcements/unsubscribe?token={unsubscribeToken}";
        return await SendTemplatedAsync(toEmail, displayName,
            b => EmailTemplates.GetAnnouncementEmail(title, body, isImportant, unsubscribeUrl, b), ct);
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

    public Task<bool> SendTenantInactivityWarningAsync(string toEmail, string tenantName, string loginUrl, int daysUntilSuspend, CancellationToken ct = default) =>
        SendTemplatedAsync(toEmail, toEmail,
            b => EmailTemplates.GetTenantInactivityWarningEmail(tenantName, loginUrl, daysUntilSuspend, b), ct);

    public Task<bool> SendTenantSuspendedAsync(string toEmail, string tenantName, string reactivateUrl, int deleteAfterDays, CancellationToken ct = default) =>
        SendTemplatedAsync(toEmail, toEmail,
            b => EmailTemplates.GetTenantSuspendedEmail(tenantName, reactivateUrl, deleteAfterDays, b), ct);

    public Task<bool> SendTenantDeletingWarningAsync(string toEmail, string tenantName, string restoreUrl, int daysUntilDelete, CancellationToken ct = default) =>
        SendTemplatedAsync(toEmail, toEmail,
            b => EmailTemplates.GetTenantDeletingWarningEmail(tenantName, restoreUrl, daysUntilDelete, b), ct);

    public Task<bool> SendTenantDeletedAsync(string toEmail, string tenantName, CancellationToken ct = default) =>
        SendTemplatedAsync(toEmail, toEmail,
            b => EmailTemplates.GetTenantDeletedEmail(tenantName, b), ct);

    public Task<bool> SendTenantReactivatedAsync(string toEmail, string tenantName, string appUrl, CancellationToken ct = default) =>
        SendTemplatedAsync(toEmail, toEmail,
            b => EmailTemplates.GetTenantReactivatedEmail(tenantName, appUrl, b), ct);

    public Task<bool> SendTenantWelcomeAsync(string toEmail, string tenantName, string appUrl, CancellationToken ct = default) =>
        SendTemplatedAsync(toEmail, toEmail,
            b => EmailTemplates.GetTenantWelcomeEmail(tenantName, appUrl, b), ct);

    public Task<bool> SendRoleChangedAsync(string toEmail, string tenantName, string newRole, string appUrl, CancellationToken ct = default) =>
        SendTemplatedAsync(toEmail, toEmail, b => EmailTemplates.GetRoleChangedEmail(tenantName, newRole, appUrl, b), ct);

    public Task<bool> SendMemberRemovedAsync(string toEmail, string tenantName, CancellationToken ct = default) =>
        SendTemplatedAsync(toEmail, toEmail, b => EmailTemplates.GetMemberRemovedEmail(tenantName, b), ct);

    public Task<bool> SendOwnershipReceivedAsync(string toEmail, string tenantName, string appUrl, CancellationToken ct = default) =>
        SendTemplatedAsync(toEmail, toEmail, b => EmailTemplates.GetOwnershipReceivedEmail(tenantName, appUrl, b), ct);

    public Task<bool> SendOwnershipTransferredAsync(string toEmail, string tenantName, string newOwnerEmail, CancellationToken ct = default) =>
        SendTemplatedAsync(toEmail, toEmail, b => EmailTemplates.GetOwnershipTransferredEmail(tenantName, newOwnerEmail, b), ct);

    public Task<bool> SendQuotaLimitReachedAsync(string toEmail, string tenantName, string resourceLabel, long limit, string manageUrl, CancellationToken ct = default) =>
        SendTemplatedAsync(toEmail, toEmail, b => EmailTemplates.GetQuotaLimitReachedEmail(tenantName, resourceLabel, limit, manageUrl, b), ct);

    public Task<bool> SendTierChangedAsync(string toEmail, string tenantName, string newPlan, string appUrl, CancellationToken ct = default) =>
        SendTemplatedAsync(toEmail, toEmail, b => EmailTemplates.GetTierChangedEmail(tenantName, newPlan, appUrl, b), ct);

    public Task<bool> SendPasswordChangedAsync(string toEmail, string displayName, CancellationToken ct = default) =>
        SendTemplatedAsync(toEmail, displayName, b => EmailTemplates.GetPasswordChangedEmail(displayName, b), ct);

    public Task<bool> SendMfaChangedAsync(string toEmail, string displayName, bool enabled, CancellationToken ct = default) =>
        SendTemplatedAsync(toEmail, displayName, b => EmailTemplates.GetMfaChangedEmail(displayName, enabled, b), ct);

    public Task<bool> SendEmailChangeRequestedOldAddressAsync(string toEmail, string displayName, string newEmail, CancellationToken ct = default) =>
        SendTemplatedAsync(toEmail, displayName, b => EmailTemplates.GetEmailChangeRequestedOldAddressEmail(displayName, newEmail, b), ct);

    public Task<bool> SendEmailChangedAsync(string toEmail, string displayName, string newEmail, CancellationToken ct = default) =>
        SendTemplatedAsync(toEmail, displayName, b => EmailTemplates.GetEmailChangedEmail(displayName, newEmail, b), ct);

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

/// <summary>
/// Best-effort send helpers shared by hand-rolled admin-notification callers (feedback, contact, ...).
/// A mail failure must never surface to the caller — it only logs a warning.
/// </summary>
public static class EmailServiceExtensions
{
    public static async Task TrySendNotificationAsync(
        this IEmailService emailService, string? notifyEmail, string subject, string htmlBody, string textBody,
        ILogger logger, string failureLogContext)
    {
        if (string.IsNullOrEmpty(notifyEmail)) return;

        try
        {
            await emailService.SendEmailAsync(notifyEmail, "Orkyo Team", subject, htmlBody, textBody);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send {Context}", failureLogContext);
        }
    }
}
