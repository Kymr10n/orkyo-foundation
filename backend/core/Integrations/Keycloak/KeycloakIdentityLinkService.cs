using Api.Configuration;
using Api.Constants;
using Api.Helpers;
using Api.Security;
using Api.Services;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Api.Integrations.Keycloak;

/// <summary>
/// Keycloak implementation of IIdentityLinkService.
/// Handles linking Keycloak identities to internal users in the control plane database.
/// </summary>
public sealed class KeycloakIdentityLinkService : IIdentityLinkService
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly IEmailService _emailService;
    private readonly IdentityProvisioningOptions _identityProvisioning;
    private readonly ILogger<KeycloakIdentityLinkService> _logger;
    private readonly IBackgroundDispatcher? _background;

    public KeycloakIdentityLinkService(
        IDbConnectionFactory connectionFactory,
        IEmailService emailService,
        IOptions<IdentityProvisioningOptions> identityProvisioning,
        ILogger<KeycloakIdentityLinkService> logger,
        IBackgroundDispatcher? background = null)
    {
        _connectionFactory = connectionFactory;
        _emailService = emailService;
        _identityProvisioning = identityProvisioning.Value;
        _logger = logger;
        _background = background;
    }

    public async Task<PrincipalContext?> FindByExternalIdentityAsync(AuthProvider provider, string externalSubject, CancellationToken ct = default)
    {
        if (provider != AuthProvider.Keycloak)
        {
            _logger.LogWarning("FindByExternalIdentityAsync called with unsupported provider: {Provider}", provider);
            return null;
        }

        await using var conn = _connectionFactory.CreateControlPlaneConnection();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(@"
            SELECT u.id, u.email, u.display_name
            FROM users u
            INNER JOIN user_identities ui ON u.id = ui.user_id
            WHERE ui.provider = 'keycloak'
              AND ui.provider_subject = @subject
              AND u.status = 'active'", conn);
        cmd.Parameters.AddWithValue("subject", externalSubject);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;

        return new PrincipalContext
        {
            UserId = reader.GetGuid("id"),
            Email = reader.GetString("email"),
            DisplayName = reader.GetNullableString("display_name"),
            AuthProvider = AuthProvider.Keycloak,
            ExternalSubject = externalSubject
        };
    }

    public async Task<IdentityLinkResult> LinkIdentityAsync(ExternalIdentityToken token, CancellationToken ct = default)
    {
        if (token.Provider != AuthProvider.Keycloak)
        {
            return IdentityLinkResult.Failed($"Unsupported provider: {token.Provider}", ApiErrorCodes.Auth.InvalidToken);
        }

        if (string.IsNullOrEmpty(token.Subject))
        {
            return IdentityLinkResult.Failed("Token subject is required", ApiErrorCodes.Auth.InvalidToken);
        }

        await using var conn = _connectionFactory.CreateControlPlaneConnection();
        await conn.OpenAsync(ct);

        // First, check if this Keycloak identity is already linked
        var existingPrincipal = await FindByExternalIdentityAsync(AuthProvider.Keycloak, token.Subject, ct);
        if (existingPrincipal != null)
        {
            _logger.LogDebug("Identity already linked: {Subject} -> {UserId}", token.Subject, existingPrincipal.UserId);
            await UpdateLastLoginAsync(conn, existingPrincipal.UserId, ct);
            return IdentityLinkResult.Linked(
                existingPrincipal.UserId,
                existingPrincipal.Email,
                existingPrincipal.DisplayName,
                isNew: false);
        }

        // Check if there's a user with this email (from invitation)
        await using var findByEmailCmd = new NpgsqlCommand(@"
            SELECT id, email, display_name, status
            FROM users
            WHERE LOWER(email) = LOWER(@email)", conn);
        findByEmailCmd.Parameters.AddWithValue("email", token.Email ?? string.Empty);

        await using var emailReader = await findByEmailCmd.ExecuteReaderAsync(ct);
        if (await emailReader.ReadAsync(ct))
        {
            var userId = emailReader.GetGuid("id");
            var email = emailReader.GetString("email");
            var displayName = emailReader.GetNullableString("display_name");
            var status = emailReader.GetString("status");

            await emailReader.CloseAsync();

            // Matching by address hands this identity an existing account, so the address must
            // be proven. An unverified claim would let anyone who can register (or federate)
            // the victim's address in Keycloak take over the invited or existing account.
            if (!token.EmailVerified)
            {
                _logger.LogWarning(
                    "Refused to link Keycloak identity {Subject} to user {UserId}: email not verified",
                    token.Subject, userId);
                return IdentityLinkResult.Failed(
                    "Verify your email address before signing in.",
                    ApiErrorCodes.Auth.EmailNotVerified);
            }

            if (status != UserStatusConstants.Active)
            {
                return IdentityLinkResult.Failed(
                    "User account is not active. Please contact your administrator.",
                    ApiErrorCodes.Auth.AccountInactive);
            }

            // Link the Keycloak identity to the existing user
            await CreateIdentityLinkAsync(conn, userId, token.Subject, token.Email, ct);
            await UpdateLastLoginAsync(conn, userId, ct);

            _logger.LogInformation(
                "Linked Keycloak identity {Subject} to existing user {UserId} ({Email})",
                token.Subject, userId, email);

            return IdentityLinkResult.Linked(userId, email, displayName, isNew: false);
        }

        // Close reader before starting transaction
        await emailReader.CloseAsync();

        // INTERIM (early access): with self-registration off, an identity nobody
        // invited gets no account. Everything above still runs — an already-linked
        // identity and the invitation email-match both resolve normally — so this
        // only closes the "signed in, therefore exists" door. Restoring self-serve
        // sign-up is a matter of setting AllowSelfRegistration back to true; the
        // auto-create path below is untouched and still the permissive behaviour.
        if (!_identityProvisioning.AllowSelfRegistration)
        {
            _logger.LogInformation(
                "Rejected unknown Keycloak identity {Subject} ({Email}): access is by invitation only",
                token.Subject, token.Email);

            return IdentityLinkResult.Failed(
                AccessMessages.InvitationOnly,
                ApiErrorCodes.Auth.NotInvited);
        }

        // No existing user - auto-create for self-registration
        // User registered via Keycloak and verified email, create internal user
        _logger.LogInformation(
            "Creating new user for Keycloak identity {Subject} with email {Email}",
            token.Subject, token.Email);

        var newUser = await CreateUserFromKeycloakAsync(conn, token, ct);
        if (newUser == null)
        {
            return IdentityLinkResult.Failed("Failed to create user account", ApiErrorCodes.Auth.IdentityNotLinked);
        }

        // Best-effort: after the response when DI composed this service, inline for a
        // hand-composed instance.
        var (alertEmail, alertName) = (newUser.Email, newUser.DisplayName ?? newUser.Email);
        if (_background is null)
            await _emailService.SendNewUserAlertAsync(alertEmail, alertName, CancellationToken.None);
        else
            _background.Dispatch<IEmailService>("new-user alert",
                (mail, mailCt) => mail.SendNewUserAlertAsync(alertEmail, alertName, mailCt));

        return IdentityLinkResult.Linked(newUser.UserId, newUser.Email, newUser.DisplayName, isNew: true);
    }

    private async Task<PrincipalContext?> CreateUserFromKeycloakAsync(NpgsqlConnection conn, ExternalIdentityToken token, CancellationToken ct = default)
    {
        await using var transaction = await conn.BeginTransactionAsync(ct);

        try
        {
            var userId = Guid.NewGuid();
            var email = UserProvisioningService.Normalize(token.Email ?? string.Empty);
            var displayName = token.DisplayName ?? token.Email?.Split('@')[0] ?? "User";

            // Create user in control plane. ON CONFLICT + re-read rather than a bare INSERT:
            // two sign-ins for the same new address can race here, and the loser used to die
            // on the unique index. This mirrors UserProvisioningService, which is where the
            // idiom is spelled out.
            await using var createUserCmd = new NpgsqlCommand(@"
                INSERT INTO users (id, email, display_name, status, last_login_at, created_at, updated_at)
                VALUES (@id, @email, @displayName, 'active', NOW(), NOW(), NOW())
                ON CONFLICT (email) DO NOTHING
                RETURNING id",
                conn, transaction);
            createUserCmd.Parameters.AddWithValue("id", userId);
            createUserCmd.Parameters.AddWithValue("email", email);
            createUserCmd.Parameters.AddWithValue("displayName", displayName);

            if (await createUserCmd.ExecuteScalarAsync(ct) is Guid insertedId)
            {
                userId = insertedId;
            }
            else
            {
                // A concurrent sign-in created the same address between our lookup and our
                // write. Theirs is as good as ours — link this identity to their row.
                await using var findCmd = new NpgsqlCommand(
                    "SELECT id FROM users WHERE LOWER(email) = @email", conn, transaction);
                findCmd.Parameters.AddWithValue("email", email);
                userId = await findCmd.ExecuteScalarAsync(ct) is Guid winner
                    ? winner
                    : throw new InvalidOperationException(
                        $"users row for {email} vanished between insert conflict and re-read");
            }

            await using var linkCmd = new NpgsqlCommand(@"
                INSERT INTO user_identities (id, user_id, provider, provider_subject, provider_email, created_at)
                VALUES (@id, @userId, 'keycloak', @subject, @email, NOW())
                ON CONFLICT (provider, provider_subject) DO NOTHING",
                conn, transaction);
            linkCmd.Parameters.AddWithValue("id", Guid.NewGuid());
            linkCmd.Parameters.AddWithValue("userId", userId);
            linkCmd.Parameters.AddWithValue("subject", token.Subject ?? string.Empty);
            linkCmd.Parameters.AddWithValue("email", token.Email ?? string.Empty);
            await linkCmd.ExecuteNonQueryAsync(ct);

            await transaction.CommitAsync(ct);

            _logger.LogInformation(
                "Created new user {UserId} ({Email}) with Keycloak identity {Subject}",
                userId, token.Email, token.Subject);

            return new PrincipalContext
            {
                UserId = userId,
                Email = email,
                DisplayName = displayName,
                AuthProvider = AuthProvider.Keycloak,
                ExternalSubject = token.Subject ?? string.Empty
            };
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(ct);
            _logger.LogError(ex, "Failed to create user for Keycloak identity {Subject}", token.Subject);
            return null;
        }
    }

    public async Task<TenantRole> GetUserTenantRoleAsync(Guid userId, Guid tenantId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateControlPlaneConnection();
        await conn.OpenAsync(ct);

        // A disabled user keeps their membership rows (the lifecycle deactivation and the
        // admin "deactivate" both set users.status only), so the user's own status is part
        // of the answer: anything that authorizes by this role — a calendar feed token,
        // above all — stops working the moment the account is disabled.
        await using var cmd = new NpgsqlCommand(@"
            SELECT tm.role
            FROM tenant_memberships tm
            JOIN users u ON u.id = tm.user_id
            WHERE tm.user_id = @userId
              AND tm.tenant_id = @tenantId
              AND tm.status = 'active'
              AND u.status <> @disabled",
            conn);
        cmd.Parameters.AddWithValue("userId", userId);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("disabled", UserStatusConstants.Disabled);

        var roleString = await cmd.ExecuteScalarAsync(ct) as string;
        return RoleConstants.ParseRoleString(roleString);
    }

    private async Task CreateIdentityLinkAsync(NpgsqlConnection conn, Guid userId, string subject, string? email, CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(@"
            INSERT INTO user_identities (user_id, provider, provider_subject, provider_email, created_at)
            VALUES (@userId, 'keycloak', @subject, @email, NOW())
            ON CONFLICT (provider, provider_subject) DO NOTHING", conn);
        cmd.Parameters.AddWithValue("userId", userId);
        cmd.Parameters.AddWithValue("subject", subject);
        cmd.Parameters.AddWithValue("email", (object?)email ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task UpdateLastLoginAsync(NpgsqlConnection conn, Guid userId, CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(@"
            UPDATE users SET last_login_at = NOW(), updated_at = NOW() WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", userId);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
