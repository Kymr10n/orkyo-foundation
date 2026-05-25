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
    }
}
