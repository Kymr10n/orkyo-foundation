using Api.Constants;
using Api.Helpers;
using Api.Security;
using Api.Services;
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
    private readonly ILogger<KeycloakIdentityLinkService> _logger;

    public KeycloakIdentityLinkService(
        IDbConnectionFactory connectionFactory,
        IEmailService emailService,
        ILogger<KeycloakIdentityLinkService> logger)
    {
        _connectionFactory = connectionFactory;
        _emailService = emailService;
        _logger = logger;
    }

    public async Task<PrincipalContext?> FindByExternalIdentityAsync(AuthProvider provider, string externalSubject)
    {
        if (provider != AuthProvider.Keycloak)
        {
            _logger.LogWarning("FindByExternalIdentityAsync called with unsupported provider: {Provider}", provider);
            return null;
        }

        await using var conn = _connectionFactory.CreateControlPlaneConnection();
        await conn.OpenAsync();

        await using var cmd = UserIdentityLinkCommandFactory.CreateSelectActiveUserByExternalIdentityCommand(
            conn, externalSubject);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;

        return new PrincipalContext
        {
            UserId = reader.GetGuid(0),
            Email = reader.GetString(1),
            DisplayName = reader.IsDBNull(2) ? null : reader.GetString(2),
            AuthProvider = AuthProvider.Keycloak,
            ExternalSubject = externalSubject
        };
    }

    public async Task<IdentityLinkResult> LinkIdentityAsync(ExternalIdentityToken token)
    {
        if (token.Provider != AuthProvider.Keycloak)
        {
            return IdentityLinkResult.Failed($"Unsupported provider: {token.Provider}", ProblemDetailsHelper.AuthCodes.InvalidToken);
        }

        if (string.IsNullOrEmpty(token.Subject))
        {
            return IdentityLinkResult.Failed("Token subject is required", ProblemDetailsHelper.AuthCodes.InvalidToken);
        }

        await using var conn = _connectionFactory.CreateControlPlaneConnection();
        await conn.OpenAsync();

        // First, check if this Keycloak identity is already linked
        var existingPrincipal = await FindByExternalIdentityAsync(AuthProvider.Keycloak, token.Subject);
        if (existingPrincipal != null)
        {
            _logger.LogDebug("Identity already linked: {Subject} -> {UserId}", token.Subject, existingPrincipal.UserId);
            await UpdateLastLoginAsync(conn, existingPrincipal.UserId);
            return IdentityLinkResult.Linked(
                existingPrincipal.UserId,
                existingPrincipal.Email,
                existingPrincipal.DisplayName,
                isNew: false);
        }

        // Check if there's a user with this email (from invitation)
        await using var findByEmailCmd = KeycloakUserProvisioningCommandFactory.CreateSelectUserByLowerEmailCommand(
            conn, token.Email ?? string.Empty);

        await using var emailReader = await findByEmailCmd.ExecuteReaderAsync();
        if (await emailReader.ReadAsync())
        {
            var userId = emailReader.GetGuid(0);
            var email = emailReader.GetString(1);
            var displayName = emailReader.IsDBNull(2) ? null : emailReader.GetString(2);
            var status = emailReader.GetString(3);

            await emailReader.CloseAsync();

            if (status != "active")
            {
                return IdentityLinkResult.Failed(
                    "User account is not active. Please contact your administrator.",
                    ProblemDetailsHelper.AuthCodes.AccountInactive);
            }

            // Link the Keycloak identity to the existing user
            await CreateIdentityLinkAsync(conn, userId, token.Subject, token.Email);
            await UpdateLastLoginAsync(conn, userId);

            _logger.LogInformation(
                "Linked Keycloak identity {Subject} to existing user {UserId} ({Email})",
                token.Subject, userId, email);

            return IdentityLinkResult.Linked(userId, email, displayName, isNew: false);
        }

        // Close reader before starting transaction
        await emailReader.CloseAsync();

        // No existing user - auto-create for self-registration
        // User registered via Keycloak and verified email, create internal user
        _logger.LogInformation(
            "Creating new user for Keycloak identity {Subject} with email {Email}",
            token.Subject, token.Email);

        var newUser = await CreateUserFromKeycloakAsync(conn, token);
        if (newUser == null)
        {
            return IdentityLinkResult.Failed("Failed to create user account", ProblemDetailsHelper.AuthCodes.IdentityNotLinked);
        }

        _ = _emailService.SendNewUserAlertAsync(newUser.Email, newUser.DisplayName ?? newUser.Email);

        return IdentityLinkResult.Linked(newUser.UserId, newUser.Email, newUser.DisplayName, isNew: true);
    }

    private async Task<PrincipalContext?> CreateUserFromKeycloakAsync(NpgsqlConnection conn, ExternalIdentityToken token)
    {
        await using var transaction = await conn.BeginTransactionAsync();

        try
        {
            var userId = Guid.NewGuid();
            var displayName = token.DisplayName ?? token.Email?.Split('@')[0] ?? "User";

            // Create user in control plane
            await using var createUserCmd = KeycloakUserProvisioningCommandFactory.CreateInsertNewActiveUserCommand(
                conn, transaction, userId, token.Email ?? string.Empty, displayName);

            await createUserCmd.ExecuteNonQueryAsync();

            // Create identity link in user_identities table
            await using var linkCmd = KeycloakUserProvisioningCommandFactory.CreateInsertIdentityLinkWithIdCommand(
                conn, transaction, Guid.NewGuid(), userId, token.Subject ?? string.Empty, token.Email ?? string.Empty);

            await linkCmd.ExecuteNonQueryAsync();

            await transaction.CommitAsync();

            _logger.LogInformation(
                "Created new user {UserId} ({Email}) with Keycloak identity {Subject}",
                userId, token.Email, token.Subject);

            return new PrincipalContext
            {
                UserId = userId,
                Email = token.Email ?? string.Empty,
                DisplayName = displayName,
                AuthProvider = AuthProvider.Keycloak,
                ExternalSubject = token.Subject ?? string.Empty
            };
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Failed to create user for Keycloak identity {Subject}", token.Subject);
            return null;
        }
    }

    public async Task<IReadOnlyList<TenantMembership>> GetUserMembershipsAsync(Guid userId)
    {
        await using var conn = _connectionFactory.CreateControlPlaneConnection();
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(@"
            SELECT 
                t.id,
                t.slug,
                t.display_name,
                tm.role,
                tm.status
            FROM tenant_memberships tm
            INNER JOIN tenants t ON tm.tenant_id = t.id
            WHERE tm.user_id = @userId
              AND tm.status = 'active'
              AND t.status = 'active'
            ORDER BY t.display_name",
            conn);
        cmd.Parameters.AddWithValue("userId", userId);

        var memberships = new List<TenantMembership>();
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var roleString = reader.GetString(3);
            var role = RoleConstants.ParseRoleString(roleString);

            memberships.Add(new TenantMembership
            {
                TenantId = reader.GetGuid(0),
                TenantSlug = reader.GetString(1),
                TenantName = reader.GetString(2),
                Role = role,
                Status = reader.GetString(4)
            });
        }

        return memberships;
    }

    public async Task<TenantRole> GetUserTenantRoleAsync(Guid userId, Guid tenantId)
    {
        await using var conn = _connectionFactory.CreateControlPlaneConnection();
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(@"
            SELECT role 
            FROM tenant_memberships 
            WHERE user_id = @userId 
              AND tenant_id = @tenantId 
              AND status = 'active'",
            conn);
        cmd.Parameters.AddWithValue("userId", userId);
        cmd.Parameters.AddWithValue("tenantId", tenantId);

        var roleString = await cmd.ExecuteScalarAsync() as string;
        return RoleConstants.ParseRoleString(roleString);
    }

    private async Task CreateIdentityLinkAsync(NpgsqlConnection conn, Guid userId, string subject, string? email)
    {
        await using var cmd = UserIdentityLinkCommandFactory.CreateInsertIdentityLinkCommand(
            conn, userId, subject, email);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task UpdateLastLoginAsync(NpgsqlConnection conn, Guid userId)
    {
        await using var cmd = UserIdentityLinkCommandFactory.CreateUpdateLastLoginCommand(conn, userId);
        await cmd.ExecuteNonQueryAsync();
    }
}
