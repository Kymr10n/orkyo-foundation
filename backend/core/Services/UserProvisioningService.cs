using System.Net.Mail;
using System.Security.Cryptography;
using Api.Integrations.Keycloak;
using Npgsql;

namespace Api.Services;

/// <inheritdoc />
public sealed class UserProvisioningService : IUserProvisioningService
{
    private readonly IKeycloakAdminService _keycloakAdminService;
    private readonly ILogger<UserProvisioningService> _logger;

    public UserProvisioningService(
        IKeycloakAdminService keycloakAdminService,
        ILogger<UserProvisioningService> logger)
    {
        _keycloakAdminService = keycloakAdminService;
        _logger = logger;
    }

    public async Task<UserProvisioningResult> ResolveOrCreateAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction? transaction,
        string email,
        string? displayName = null,
        string? password = null,
        bool emailVerified = true,
        CancellationToken ct = default)
    {
        var normalized = Normalize(email);

        var existing = await FindByEmailAsync(conn, transaction, normalized, ct);
        if (existing.HasValue) return new UserProvisioningResult(existing.Value, Created: false);

        // Ask the identity provider first: if it refuses, the caller's transaction has
        // written nothing yet and can roll back cleanly.
        //
        // A 409 is only survivable when we generated the credential ourselves. If the
        // caller supplied one, the person is choosing a password right now and Keycloak
        // has just declined to set it — carrying on would hand them an account they
        // cannot sign into.
        var callerSuppliedPassword = password is not null;
        try
        {
            await _keycloakAdminService.CreateUserAsync(
                normalized,
                password ?? GenerateUnusedPassword(),
                firstName: displayName,
                lastName: null,
                emailVerified: emailVerified,
                ct: ct);
        }
        catch (KeycloakAdminException ex) when (ex.StatusCode == 409 && !callerSuppliedPassword)
        {
            _logger.LogInformation(
                "Keycloak already has an account for {Email}; reusing it — the owner sets " +
                "their own credential through password setup", normalized);
        }

        var userId = Guid.NewGuid();
        await using var insertCmd = new NpgsqlCommand(@"
            INSERT INTO users (id, email, display_name, status, created_at, updated_at)
            VALUES (@id, @email, @displayName, 'active', NOW(), NOW())
            ON CONFLICT (email) DO NOTHING
            RETURNING id", conn, transaction);
        insertCmd.Parameters.AddWithValue("id", userId);
        insertCmd.Parameters.AddWithValue("email", normalized);
        // display_name is NOT NULL; the address is a recognisable placeholder until the
        // first sign-in replaces it with the name from the token.
        insertCmd.Parameters.AddWithValue("displayName", (object?)displayName ?? normalized);

        if (await insertCmd.ExecuteScalarAsync(ct) is Guid inserted)
        {
            return new UserProvisioningResult(inserted, Created: true);
        }

        // ON CONFLICT swallowed the insert: a concurrent request created the same
        // address between our lookup and our write. Theirs is as good as ours.
        var winner = await FindByEmailAsync(conn, transaction, normalized, ct)
            ?? throw new InvalidOperationException(
                $"users row for {normalized} vanished between insert conflict and re-read");

        return new UserProvisioningResult(winner, Created: false);
    }

    private static async Task<Guid?> FindByEmailAsync(
        NpgsqlConnection conn, NpgsqlTransaction? transaction, string normalizedEmail, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT id FROM users WHERE LOWER(email) = @email", conn, transaction);
        cmd.Parameters.AddWithValue("email", normalizedEmail);
        return await cmd.ExecuteScalarAsync(ct) as Guid?;
    }

    /// <summary>
    /// Lower-cased so the case-insensitive lookup and the case-sensitive unique index
    /// agree; without this, two spellings of one mailbox can both be inserted.
    /// </summary>
    private static string Normalize(string email)
    {
        var trimmed = email?.Trim() ?? string.Empty;
        if (!MailAddress.TryCreate(trimmed, out _))
        {
            throw new ArgumentException($"'{email}' is not a valid email address", nameof(email));
        }
        return trimmed.ToLowerInvariant();
    }

    /// <summary>
    /// A credential nobody receives, for accounts whose owner will set their own via
    /// password-setup mail or an identity provider. Hex plus one character from each
    /// class Keycloak policies commonly require, so adding a policy later cannot
    /// invalidate it.
    /// </summary>
    private static string GenerateUnusedPassword() =>
        RandomNumberGenerator.GetHexString(48) + "aA1!";
}
