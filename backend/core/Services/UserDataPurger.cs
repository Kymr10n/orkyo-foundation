using Api.Repositories;

namespace Api.Services;

/// <summary>
/// Erases one user's application data everywhere it lives. Used by the GDPR lifecycle purge
/// (<see cref="UserLifecycleService"/>) and the site-admin permanent delete
/// (<see cref="UserManagementService"/>), so both paths remove the same rows.
///
/// A user's data is spread over two layers:
/// <list type="bullet">
///   <item><description>Every tenant database the user is a member of holds a <c>users</c> mirror
///   row plus per-user rows that carry no foreign key to it: calendar feed tokens, assistant
///   conversations and allowances, token and daily usage. Rows that do reference the mirror
///   cascade (preferences, request templates, site memberships) or null out (feedback, people,
///   audit actor) when the mirror goes.</description></item>
///   <item><description>The control-plane <c>users</c> row, whose cascades remove identities,
///   sessions, ToS acceptances, tenant memberships and sent invitations.</description></item>
/// </list>
///
/// Tenant databases are purged first and the control-plane row last. A tenant database that
/// cannot be reached throws, so the control-plane row stays, the user remains in the purge
/// queue, and the next run retries. Deleting the control-plane row first would also delete the
/// memberships that say which tenant databases still hold the person's data.
///
/// Community runs control plane and tenant in one database and its
/// <see cref="IDbConnectionFactory"/> maps every identifier to it; there the tenant step deletes
/// the only <c>users</c> row and the control-plane step is a no-op.
/// </summary>
public sealed class UserDataPurger
{
    private readonly UserPurgeRepository _repository;
    private readonly ILogger _logger;

    public UserDataPurger(IDbConnectionFactory connectionFactory, ILogger logger)
    {
        _repository = new UserPurgeRepository(connectionFactory);
        _logger = logger;
    }

    /// <summary>
    /// Removes the user from every tenant database they belong to, then from the control plane.
    /// Throws when a tenant database cannot be purged; the control-plane row is then left in place.
    /// </summary>
    public async Task PurgeAsync(Guid userId, CancellationToken ct = default)
    {
        var tenantDatabases = await _repository.ListTenantDatabasesAsync(userId, ct);
        foreach (var dbIdentifier in tenantDatabases)
        {
            ct.ThrowIfCancellationRequested();
            await _repository.PurgeTenantDatabaseAsync(dbIdentifier, userId, ct);
            _logger.LogInformation("Purged user {UserId} from tenant database {Database}", userId, dbIdentifier);
        }

        await _repository.DeleteControlPlaneUserAsync(userId, ct);
        _logger.LogInformation(
            "Purged user {UserId} from {TenantCount} tenant database(s) and the control plane",
            userId, tenantDatabases.Count);
    }
}
