namespace Api.Configuration;

/// <summary>
/// Governs whether an unknown person can provision themselves into the product.
/// </summary>
/// <remarks>
/// <para>
/// This is deliberately NOT an environment variable. An unset or mistyped env var
/// would silently fall back to the permissive default and reopen self sign-up in
/// production without anyone noticing; each edition states its stance in its own
/// composition root instead, where it is reviewable in the diff.
/// </para>
/// <para>
/// Community leaves the default (<c>true</c>): a self-hosted instance owns its own
/// front door, and its JIT provisioning middleware depends on identities being
/// created on first sign-in.
/// </para>
/// <para>
/// SaaS sets it to <c>false</c> for the duration of the design-partner early-access
/// programme. Self-serve sign-up remains the goal — flipping this single value back
/// to <c>true</c> restores identity auto-creation, the create-account endpoint, and
/// workspace self-creation together.
/// </para>
/// </remarks>
public sealed class IdentityProvisioningOptions
{
    /// <summary>
    /// When false, only people who are already known to the control plane — invited,
    /// or provisioned by a site admin — can obtain an account. Unknown identities are
    /// rejected at link time rather than being created on the fly.
    /// </summary>
    public bool AllowSelfRegistration { get; set; } = true;
}
