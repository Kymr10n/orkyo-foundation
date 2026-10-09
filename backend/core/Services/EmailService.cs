using Api.Configuration;
using Api.Repositories;
using Orkyo.Shared;

namespace Api.Services;

public interface IEmailService
{
    Task<bool> SendEmailAsync(string toEmail, string toName, string subject, string htmlBody, string textBody, CancellationToken ct = default);
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
    Task<bool> SendPasskeyRemovedAsync(string toEmail, string displayName, CancellationToken ct = default);
    Task<bool> SendEmailChangeRequestedOldAddressAsync(string toEmail, string displayName, string newEmail, CancellationToken ct = default);
    Task<bool> SendEmailChangedAsync(string toEmail, string displayName, string newEmail, CancellationToken ct = default);

    // Platform announcements
    Task<bool> SendAnnouncementEmailAsync(string toEmail, string displayName, string title, string body, bool isImportant, Guid unsubscribeToken, CancellationToken ct = default);
}

public class EmailService : IEmailService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<EmailService> _logger;
    private readonly ITenantSettingsService _settingsService;
    private readonly IEmailOutboxRepository _outbox;
    private readonly EmailOutboxDeliverer _deliverer;

    public EmailService(
        IConfiguration configuration,
        ILogger<EmailService> logger,
        ITenantSettingsService settingsService,
        IEmailOutboxRepository outbox,
        EmailOutboxDeliverer deliverer)
    {
        _configuration = configuration;
        _logger = logger;
        _settingsService = settingsService;
        _outbox = outbox;
        _deliverer = deliverer;
    }

    private async Task<EmailBranding> GetBrandingAsync()
    {
        try
        {
            var settings = await _settingsService.GetSettingsAsync();
            return settings.ToEmailBranding();
        }
        catch (Exception ex)
        {
            // Fallback to defaults if tenant context is unavailable (e.g., pre-auth flows).
            // Logged so real settings-service failures don't hide behind the fallback.
            _logger.LogWarning(ex, "Falling back to default email branding (tenant settings unavailable)");
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

    /// <summary>
    /// Writes the rendered mail to the outbox, then makes one delivery attempt. True means the
    /// mail is durably queued: it has either gone out or will be retried by the worker's
    /// email-outbox job. False means it could not even be queued (the database refused the
    /// write), which is the one case where the caller must treat the mail as not sent.
    /// </summary>
    public async Task<bool> SendEmailAsync(string toEmail, string toName, string subject, string htmlBody, string textBody, CancellationToken ct = default)
    {
        Guid id;
        try
        {
            id = await _outbox.EnqueueAsync(toEmail, toName, subject, htmlBody, textBody, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to queue email (subject: {Subject})", subject);
            return false;
        }

        // Best effort: a failure here is recorded on the row and the worker retries.
        await _deliverer.TryDeliverNowAsync(id, ct);
        return true;
    }

    private string BuildTokenLink(string path, string queryParam, string token) =>
        EmailTokenLinkBuilder.Build(
            _configuration.GetRequired(ConfigKeys.AppBaseUrl), path, queryParam, token);

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

    public Task<bool> SendPasskeyRemovedAsync(string toEmail, string displayName, CancellationToken ct = default) =>
        SendTemplatedAsync(toEmail, displayName, b => EmailTemplates.GetPasskeyRemovedEmail(displayName, b), ct);

    public Task<bool> SendEmailChangeRequestedOldAddressAsync(string toEmail, string displayName, string newEmail, CancellationToken ct = default) =>
        SendTemplatedAsync(toEmail, displayName, b => EmailTemplates.GetEmailChangeRequestedOldAddressEmail(displayName, newEmail, b), ct);

    public Task<bool> SendEmailChangedAsync(string toEmail, string displayName, string newEmail, CancellationToken ct = default) =>
        SendTemplatedAsync(toEmail, displayName, b => EmailTemplates.GetEmailChangedEmail(displayName, newEmail, b), ct);

    // Delegates to the shared best-effort helper so there is exactly one notification-send shape.
    private Task SendAdminAlertAsync((string subject, string htmlBody, string textBody) template, string logContext) =>
        this.TrySendNotificationAsync(
            _configuration.GetOptionalString(ConfigKeys.AlertEmailTo),
            template.subject, template.htmlBody, template.textBody, _logger, $"admin alert for {logContext}");
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
