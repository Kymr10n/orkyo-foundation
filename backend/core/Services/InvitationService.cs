using System.Security.Cryptography;
using System.Text;
using Api.Helpers;
using Api.Integrations.Keycloak;
using Api.Security.Quotas;
using Npgsql;

namespace Api.Services;

public sealed class InvitationService : IInvitationService
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly IEmailService _emailService;
    private readonly ITenantUserService _tenantUserService;
    private readonly IKeycloakAdminService _keycloakAdminService;
    private readonly ITenantSettingsService _settingsService;
    private readonly IQuotaEnforcer _quotaEnforcer;
    private readonly ILogger<InvitationService> _logger;

    public InvitationService(
        IDbConnectionFactory connectionFactory,
        IEmailService emailService,
        ITenantUserService tenantUserService,
        IKeycloakAdminService keycloakAdminService,
        ITenantSettingsService settingsService,
        IQuotaEnforcer quotaEnforcer,
        ILogger<InvitationService> logger)
    {
        _connectionFactory = connectionFactory;
        _emailService = emailService;
        _tenantUserService = tenantUserService;
        _keycloakAdminService = keycloakAdminService;
        _settingsService = settingsService;
        _quotaEnforcer = quotaEnforcer;
        _logger = logger;
    }

    public async Task<(Models.Invitation invitation, string token)?> InviteUserAsync(
        TenantContext tenant, Guid invitedBy, string email, Models.UserRole role, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateControlPlaneConnection();
        await conn.OpenAsync(ct);

        // Enforce seat limit via the product-specific IQuotaEnforcer.
        // SaaS: TierBasedQuotaEnforcer enforces tier limits.
        // Community: CommunityQuotaEnforcer is a no-op (unlimited).
        await using var countCmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM tenant_memberships WHERE tenant_id = @tenantId AND status = 'active'", conn);
        countCmd.Parameters.AddWithValue("tenantId", tenant.TenantId);
        var currentCount = Convert.ToInt32(await countCmd.ExecuteScalarAsync(ct));
        _quotaEnforcer.EnforceLimit(QuotaResourceTypes.ActiveSeats, currentCount);

        // Check if already a member
        await using var checkCmd = new NpgsqlCommand(@"
            SELECT COUNT(*) FROM tenant_memberships tm
            INNER JOIN users u ON tm.user_id = u.id
            WHERE u.email = @email AND tm.tenant_id = @tenantId", conn);
        checkCmd.Parameters.AddWithValue("email", email);
        checkCmd.Parameters.AddWithValue("tenantId", tenant.TenantId);
        if (Convert.ToInt32(await checkCmd.ExecuteScalarAsync(ct)) > 0)
        {
            _logger.LogWarning("Cannot invite {Email}: already a member", email);
            return null;
        }

        // If the user already exists globally, grant membership directly (no token needed)
        await using var checkUserCmd = new NpgsqlCommand("SELECT id FROM users WHERE email = @email", conn);
        checkUserCmd.Parameters.AddWithValue("email", email);
        var existingUserId = await checkUserCmd.ExecuteScalarAsync(ct) as Guid?;

        var org = ToOrg(tenant);

        if (existingUserId.HasValue)
        {
            await using var membershipCmd = new NpgsqlCommand(@"
                INSERT INTO tenant_memberships (user_id, tenant_id, role, status, invited_by, created_at, updated_at)
                VALUES (@userId, @tenantId, @role, 'active', @invitedBy, NOW(), NOW())", conn);
            membershipCmd.Parameters.AddWithValue("userId", existingUserId.Value);
            membershipCmd.Parameters.AddWithValue("tenantId", tenant.TenantId);
            membershipCmd.Parameters.AddWithValue("role", role.ToString().ToLowerInvariant());
            membershipCmd.Parameters.AddWithValue("invitedBy", invitedBy);
            await membershipCmd.ExecuteNonQueryAsync(ct);

            await _tenantUserService.CreateUserStubInTenantDatabaseAsync(org, existingUserId.Value, email, ct);
            _logger.LogInformation("Added existing user {Email} to tenant {TenantId} with role {Role}", email, tenant.TenantId, role);
            await _tenantUserService.RecordAuditEventAsync(org, "user.added_to_tenant", invitedBy, "user", existingUserId.Value.ToString(), new { email, role = role.ToString() });
            return null;
        }

        var token = GenerateSecureToken();
        var tokenHash = HashToken(token);

        var settings = await _settingsService.GetSettingsAsync();
        await using var insertCmd = new NpgsqlCommand(@"
            INSERT INTO invitations (email, role, invited_by, tenant_id, token_hash, expires_at, created_at, updated_at)
            VALUES (@email, @role, @invitedBy, @tenantId, @tokenHash, @expiresAt, NOW(), NOW())
            RETURNING id, email, role, invited_by, token_hash, expires_at, accepted_at, created_at", conn);
        insertCmd.Parameters.AddWithValue("email", email);
        insertCmd.Parameters.AddWithValue("role", role.ToString().ToLowerInvariant());
        insertCmd.Parameters.AddWithValue("invitedBy", invitedBy);
        insertCmd.Parameters.AddWithValue("tenantId", tenant.TenantId);
        insertCmd.Parameters.AddWithValue("tokenHash", tokenHash);
        insertCmd.Parameters.AddWithValue("expiresAt", DateTime.UtcNow.AddDays(settings.Invitation_ExpiryDays));

        await using var reader = await insertCmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        var invitation = MapInvitation(reader);
        await reader.CloseAsync();

        await _emailService.SendInvitationEmailAsync(email, token, invitation.ExpiresAt);
        await _tenantUserService.RecordAuditEventAsync(org, "user.invited", invitedBy, "invitation", invitation.Id.ToString(), new { email, role = role.ToString() });

        return (invitation, token);
    }

    public async Task<(string? email, DateTime? expiresAt, string? tenantName, string? error)> ValidateInvitationAsync(
        string token, CancellationToken ct = default)
    {
        var tokenHash = HashToken(token);

        await using var conn = _connectionFactory.CreateControlPlaneConnection();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(@"
            SELECT i.email, i.expires_at, i.accepted_at, t.display_name
            FROM invitations i INNER JOIN tenants t ON i.tenant_id = t.id
            WHERE i.token_hash = @tokenHash", conn);
        cmd.Parameters.AddWithValue("tokenHash", tokenHash);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return (null, null, null, "Invalid or expired invitation");

        var email = reader.GetString(0);
        var expiresAt = reader.GetDateTime(1);
        var acceptedAt = reader.IsDBNull(2) ? (DateTime?)null : reader.GetDateTime(2);
        var tenantName = reader.GetString(3);

        if (acceptedAt.HasValue) return (null, null, null, "Invitation has already been accepted");
        if (expiresAt < DateTime.UtcNow) return (null, null, null, "Invitation has expired");

        return (email, expiresAt, tenantName, null);
    }

    public async Task<(Models.User? user, string? error)> AcceptInvitationAsync(
        string token, string displayName, string password, CancellationToken ct = default)
    {
        var tokenHash = HashToken(token);

        await using var conn = _connectionFactory.CreateControlPlaneConnection();
        await conn.OpenAsync(ct);

        await using var findCmd = new NpgsqlCommand(@"
            SELECT i.id, i.email, i.role, i.tenant_id, i.expires_at, i.accepted_at, t.db_identifier, t.slug
            FROM invitations i INNER JOIN tenants t ON i.tenant_id = t.id
            WHERE i.token_hash = @tokenHash", conn);
        findCmd.Parameters.AddWithValue("tokenHash", tokenHash);

        await using var reader = await findCmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return (null, "Invalid or expired invitation");

        var invitationId = reader.GetGuid(0);
        var email = reader.GetString(1);
        var role = UserHelper.ParseUserRole(reader.GetString(2));
        var tenantId = reader.GetGuid(3);
        var expiresAt = reader.GetDateTime(4);
        var acceptedAt = reader.IsDBNull(5) ? (DateTime?)null : reader.GetDateTime(5);
        var dbIdentifier = reader.GetString(6);
        var tenantSlug = reader.GetString(7);
        await reader.CloseAsync();

        if (acceptedAt.HasValue) return (null, "Invitation has already been accepted");
        if (expiresAt < DateTime.UtcNow) return (null, "Invitation has expired");

        // Build the org context using the factory so each product routes correctly.
        // SaaS: CreateConnectionForDatabase routes to the per-tenant database.
        // Community: CreateConnectionForDatabase returns the single shared database.
        var orgCs = _connectionFactory.CreateConnectionForDatabase(dbIdentifier).ConnectionString;
        var org = new OrgContext { OrgId = tenantId, OrgSlug = tenantSlug, DbConnectionString = orgCs };

        await using var transaction = await conn.BeginTransactionAsync(ct);
        try
        {
            await using var checkCmd = new NpgsqlCommand("SELECT id FROM users WHERE email = @email", conn, transaction);
            checkCmd.Parameters.AddWithValue("email", email);
            var existingUserId = await checkCmd.ExecuteScalarAsync(ct) as Guid?;

            Guid userId;
            if (existingUserId.HasValue)
            {
                userId = existingUserId.Value;
            }
            else
            {
                try
                {
                    await _keycloakAdminService.CreateUserAsync(email, password, displayName, null, emailVerified: true);
                }
                catch (KeycloakAdminException ex)
                {
                    _logger.LogWarning(ex, "Failed to create Keycloak user for {Email}", email);
                    await transaction.RollbackAsync(ct);
                    return (null, ex.Message);
                }

                userId = Guid.NewGuid();
                await using var insertUserCmd = new NpgsqlCommand(@"
                    INSERT INTO users (id, email, display_name, status, created_at, updated_at)
                    VALUES (@id, @email, @displayName, 'active', NOW(), NOW())", conn, transaction);
                insertUserCmd.Parameters.AddWithValue("id", userId);
                insertUserCmd.Parameters.AddWithValue("email", email);
                insertUserCmd.Parameters.AddWithValue("displayName", displayName);
                await insertUserCmd.ExecuteNonQueryAsync(ct);
            }

            await using var membershipCmd = new NpgsqlCommand(@"
                INSERT INTO tenant_memberships (user_id, tenant_id, role, status, created_at, updated_at)
                VALUES (@userId, @tenantId, @role, 'active', NOW(), NOW())
                ON CONFLICT (user_id, tenant_id) DO UPDATE SET role = @role, status = 'active', updated_at = NOW()",
                conn, transaction);
            membershipCmd.Parameters.AddWithValue("userId", userId);
            membershipCmd.Parameters.AddWithValue("tenantId", tenantId);
            membershipCmd.Parameters.AddWithValue("role", role.ToString().ToLowerInvariant());
            await membershipCmd.ExecuteNonQueryAsync(ct);

            await using var acceptCmd = new NpgsqlCommand(
                "UPDATE invitations SET accepted_at = NOW() WHERE id = @id", conn, transaction);
            acceptCmd.Parameters.AddWithValue("id", invitationId);
            await acceptCmd.ExecuteNonQueryAsync(ct);

            await transaction.CommitAsync(ct);

            await _tenantUserService.CreateUserStubInTenantDatabaseAsync(org, userId, email, ct);

            var user = new Models.User
            {
                Id = userId,
                Email = email,
                DisplayName = displayName,
                Role = role,
                IsTenantAdmin = false,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            try { await _tenantUserService.RecordAuditEventAsync(org, "user.invitation_accepted", userId, "user", userId.ToString()); }
            catch { /* ignore audit failures */ }

            _logger.LogInformation("User {Email} accepted invitation and joined tenant {TenantId}", email, tenantId);
            return (user, null);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(ct);
            _logger.LogError(ex, "Error accepting invitation");
            return (null, "Failed to accept invitation");
        }
    }

    public async Task<List<Models.Invitation>> GetPendingInvitationsAsync(
        TenantContext tenant, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateControlPlaneConnection();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(@"
            SELECT id, email, role, invited_by, token_hash, expires_at, accepted_at, created_at
            FROM invitations
            WHERE tenant_id = @tenantId AND accepted_at IS NULL AND expires_at > NOW()
            ORDER BY created_at DESC", conn);
        cmd.Parameters.AddWithValue("tenantId", tenant.TenantId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var invitations = new List<Models.Invitation>();
        while (await reader.ReadAsync(ct)) invitations.Add(MapInvitation(reader));
        return invitations;
    }

    public async Task<bool> RevokeInvitationAsync(
        TenantContext tenant, Guid invitationId, Guid revokedBy, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateControlPlaneConnection();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(@"
            DELETE FROM invitations WHERE id = @invitationId AND tenant_id = @tenantId AND accepted_at IS NULL", conn);
        cmd.Parameters.AddWithValue("invitationId", invitationId);
        cmd.Parameters.AddWithValue("tenantId", tenant.TenantId);

        var rowsAffected = await cmd.ExecuteNonQueryAsync(ct);
        if (rowsAffected > 0)
            await _tenantUserService.RecordAuditEventAsync(ToOrg(tenant), "invitation.revoked", revokedBy, "invitation", invitationId.ToString());

        return rowsAffected > 0;
    }

    private static OrgContext ToOrg(TenantContext t) => new()
    {
        OrgId = t.TenantId,
        OrgSlug = t.TenantSlug,
        DbConnectionString = t.TenantDbConnectionString
    };

    private static Models.Invitation MapInvitation(NpgsqlDataReader reader) => new()
    {
        Id = reader.GetGuid(0),
        Email = reader.GetString(1),
        Role = UserHelper.ParseUserRole(reader.GetString(2)),
        InvitedBy = reader.GetGuid(3),
        TokenHash = reader.GetString(4),
        ExpiresAt = reader.GetDateTime(5),
        AcceptedAt = reader.IsDBNull(6) ? null : reader.GetDateTime(6),
        CreatedAt = reader.GetDateTime(7)
    };

    private static string GenerateSecureToken()
    {
        var bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").TrimEnd('=');
    }

    private static string HashToken(string token)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToBase64String(hash);
    }
}
