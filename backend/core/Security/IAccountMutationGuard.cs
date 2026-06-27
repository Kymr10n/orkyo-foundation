using Api.Helpers;

namespace Api.Security;

/// <summary>
/// Gate for self-service account mutations (password, email, profile, MFA) on the
/// caller's own identity. Foundation domain code asks whether the current identity is
/// allowed to change its own credentials without knowing about editions or demo accounts.
///
/// The default permits everything (Community and the foundation fallback). Products may
/// register an override AFTER <c>AddFoundationServices</c> — SaaS locks the shared public
/// demo identity so an anonymous visitor cannot rewrite the credential the demo-login flow
/// depends on.
/// </summary>
public interface IAccountMutationGuard
{
    /// <summary>
    /// Throws <see cref="AccountLockedException"/> (mapped to <c>403</c>) when the current
    /// identity is not allowed to change its own account credentials/profile.
    /// </summary>
    void EnsureCanMutateOwnAccount(ICurrentPrincipal principal);

    /// <summary>
    /// Whether the current identity's account is read-only. Pure query for surfacing the
    /// state in the UI; the security boundary is <see cref="EnsureCanMutateOwnAccount"/>.
    /// </summary>
    bool IsAccountLocked(ICurrentPrincipal principal);
}

/// <summary>
/// Default implementation that allows every account mutation. Used by Community (no shared
/// demo identity) and as the foundation fallback so foundation builds standalone.
/// </summary>
public sealed class AllowAllAccountMutationGuard : IAccountMutationGuard
{
    public void EnsureCanMutateOwnAccount(ICurrentPrincipal principal) { }

    public bool IsAccountLocked(ICurrentPrincipal principal) => false;
}
