using Api.Endpoints;
using Api.Models;
using Npgsql;
using Orkyo.Shared;

namespace Api.Services;

public class SessionService : ISessionService
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SessionService> _logger;

    public SessionService(
        IDbConnectionFactory connectionFactory,
        IConfiguration configuration,
        ILogger<SessionService> logger)
    {
        _connectionFactory = connectionFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<SessionBootstrapResponse> BootstrapSessionAsync(string keycloakSub, string? email, string? displayName)
    {
        await using var db = _connectionFactory.CreateControlPlaneConnection();
        await db.OpenAsync();

        var existingIdentity = await FindIdentityByKeycloakSubAsync(db, keycloakSub);

        Guid userId;

        if (existingIdentity != null)
        {
            userId = existingIdentity.UserId;
            _logger.LogInformation("Found existing identity for Keycloak sub={Sub}, user_id={UserId}", keycloakSub, userId);
            await UpdateLastLoginAsync(db, userId);
        }
        else
        {
            _logger.LogInformation("First login for Keycloak sub={Sub}, email={Email}", keycloakSub, email);

            var existingUser = await FindUserByEmailAsync(db, email ?? "");

            if (existingUser != null)
            {
                userId = existingUser.Id;
                _logger.LogInformation("Linking Keycloak identity to existing user email={Email}, user_id={UserId}", email, userId);
            }
            else
            {
                userId = await CreateUserAsync(db, email ?? keycloakSub, displayName ?? email ?? "User");
                _logger.LogInformation("Created new user for Keycloak sub={Sub}, user_id={UserId}", keycloakSub, userId);
            }

            await CreateIdentityLinkAsync(db, userId, keycloakSub, email);
            await UpdateLastLoginAsync(db, userId);
        }

        var userInfo = await GetUserByIdInternalAsync(db, userId);
        if (userInfo == null)
            throw new InvalidOperationException($"User {userId} not found after bootstrap");

        var memberships = await GetTenantMembershipsAsync(db, userId);
        var requiredTosVersion = GetRequiredTosVersion();
        var tosRequired = false;

        if (!string.IsNullOrEmpty(requiredTosVersion))
            tosRequired = !await HasAcceptedTosInternalAsync(db, userId, requiredTosVersion);

        return new SessionBootstrapResponse
        {
            User = userInfo,
            TosRequired = tosRequired,
            RequiredTosVersion = requiredTosVersion,
            Tenants = memberships,
            SuggestedTenantSlug = memberships.FirstOrDefault(m => m.State == "active")?.Slug
        };
    }

    public async Task<SessionBootstrapResponse?> GetSessionByKeycloakSubAsync(string keycloakSub)
    {
        await using var db = _connectionFactory.CreateControlPlaneConnection();
        await db.OpenAsync();

        var identity = await FindIdentityByKeycloakSubAsync(db, keycloakSub);
        if (identity == null) return null;

        return await BuildSessionResponseAsync(db, identity.UserId);
    }

    public async Task<SessionBootstrapResponse?> GetSessionByUserIdAsync(Guid userId)
    {
        await using var db = _connectionFactory.CreateControlPlaneConnection();
        await db.OpenAsync();

        return await BuildSessionResponseAsync(db, userId);
    }

    public async Task<SessionBootstrapResponse?> BuildSessionResponseAsync(Guid userId)
        => await GetSessionByUserIdAsync(userId);

    private async Task<SessionBootstrapResponse?> BuildSessionResponseAsync(NpgsqlConnection db, Guid userId)
    {
        var userInfo = await GetUserByIdInternalAsync(db, userId);
        if (userInfo == null) return null;

        var memberships = await GetTenantMembershipsAsync(db, userId);
        var requiredTosVersion = GetRequiredTosVersion();
        var tosRequired = false;

        if (!string.IsNullOrEmpty(requiredTosVersion))
            tosRequired = !await HasAcceptedTosInternalAsync(db, userId, requiredTosVersion);

        return new SessionBootstrapResponse
        {
            User = userInfo,
            TosRequired = tosRequired,
            RequiredTosVersion = requiredTosVersion,
            Tenants = memberships,
            SuggestedTenantSlug = memberships.FirstOrDefault(m => m.State == "active")?.Slug
        };
    }

    public async Task MarkTourSeenAsync(Guid userId)
    {
        await using var db = _connectionFactory.CreateControlPlaneConnection();
        await db.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "UPDATE users SET has_seen_tour = true, updated_at = NOW() WHERE id = @id", db);
        cmd.Parameters.AddWithValue("id", userId);
        await cmd.ExecuteNonQueryAsync();
        _logger.LogInformation("Tour marked as seen for user {UserId}", userId);
    }

    public async Task UpdateDisplayNameAsync(Guid userId, string displayName)
    {
        await using var db = _connectionFactory.CreateControlPlaneConnection();
        await db.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "UPDATE users SET display_name = @name, updated_at = NOW() WHERE id = @id AND (display_name IS DISTINCT FROM @name)", db);
        cmd.Parameters.AddWithValue("name", displayName);
        cmd.Parameters.AddWithValue("id", userId);
        var rows = await cmd.ExecuteNonQueryAsync();
        if (rows > 0)
            _logger.LogInformation("Display name synced for user {UserId}", userId);
    }

    public string? GetRequiredTosVersion() => _configuration[ConfigKeys.TosRequiredVersion];

    public async Task<bool> HasAcceptedTosAsync(Guid userId, string requiredVersion)
    {
        await using var db = _connectionFactory.CreateControlPlaneConnection();
        await db.OpenAsync();
        return await HasAcceptedTosInternalAsync(db, userId, requiredVersion);
    }

    public async Task AcceptTosAsync(Guid userId, string tosVersion, string? ipAddress, string? userAgent)
    {
        await using var db = _connectionFactory.CreateControlPlaneConnection();
        await db.OpenAsync();

        await using var cmd = new NpgsqlCommand(@"
            INSERT INTO tos_acceptances (user_id, tos_version, accepted_at, accepted_ip, accepted_user_agent)
            VALUES (@userId, @version, NOW(), @ip, @userAgent)
            ON CONFLICT (user_id, tos_version) DO NOTHING
        ", db);
        cmd.Parameters.AddWithValue("userId", userId);
        cmd.Parameters.AddWithValue("version", tosVersion);
        cmd.Parameters.AddWithValue("ip", (object?)ipAddress ?? DBNull.Value);
        cmd.Parameters.AddWithValue("userAgent", (object?)userAgent ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync();
        _logger.LogInformation("User {UserId} accepted ToS version {Version}", userId, tosVersion);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task<UserIdentity?> FindIdentityByKeycloakSubAsync(NpgsqlConnection db, string keycloakSub)
    {
        await using var cmd = new NpgsqlCommand(@"
            SELECT id, user_id, provider, provider_subject, provider_email, created_at
            FROM user_identities
            WHERE provider = 'keycloak' AND provider_subject = @sub
        ", db);
        cmd.Parameters.AddWithValue("sub", keycloakSub);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;

        return new UserIdentity
        {
            Id = reader.GetGuid(0),
            UserId = reader.GetGuid(1),
            Provider = reader.GetString(2),
            ProviderSubject = reader.GetString(3),
            ProviderEmail = reader.IsDBNull(4) ? null : reader.GetString(4),
            CreatedAt = reader.GetDateTime(5)
        };
    }

    private async Task<User?> FindUserByEmailAsync(NpgsqlConnection db, string email)
    {
        if (string.IsNullOrEmpty(email)) return null;

        await using var cmd = new NpgsqlCommand(@"
            SELECT id, email, display_name, status, created_at, updated_at, last_login_at
            FROM users WHERE LOWER(email) = LOWER(@email)
        ", db);
        cmd.Parameters.AddWithValue("email", email);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;

        return new User
        {
            Id = reader.GetGuid(0),
            Email = reader.GetString(1),
            DisplayName = reader.GetString(2),
            Status = Enum.Parse<UserStatus>(reader.GetString(3), true),
            CreatedAt = reader.GetDateTime(4),
            UpdatedAt = reader.GetDateTime(5),
            LastLoginAt = reader.IsDBNull(6) ? null : reader.GetDateTime(6)
        };
    }

    private async Task<Guid> CreateUserAsync(NpgsqlConnection db, string email, string displayName)
    {
        await using var cmd = new NpgsqlCommand(@"
            INSERT INTO users (email, display_name, status, created_at, updated_at)
            VALUES (@email, @displayName, 'active', NOW(), NOW())
            RETURNING id
        ", db);
        cmd.Parameters.AddWithValue("email", email);
        cmd.Parameters.AddWithValue("displayName", displayName);

        return (Guid)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task CreateIdentityLinkAsync(NpgsqlConnection db, Guid userId, string keycloakSub, string? email)
    {
        await using var cmd = new NpgsqlCommand(@"
            INSERT INTO user_identities (user_id, provider, provider_subject, provider_email, created_at)
            VALUES (@userId, 'keycloak', @sub, @email, NOW())
            ON CONFLICT (provider, provider_subject) DO NOTHING
        ", db);
        cmd.Parameters.AddWithValue("userId", userId);
        cmd.Parameters.AddWithValue("sub", keycloakSub);
        cmd.Parameters.AddWithValue("email", (object?)email ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync();
    }

    private async Task UpdateLastLoginAsync(NpgsqlConnection db, Guid userId)
    {
        // Clears any lifecycle warning/dormancy state — a successful login is proof of activity.
        await using var cmd = new NpgsqlCommand(@"
            UPDATE users
            SET last_login_at = NOW(),
                updated_at = NOW(),
                lifecycle_status = NULL,
                lifecycle_warning_count = 0,
                lifecycle_last_warned_at = NULL,
                lifecycle_dormant_since = NULL,
                lifecycle_confirm_token = NULL
            WHERE id = @id
        ", db);
        cmd.Parameters.AddWithValue("id", userId);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<UserInfo?> GetUserByIdInternalAsync(NpgsqlConnection db, Guid userId)
    {
        await using var cmd = new NpgsqlCommand(@"
            SELECT u.id, u.email, u.display_name, u.created_at, u.last_login_at, u.has_seen_tour,
                   (SELECT provider_subject FROM user_identities WHERE user_id = u.id AND provider = 'keycloak' LIMIT 1) as keycloak_id
            FROM users u
            WHERE u.id = @id
        ", db);
        cmd.Parameters.AddWithValue("id", userId);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;

        return new UserInfo
        {
            Id = reader.GetGuid(0),
            Email = reader.GetString(1),
            DisplayName = reader.GetString(2),
            CreatedAt = reader.GetDateTime(3),
            LastLoginAt = reader.IsDBNull(4) ? null : reader.GetDateTime(4),
            HasSeenTour = reader.GetBoolean(5),
            KeycloakId = reader.IsDBNull(6) ? null : reader.GetString(6)
        };
    }

    private async Task<List<TenantMembershipInfo>> GetTenantMembershipsAsync(NpgsqlConnection db, Guid userId)
    {
        await using var cmd = new NpgsqlCommand(@"
            SELECT t.id, t.slug, t.display_name, tm.role, t.status,
                   t.owner_user_id, t.tier, t.suspension_reason, t.suspended_at
            FROM tenant_memberships tm
            JOIN tenants t ON t.id = tm.tenant_id
            WHERE tm.user_id = @userId AND tm.status = 'active'
            ORDER BY t.display_name
        ", db);
        cmd.Parameters.AddWithValue("userId", userId);

        var memberships = new List<TenantMembershipInfo>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var ownerUserId = reader.IsDBNull(5) ? (Guid?)null : reader.GetGuid(5);
            var status = reader.GetString(4);
            var suspensionReason = reader.IsDBNull(7) ? null : reader.GetString(7);
            var suspendedAt = reader.IsDBNull(8) ? (DateTime?)null : reader.GetDateTime(8);
            var isOwner = ownerUserId == userId;
            var role = reader.GetString(3);

            memberships.Add(new TenantMembershipInfo
            {
                TenantId = reader.GetGuid(0),
                Slug = reader.GetString(1),
                DisplayName = reader.GetString(2),
                Role = role,
                State = status,
                IsOwner = isOwner,
                Tier = ((ServiceTier)reader.GetInt32(6)).ToString(),
                SuspensionReason = suspensionReason,
                SuspendedAt = suspendedAt,
                CanReactivate = SuspensionPolicy.CanReactivate(status, suspensionReason, role, isOwner)
            });
        }

        return memberships;
    }

    private async Task<bool> HasAcceptedTosInternalAsync(NpgsqlConnection db, Guid userId, string requiredVersion)
    {
        await using var cmd = new NpgsqlCommand(@"
            SELECT 1 FROM tos_acceptances WHERE user_id = @userId AND tos_version = @version LIMIT 1
        ", db);
        cmd.Parameters.AddWithValue("userId", userId);
        cmd.Parameters.AddWithValue("version", requiredVersion);

        return await cmd.ExecuteScalarAsync() != null;
    }
}
