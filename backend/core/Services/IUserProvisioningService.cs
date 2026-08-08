using Npgsql;

namespace Api.Services;

/// <summary>
/// Resolves a control-plane user for an email address, creating the account —
/// Keycloak credential and <c>users</c> row — when nobody with that address exists.
/// </summary>
/// <remarks>
/// Two callers need this and used to own separate copies: accepting an invitation
/// and a site admin opening a workspace for a design partner. The invariants worth
/// having in one place are the awkward ones — the address is lower-cased so it
/// cannot collide with the case-sensitive unique index behind a case-insensitive
/// lookup, the insert tolerates a concurrent winner, and the identity provider is
/// only asked for an account once the caller's transaction is in a position to keep
/// it.
/// </remarks>
public interface IUserProvisioningService
{
    /// <summary>
    /// Returns the user id for <paramref name="email"/>, creating the account if needed.
    /// Joins the caller's connection and transaction so the <c>users</c> row commits or
    /// rolls back with whatever else the caller is writing.
    /// </summary>
    /// <param name="password">
    /// Credential for a newly created account. Pass null when the person is not choosing
    /// one now — a random credential is generated that nobody is told, and the caller is
    /// expected to send them through password setup or an identity provider. Supplying a
    /// password also makes an existing identity-provider account an error rather than
    /// something to reuse: that password would silently never take effect.
    /// </param>
    /// <exception cref="Api.Integrations.Keycloak.KeycloakAdminException">
    /// The identity provider refused to create the account.
    /// </exception>
    /// <exception cref="ArgumentException">The address is not a usable email address.</exception>
    Task<UserProvisioningResult> ResolveOrCreateAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction? transaction,
        string email,
        string? displayName = null,
        string? password = null,
        bool emailVerified = true,
        CancellationToken ct = default);
}

/// <param name="UserId">The existing or newly created control-plane user.</param>
/// <param name="Created">
/// True when this call created the account. Callers use it to decide whether the person
/// needs an introduction — a password-setup mail, a welcome — or is already established.
/// </param>
public readonly record struct UserProvisioningResult(Guid UserId, bool Created);
