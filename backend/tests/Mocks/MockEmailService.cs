using Api.Services;

namespace Orkyo.Foundation.Tests.Mocks;

/// <summary>
/// Trackable mock for <see cref="IEmailService"/> used in integration tests that
/// need to assert email sends without starting an SMTP server.
/// </summary>
public class MockEmailService : IEmailService
{
    // ── General send ──────────────────────────────────────────────────────────
    public int SendEmailCallCount { get; private set; }

    public Task<bool> SendEmailAsync(string toEmail, string toName, string subject,
        string htmlBody, string textBody, CancellationToken ct = default)
    {
        SendEmailCallCount++;
        return Task.FromResult(true);
    }

    // ── Verification ──────────────────────────────────────────────────────────
    public int SendVerificationCallCount { get; private set; }

    public Task<bool> SendVerificationEmailAsync(string toEmail, string displayName,
        string verificationToken, CancellationToken ct = default)
    {
        SendVerificationCallCount++;
        return Task.FromResult(true);
    }

    // ── Password reset ────────────────────────────────────────────────────────
    public int SendPasswordResetCallCount { get; private set; }

    public Task<bool> SendPasswordResetEmailAsync(string toEmail, string displayName,
        string resetToken, CancellationToken ct = default)
    {
        SendPasswordResetCallCount++;
        return Task.FromResult(true);
    }

    // ── Welcome ───────────────────────────────────────────────────────────────
    public int SendWelcomeCallCount { get; private set; }

    public Task<bool> SendWelcomeEmailAsync(string toEmail, string displayName,
        CancellationToken ct = default)
    {
        SendWelcomeCallCount++;
        return Task.FromResult(true);
    }

    // ── Invitation ────────────────────────────────────────────────────────────
    public int SendInvitationCallCount { get; private set; }

    public Task<bool> SendInvitationEmailAsync(string toEmail, string token,
        DateTime expiresAt, CancellationToken ct = default)
    {
        SendInvitationCallCount++;
        return Task.FromResult(true);
    }

    // ── Lifecycle warning ─────────────────────────────────────────────────────
    public int SendLifecycleWarningCallCount { get; private set; }

    public Task<bool> SendLifecycleWarningEmailAsync(string toEmail, string displayName,
        string confirmToken, int warningNumber, CancellationToken ct = default)
    {
        SendLifecycleWarningCallCount++;
        return Task.FromResult(true);
    }

    // ── Dormancy notice ───────────────────────────────────────────────────────
    public int SendDormancyNoticeCallCount { get; private set; }

    public Task<bool> SendDormancyNoticeEmailAsync(string toEmail, string displayName,
        CancellationToken ct = default)
    {
        SendDormancyNoticeCallCount++;
        return Task.FromResult(true);
    }

    // ── Email change confirmation ─────────────────────────────────────────────
    public int SendEmailChangeConfirmationCallCount { get; private set; }
    public (string toEmail, string displayName, string token) LastSendEmailChangeConfirmationCall { get; private set; }
    /// <summary>When set, the next call to <see cref="SendEmailChangeConfirmationAsync"/> returns false (auto-resets).</summary>
    public bool FailNextEmailChangeConfirmation { get; set; }

    public Task<bool> SendEmailChangeConfirmationAsync(string toEmail, string displayName,
        string confirmationToken, CancellationToken ct = default)
    {
        SendEmailChangeConfirmationCallCount++;
        LastSendEmailChangeConfirmationCall = (toEmail, displayName, confirmationToken);
        if (FailNextEmailChangeConfirmation)
        {
            FailNextEmailChangeConfirmation = false;
            return Task.FromResult(false);
        }
        return Task.FromResult(true);
    }

    // ── Alerts (fire-and-forget) ──────────────────────────────────────────────
    public int SendNewUserAlertCallCount { get; private set; }

    public Task SendNewUserAlertAsync(string userEmail, string displayName,
        CancellationToken ct = default)
    {
        SendNewUserAlertCallCount++;
        return Task.CompletedTask;
    }

    public int SendNewTenantAlertCallCount { get; private set; }

    public Task SendNewTenantAlertAsync(string tenantSlug, string tenantDisplayName,
        string ownerEmail, CancellationToken ct = default)
    {
        SendNewTenantAlertCallCount++;
        return Task.CompletedTask;
    }

    // ── Lifecycle / admin / security (added 2026-06) ──────────────────────────
    public int SendTenantInactivityWarningCallCount { get; private set; }
    public int SendTenantSuspendedCallCount { get; private set; }
    public int SendTenantDeletingWarningCallCount { get; private set; }
    public int SendTenantDeletedCallCount { get; private set; }
    public int SendTenantReactivatedCallCount { get; private set; }
    public int SendTenantWelcomeCallCount { get; private set; }
    public int SendRoleChangedCallCount { get; private set; }
    public int SendMemberRemovedCallCount { get; private set; }
    public int SendOwnershipReceivedCallCount { get; private set; }
    public int SendOwnershipTransferredCallCount { get; private set; }
    public int SendQuotaLimitReachedCallCount { get; private set; }
    public int SendTierChangedCallCount { get; private set; }
    public int SendPasswordChangedCallCount { get; private set; }
    public int SendMfaChangedCallCount { get; private set; }
    public int SendEmailChangeRequestedOldAddressCallCount { get; private set; }
    public int SendEmailChangedCallCount { get; private set; }
    /// <summary>Recipient emails captured across the tenant/account notification sends, for assertions.</summary>
    public List<string> Recipients { get; } = new();

    public Task<bool> SendTenantInactivityWarningAsync(string toEmail, string tenantName, string loginUrl, int daysUntilSuspend, CancellationToken ct = default)
    { SendTenantInactivityWarningCallCount++; Recipients.Add(toEmail); return Task.FromResult(true); }
    public Task<bool> SendTenantSuspendedAsync(string toEmail, string tenantName, string reactivateUrl, int deleteAfterDays, CancellationToken ct = default)
    { SendTenantSuspendedCallCount++; Recipients.Add(toEmail); return Task.FromResult(true); }
    public Task<bool> SendTenantDeletingWarningAsync(string toEmail, string tenantName, string restoreUrl, int daysUntilDelete, CancellationToken ct = default)
    { SendTenantDeletingWarningCallCount++; Recipients.Add(toEmail); return Task.FromResult(true); }
    public Task<bool> SendTenantDeletedAsync(string toEmail, string tenantName, CancellationToken ct = default)
    { SendTenantDeletedCallCount++; Recipients.Add(toEmail); return Task.FromResult(true); }
    public Task<bool> SendTenantReactivatedAsync(string toEmail, string tenantName, string appUrl, CancellationToken ct = default)
    { SendTenantReactivatedCallCount++; Recipients.Add(toEmail); return Task.FromResult(true); }
    public Task<bool> SendTenantWelcomeAsync(string toEmail, string tenantName, string appUrl, CancellationToken ct = default)
    { SendTenantWelcomeCallCount++; Recipients.Add(toEmail); return Task.FromResult(true); }
    public Task<bool> SendRoleChangedAsync(string toEmail, string tenantName, string newRole, string appUrl, CancellationToken ct = default)
    { SendRoleChangedCallCount++; Recipients.Add(toEmail); return Task.FromResult(true); }
    public Task<bool> SendMemberRemovedAsync(string toEmail, string tenantName, CancellationToken ct = default)
    { SendMemberRemovedCallCount++; Recipients.Add(toEmail); return Task.FromResult(true); }
    public Task<bool> SendOwnershipReceivedAsync(string toEmail, string tenantName, string appUrl, CancellationToken ct = default)
    { SendOwnershipReceivedCallCount++; Recipients.Add(toEmail); return Task.FromResult(true); }
    public Task<bool> SendOwnershipTransferredAsync(string toEmail, string tenantName, string newOwnerEmail, CancellationToken ct = default)
    { SendOwnershipTransferredCallCount++; Recipients.Add(toEmail); return Task.FromResult(true); }
    public Task<bool> SendQuotaLimitReachedAsync(string toEmail, string tenantName, string resourceLabel, long limit, string manageUrl, CancellationToken ct = default)
    { SendQuotaLimitReachedCallCount++; Recipients.Add(toEmail); return Task.FromResult(true); }
    public Task<bool> SendTierChangedAsync(string toEmail, string tenantName, string newPlan, string appUrl, CancellationToken ct = default)
    { SendTierChangedCallCount++; Recipients.Add(toEmail); return Task.FromResult(true); }
    public Task<bool> SendPasswordChangedAsync(string toEmail, string displayName, CancellationToken ct = default)
    { SendPasswordChangedCallCount++; Recipients.Add(toEmail); return Task.FromResult(true); }
    public Task<bool> SendMfaChangedAsync(string toEmail, string displayName, bool enabled, CancellationToken ct = default)
    { SendMfaChangedCallCount++; Recipients.Add(toEmail); return Task.FromResult(true); }
    public Task<bool> SendEmailChangeRequestedOldAddressAsync(string toEmail, string displayName, string newEmail, CancellationToken ct = default)
    { SendEmailChangeRequestedOldAddressCallCount++; Recipients.Add(toEmail); return Task.FromResult(true); }
    public Task<bool> SendEmailChangedAsync(string toEmail, string displayName, string newEmail, CancellationToken ct = default)
    { SendEmailChangedCallCount++; Recipients.Add(toEmail); return Task.FromResult(true); }

    // ── Reset ─────────────────────────────────────────────────────────────────
    public void Reset()
    {
        SendEmailCallCount = 0;
        SendVerificationCallCount = 0;
        SendPasswordResetCallCount = 0;
        SendWelcomeCallCount = 0;
        SendInvitationCallCount = 0;
        SendLifecycleWarningCallCount = 0;
        SendDormancyNoticeCallCount = 0;
        SendEmailChangeConfirmationCallCount = 0;
        LastSendEmailChangeConfirmationCall = default;
        FailNextEmailChangeConfirmation = false;
        SendNewUserAlertCallCount = 0;
        SendNewTenantAlertCallCount = 0;
        SendTenantInactivityWarningCallCount = 0;
        SendTenantSuspendedCallCount = 0;
        SendTenantDeletingWarningCallCount = 0;
        SendTenantDeletedCallCount = 0;
        SendTenantReactivatedCallCount = 0;
        SendTenantWelcomeCallCount = 0;
        SendRoleChangedCallCount = 0;
        SendMemberRemovedCallCount = 0;
        SendOwnershipReceivedCallCount = 0;
        SendOwnershipTransferredCallCount = 0;
        SendQuotaLimitReachedCallCount = 0;
        SendTierChangedCallCount = 0;
        SendPasswordChangedCallCount = 0;
        SendMfaChangedCallCount = 0;
        SendEmailChangeRequestedOldAddressCallCount = 0;
        SendEmailChangedCallCount = 0;
        Recipients.Clear();
    }
}
