using Api.Constants;
using Api.Integrations.Keycloak;
using Api.Repositories;
using Api.Services.BffSession;

namespace Api.Services;

public enum AccountDeletionOutcome
{
    Deleted,
    /// <summary>No user row for the caller.</summary>
    NotFound,
    /// <summary>The typed email does not match the account's email.</summary>
    EmailMismatch,
    /// <summary>The caller owns at least one organization; <see cref="AccountDeletionResult.Organizations"/> names them.</summary>
    OwnsOrganizations,
    /// <summary>The caller is the only active admin of at least one organization; <see cref="AccountDeletionResult.Organizations"/> names them.</summary>
    LastAdmin,
}

public sealed record AccountDeletionResult(AccountDeletionOutcome Outcome, IReadOnlyList<string> Organizations)
{
    public static readonly AccountDeletionResult Deleted = new(AccountDeletionOutcome.Deleted, []);
    public static readonly AccountDeletionResult NotFound = new(AccountDeletionOutcome.NotFound, []);
    public static readonly AccountDeletionResult EmailMismatch = new(AccountDeletionOutcome.EmailMismatch, []);
}

/// <summary>
/// Self-service erasure (GDPR Art. 17). The same rows go as in the lifecycle purge, through
/// <see cref="UserDataPurger"/>; what differs is the refusals in front of it. An owner or a last
/// active admin is refused, because deleting them would strand the organization: the owner must
/// delete it or hand it over, the admin must promote someone, exactly as leaving it requires.
/// </summary>
public interface IAccountDeletionService
{
    Task<AccountDeletionResult> DeleteOwnAccountAsync(Guid userId, string confirmEmail, CancellationToken ct = default);
}

public sealed class AccountDeletionService : IAccountDeletionService
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly IPlatformUserRepository _users;
    private readonly IKeycloakAdminService _keycloak;
    private readonly IBffSessionStore _bffSessions;
    private readonly IAdminAuditService _audit;
    private readonly ILogger<AccountDeletionService> _logger;

    public AccountDeletionService(
        IDbConnectionFactory connectionFactory,
        IPlatformUserRepository users,
        IKeycloakAdminService keycloak,
        IBffSessionStore bffSessions,
        IAdminAuditService audit,
        ILogger<AccountDeletionService> logger)
    {
        _connectionFactory = connectionFactory;
        _users = users;
        _keycloak = keycloak;
        _bffSessions = bffSessions;
        _audit = audit;
        _logger = logger;
    }

    public async Task<AccountDeletionResult> DeleteOwnAccountAsync(Guid userId, string confirmEmail, CancellationToken ct = default)
    {
        var row = await _users.GetEmailAndDisplayNameAsync(userId, ct);
        if (row is null)
            return AccountDeletionResult.NotFound;
        if (!string.Equals(row.Value.Email.Trim(), confirmEmail.Trim(), StringComparison.OrdinalIgnoreCase))
            return AccountDeletionResult.EmailMismatch;

        var repository = new UserPurgeRepository(_connectionFactory);

        var owned = await repository.ListOwnedTenantNamesAsync(userId, ct);
        if (owned.Count > 0)
            return new AccountDeletionResult(AccountDeletionOutcome.OwnsOrganizations, owned);

        var lastAdminOf = await repository.ListTenantNamesWhereLastActiveAdminAsync(userId, ct);
        if (lastAdminOf.Count > 0)
            return new AccountDeletionResult(AccountDeletionOutcome.LastAdmin, lastAdminOf);

        var keycloakSubject = await _users.GetKeycloakSubjectAsync(userId, ct);

        // Recorded before the purge: the actor FK is ON DELETE SET NULL, so the row survives with
        // the user id kept in target_id.
        await _audit.RecordEventAsync(userId, SecurityAuditActions.AccountDeleted, "user", userId.ToString(), ct: ct);

        // Rows first, identity provider second, same order as the lifecycle purge. A Keycloak
        // failure after the rows are gone leaves an account with nothing behind it, which the
        // bootstrap refuses; the reverse order would leave the person's data under a login that
        // no longer exists.
        await new UserDataPurger(_connectionFactory, _logger).PurgeAsync(userId, ct);

        if (!string.IsNullOrEmpty(keycloakSubject))
        {
            try
            {
                await _keycloak.DeleteUserAsync(keycloakSubject, ct);
            }
            catch (KeycloakAdminException ex)
            {
                _logger.LogError(ex, "Self-service delete: application data of user {UserId} is erased but the Keycloak account {Subject} remains", userId, keycloakSubject);
            }
        }

        // The BFF sessions authenticate from their stored tokens, not from Keycloak, so every
        // other device stays signed in until they are removed here.
        await _bffSessions.RemoveAllForUserAsync(userId.ToString(), ct);

        _logger.LogWarning("Self-service delete: user {UserId} permanently deleted", userId);
        return AccountDeletionResult.Deleted;
    }
}
