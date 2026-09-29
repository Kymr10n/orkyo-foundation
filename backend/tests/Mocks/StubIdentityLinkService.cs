using Api.Constants;
using Api.Security;

namespace Orkyo.Foundation.Tests.Mocks;

/// <summary>
/// Programmable stub for <see cref="IIdentityLinkService"/>.
///
/// The real implementation talks to Keycloak, so the test host cannot use it. A bare
/// <c>Mock.Of</c> returned a null <see cref="IdentityLinkResult"/> and made the whole
/// /api/session/bootstrap route untestable — the endpoint faulted before it reached any
/// of its own branches. This stub returns a failure by default and lets a test set
/// <see cref="LinkResult"/> to drive the success path.
/// </summary>
public sealed class StubIdentityLinkService : IIdentityLinkService
{
    /// <summary>What <see cref="LinkIdentityAsync"/> returns. Failure unless a test sets it.</summary>
    public IdentityLinkResult LinkResult { get; set; } =
        IdentityLinkResult.Failed("No identity link configured", ApiErrorCodes.Auth.IdentityNotLinked);

    /// <summary>The token the endpoint passed to <see cref="LinkIdentityAsync"/>.</summary>
    public ExternalIdentityToken? LastToken { get; private set; }

    public Task<IdentityLinkResult> LinkIdentityAsync(
        ExternalIdentityToken token, CancellationToken ct = default)
    {
        LastToken = token;
        return Task.FromResult(LinkResult);
    }

    public Task<PrincipalContext?> FindByExternalIdentityAsync(
        AuthProvider provider, string externalSubject, CancellationToken ct = default) =>
        Task.FromResult<PrincipalContext?>(null);

    /// <summary>
    /// What <see cref="GetUserTenantRoleAsync"/> returns. Admin by default, matching the role
    /// the test host gives every request; a test sets <see cref="TenantRole.None"/> to reach a
    /// removed-member refusal (the calendar feed) and restores it afterwards.
    /// </summary>
    public TenantRole TenantRole { get; set; } = TenantRole.Admin;

    public Task<TenantRole> GetUserTenantRoleAsync(
        Guid userId, Guid tenantId, CancellationToken ct = default) =>
        Task.FromResult(TenantRole);
}
