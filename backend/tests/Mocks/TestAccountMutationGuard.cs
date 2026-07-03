using Api.Helpers;
using Api.Security;

namespace Orkyo.Foundation.Tests.Mocks;

/// <summary>
/// Toggleable test double for <see cref="IAccountMutationGuard"/>. Registered as a singleton and
/// exposed on the factory so a test can flip <see cref="Locked"/> to exercise the account-locked
/// code paths (session privacy, self-service mutation lock) without depending on the harness's
/// email→principal resolution. Defaults to unlocked, matching the foundation allow-all behavior so
/// every other test is unaffected. The demo-account → locked *detection* is covered separately by
/// the SaaS <c>DemoAccountLockTests</c>.
/// </summary>
public sealed class TestAccountMutationGuard : IAccountMutationGuard
{
    public bool Locked { get; set; }

    public bool IsAccountLocked(ICurrentPrincipal principal) => Locked;

    public void EnsureCanMutateOwnAccount(ICurrentPrincipal principal)
    {
        if (Locked)
            throw new AccountLockedException("This is a shared demo account (test).");
    }
}
