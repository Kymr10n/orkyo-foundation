namespace Api.Helpers;

/// <summary>
/// Thrown when self-service account changes (password, email, profile, MFA) are blocked
/// for the current identity — e.g. a shared public demo account whose credentials must not
/// be mutable by anonymous visitors. Mapped to <c>403</c> by <c>AppExceptionHandler</c>.
/// </summary>
public sealed class AccountLockedException : Exception
{
    public AccountLockedException(string message) : base(message) { }
}
