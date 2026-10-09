using Api.Services;

namespace Orkyo.Foundation.Tests.Mocks;

/// <summary>
/// Trackable mock for <see cref="IEmailService"/> used in integration tests that
/// need to assert email sends without starting an SMTP server.
/// </summary>
public class MockEmailService : IEmailService
{
    // Broadcasts dispatch concurrently (Parallel.ForEachAsync), so every record takes the lock.
    private readonly object _lock = new();
    private readonly List<(string Method, string To)> _calls = new();

    /// <summary>Every send so far, in order: the <see cref="IEmailService"/> method name and the recipient.</summary>
    public IReadOnlyList<(string Method, string To)> Calls
    {
        get { lock (_lock) return _calls.ToList(); }
    }

    /// <summary>How many times <paramref name="method"/> (an <see cref="IEmailService"/> method name) was called.</summary>
    public int CallCount(string method) => Calls.Count(c => c.Method == method);

    /// <summary>The recipients of every send so far, in order.</summary>
    public IReadOnlyList<string> Recipients => Calls.Select(c => c.To).ToList();

    /// <summary>The confirm token last mailed to each recipient.</summary>
    public Dictionary<string, string> LifecycleWarningTokens { get; } = new();

    /// <summary>While set, every lifecycle warning send returns false (an SMTP outage).</summary>
    public bool FailLifecycleWarnings { get; set; }

    /// <summary>While set, every dormancy notice send returns false (an SMTP outage).</summary>
    public bool FailDormancyNotices { get; set; }

    /// <summary>When set, the next <see cref="SendEmailChangeConfirmationAsync"/> returns false (auto-resets).</summary>
    public bool FailNextEmailChangeConfirmation { get; set; }

    /// <summary>While set, <see cref="SendEmailAsync"/> throws, as a mail server that refuses the connection does.</summary>
    public bool ThrowOnSendEmail { get; set; }

    public (string toEmail, string displayName, string token) LastSendEmailChangeConfirmationCall { get; private set; }

    public string? LastEmailChangedDisplayName { get; private set; }

    private Task<bool> Record(string method, string to, bool result = true)
    {
        lock (_lock) _calls.Add((method, to));
        return Task.FromResult(result);
    }

    public Task<bool> SendEmailAsync(string toEmail, string toName, string subject,
        string htmlBody, string textBody, CancellationToken ct = default)
        => ThrowOnSendEmail
            ? throw new InvalidOperationException("SMTP connection refused")
            : Record(nameof(SendEmailAsync), toEmail);

    public Task<bool> SendWelcomeEmailAsync(string toEmail, string displayName, CancellationToken ct = default)
        => Record(nameof(SendWelcomeEmailAsync), toEmail);

    public Task<bool> SendInvitationEmailAsync(string toEmail, string token, DateTime expiresAt, CancellationToken ct = default)
        => Record(nameof(SendInvitationEmailAsync), toEmail);

    public Task<bool> SendLifecycleWarningEmailAsync(string toEmail, string displayName,
        string confirmToken, int warningNumber, CancellationToken ct = default)
    {
        lock (_lock) LifecycleWarningTokens[toEmail] = confirmToken;
        return Record(nameof(SendLifecycleWarningEmailAsync), toEmail, !FailLifecycleWarnings);
    }

    public Task<bool> SendDormancyNoticeEmailAsync(string toEmail, string displayName, CancellationToken ct = default)
        => Record(nameof(SendDormancyNoticeEmailAsync), toEmail, !FailDormancyNotices);

    public Task<bool> SendEmailChangeConfirmationAsync(string toEmail, string displayName,
        string confirmationToken, CancellationToken ct = default)
    {
        lock (_lock) LastSendEmailChangeConfirmationCall = (toEmail, displayName, confirmationToken);
        var fail = FailNextEmailChangeConfirmation;
        FailNextEmailChangeConfirmation = false;
        return Record(nameof(SendEmailChangeConfirmationAsync), toEmail, !fail);
    }

    public Task SendNewUserAlertAsync(string userEmail, string displayName, CancellationToken ct = default)
        => Record(nameof(SendNewUserAlertAsync), userEmail);

    public Task SendNewTenantAlertAsync(string tenantSlug, string tenantDisplayName,
        string ownerEmail, CancellationToken ct = default)
        => Record(nameof(SendNewTenantAlertAsync), ownerEmail);

    public Task<bool> SendTenantInactivityWarningAsync(string toEmail, string tenantName, string loginUrl, int daysUntilSuspend, CancellationToken ct = default)
        => Record(nameof(SendTenantInactivityWarningAsync), toEmail);
    public Task<bool> SendTenantSuspendedAsync(string toEmail, string tenantName, string reactivateUrl, int deleteAfterDays, CancellationToken ct = default)
        => Record(nameof(SendTenantSuspendedAsync), toEmail);
    public Task<bool> SendTenantDeletingWarningAsync(string toEmail, string tenantName, string restoreUrl, int daysUntilDelete, CancellationToken ct = default)
        => Record(nameof(SendTenantDeletingWarningAsync), toEmail);
    public Task<bool> SendTenantDeletedAsync(string toEmail, string tenantName, CancellationToken ct = default)
        => Record(nameof(SendTenantDeletedAsync), toEmail);
    public Task<bool> SendTenantWelcomeAsync(string toEmail, string tenantName, string appUrl, CancellationToken ct = default)
        => Record(nameof(SendTenantWelcomeAsync), toEmail);
    public Task<bool> SendRoleChangedAsync(string toEmail, string tenantName, string newRole, string appUrl, CancellationToken ct = default)
        => Record(nameof(SendRoleChangedAsync), toEmail);
    public Task<bool> SendMemberRemovedAsync(string toEmail, string tenantName, CancellationToken ct = default)
        => Record(nameof(SendMemberRemovedAsync), toEmail);
    public Task<bool> SendOwnershipReceivedAsync(string toEmail, string tenantName, string appUrl, CancellationToken ct = default)
        => Record(nameof(SendOwnershipReceivedAsync), toEmail);
    public Task<bool> SendOwnershipTransferredAsync(string toEmail, string tenantName, string newOwnerEmail, CancellationToken ct = default)
        => Record(nameof(SendOwnershipTransferredAsync), toEmail);
    public Task<bool> SendQuotaLimitReachedAsync(string toEmail, string tenantName, string resourceLabel, long limit, string manageUrl, CancellationToken ct = default)
        => Record(nameof(SendQuotaLimitReachedAsync), toEmail);
    public Task<bool> SendTierChangedAsync(string toEmail, string tenantName, string newPlan, string appUrl, CancellationToken ct = default)
        => Record(nameof(SendTierChangedAsync), toEmail);
    public Task<bool> SendPasswordChangedAsync(string toEmail, string displayName, CancellationToken ct = default)
        => Record(nameof(SendPasswordChangedAsync), toEmail);
    public Task<bool> SendMfaChangedAsync(string toEmail, string displayName, bool enabled, CancellationToken ct = default)
        => Record(nameof(SendMfaChangedAsync), toEmail);
    public Task<bool> SendPasskeyRemovedAsync(string toEmail, string displayName, CancellationToken ct = default)
        => Record(nameof(SendPasskeyRemovedAsync), toEmail);
    public Task<bool> SendEmailChangeRequestedOldAddressAsync(string toEmail, string displayName, string newEmail, CancellationToken ct = default)
        => Record(nameof(SendEmailChangeRequestedOldAddressAsync), toEmail);

    public Task<bool> SendEmailChangedAsync(string toEmail, string displayName, string newEmail, CancellationToken ct = default)
    {
        lock (_lock) LastEmailChangedDisplayName = displayName;
        return Record(nameof(SendEmailChangedAsync), toEmail);
    }

    public Task<bool> SendAnnouncementEmailAsync(string toEmail, string displayName, string title, string body,
        bool isImportant, Guid unsubscribeToken, CancellationToken ct = default)
        => Record(nameof(SendAnnouncementEmailAsync), toEmail);

    public void Reset()
    {
        lock (_lock)
        {
            _calls.Clear();
            LifecycleWarningTokens.Clear();
        }
        FailLifecycleWarnings = false;
        FailDormancyNotices = false;
        FailNextEmailChangeConfirmation = false;
        ThrowOnSendEmail = false;
        LastSendEmailChangeConfirmationCall = default;
        LastEmailChangedDisplayName = null;
    }
}
