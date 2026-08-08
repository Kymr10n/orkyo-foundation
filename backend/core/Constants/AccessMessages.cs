namespace Api.Constants;

/// <summary>
/// User-facing sentences for the access model. They are constants because the same
/// refusal is delivered through several channels — an identity-link failure, a 403
/// on the public account endpoint, a refused workspace creation — and a visitor who
/// meets two of them should not be told two different things.
/// </summary>
/// <remarks>
/// INTERIM (early access): the invitation-only wording belongs to the design-partner
/// programme. When self-serve sign-up reopens these strings go with it — see
/// <c>orkyo-saas/docs/early-access.md</c>.
/// </remarks>
public static class AccessMessages
{
    /// <summary>Someone authenticated successfully but nobody has invited them.</summary>
    public const string InvitationOnly = "Access to Orkyo is currently by invitation only.";

    /// <summary>A signed-in user tried to create their own workspace.</summary>
    public const string SelfServiceWorkspaceClosed =
        "Workspace creation is currently by invitation only. Apply for access at orkyo.com/design-partners.";
}
