using Api.Security;

namespace Orkyo.Foundation.Tests.Security;

/// <summary>
/// The foundation default <see cref="AllowAllAccountMutationGuard"/> must never block account
/// mutations — single-tenant Community has no shared/locked identity. Products (SaaS) register an
/// override; that locking behavior is tested in the SaaS suite.
/// </summary>
public class AccountMutationGuardTests
{
    private static ICurrentPrincipal Principal(string email)
    {
        var mock = new Mock<ICurrentPrincipal>();
        mock.SetupGet(p => p.Email).Returns(email);
        return mock.Object;
    }

    [Fact]
    public void DefaultGuard_NeverReportsLocked()
    {
        var guard = new AllowAllAccountMutationGuard();

        guard.IsAccountLocked(Principal("anyone@example.com")).Should().BeFalse();
    }

    [Fact]
    public void DefaultGuard_NeverThrows()
    {
        var guard = new AllowAllAccountMutationGuard();

        var act = () => guard.EnsureCanMutateOwnAccount(Principal("anyone@example.com"));

        act.Should().NotThrow();
    }
}
