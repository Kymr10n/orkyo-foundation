using Api.Constants;
using Api.Models;
using Api.Security.Features;
using Npgsql;
using Orkyo.Shared;

namespace Api.Services;

public class SessionService : ISessionService
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly IConfiguration _configuration;
    private readonly ITenantPlanInfoProvider _planInfoProvider;
    private readonly ITenantEntitlementProvider _entitlementProvider;
    private readonly ITenantMembershipEnricher _membershipEnricher;
    private readonly ITenantSettingsService _tenantSettingsService;
    private readonly ILogger<SessionService> _logger;

    public SessionService(
        IDbConnectionFactory connectionFactory,
        IConfiguration configuration,
        ITenantPlanInfoProvider planInfoProvider,
        ITenantEntitlementProvider entitlementProvider,
        ITenantMembershipEnricher membershipEnricher,
        ITenantSettingsService tenantSettingsService,
        ILogger<SessionService> logger)
    {
        _connectionFactory = connectionFactory;
        _configuration = configuration;
        _planInfoProvider = planInfoProvider;
        _entitlementProvider = entitlementProvider;
        _membershipEnricher = membershipEnricher;
        _tenantSettingsService = tenantSettingsService;
        _logger = logger;
    }

    public async Task<SessionBootstrapResponse?> GetSessionByUserIdAsync(Guid userId, CancellationToken ct = default)
    {
        await using var db = _connectionFactory.CreateControlPlaneConnection();
        await db.OpenAsync(ct);

        return await BuildSessionResponseAsync(db, userId, ct);
    }

    public async Task<SessionBootstrapResponse?> BuildSessionResponseAsync(Guid userId, CancellationToken ct = default)
        => await GetSessionByUserIdAsync(userId, ct);

    private async Task<SessionBootstrapResponse?> BuildSessionResponseAsync(NpgsqlConnection db, Guid userId, CancellationToken ct = default)
    {
        var userInfo = await GetUserByIdInternalAsync(db, userId, ct);
        if (userInfo == null) return null;

        var memberships = await GetTenantMembershipsAsync(db, userId, ct);
        var requiredTosVersion = GetRequiredTosVersion();
        var tosRequired = false;

        if (!string.IsNullOrEmpty(requiredTosVersion))
            tosRequired = !await HasAcceptedTosInternalAsync(db, userId, requiredTosVersion, ct);

        return new SessionBootstrapResponse
        {
            User = userInfo,
            TosRequired = tosRequired,
            RequiredTosVersion = requiredTosVersion,
            TosText = await GetTosTextIfRequiredAsync(tosRequired, ct),
            Tenants = memberships,
            SuggestedTenantSlug = memberships.FirstOrDefault(m => m.State == MembershipStatusConstants.Active)?.Slug
        };
    }

    /// <summary>
    /// Resolve the site-scoped ToS text, only when the acceptance page will actually be shown.
    /// Session endpoints run pre-tenant (SkipTenantResolution), where GetSettingsAsync resolves
    /// site scope — same pattern as the password-policy read in SecurityEndpoints.
    /// </summary>
    private async Task<string?> GetTosTextIfRequiredAsync(bool tosRequired, CancellationToken ct)
    {
        if (!tosRequired) return null;
        var settings = await _tenantSettingsService.GetSettingsAsync(ct);
        return settings.Tos_Text;
    }

    public async Task MarkTourSeenAsync(Guid userId, CancellationToken ct = default)
    {
        await using var db = _connectionFactory.CreateControlPlaneConnection();
        await db.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(
            "UPDATE users SET has_seen_tour = true, updated_at = NOW() WHERE id = @id", db);
        cmd.Parameters.AddWithValue("id", userId);
        await cmd.ExecuteNonQueryAsync(ct);
        _logger.LogInformation("Tour marked as seen for user {UserId}", userId);
    }

    public async Task UpdateDisplayNameAsync(Guid userId, string displayName, CancellationToken ct = default)
    {
        await using var db = _connectionFactory.CreateControlPlaneConnection();
        await db.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(
            "UPDATE users SET display_name = @name, updated_at = NOW() WHERE id = @id AND (display_name IS DISTINCT FROM @name)", db);
        cmd.Parameters.AddWithValue("name", displayName);
        cmd.Parameters.AddWithValue("id", userId);
        var rows = await cmd.ExecuteNonQueryAsync(ct);
        if (rows > 0)
            _logger.LogInformation("Display name synced for user {UserId}", userId);
    }

    public string? GetRequiredTosVersion() => _configuration[ConfigKeys.TosRequiredVersion];

    public async Task AcceptTosAsync(Guid userId, string tosVersion, string? ipAddress, string? userAgent, CancellationToken ct = default)
    {
        await using var db = _connectionFactory.CreateControlPlaneConnection();
        await db.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(@"
            INSERT INTO tos_acceptances (user_id, tos_version, accepted_at, accepted_ip, accepted_user_agent)
            VALUES (@userId, @version, NOW(), @ip, @userAgent)
            ON CONFLICT (user_id, tos_version) DO NOTHING
        ", db);
        cmd.Parameters.AddWithValue("userId", userId);
        cmd.Parameters.AddWithValue("version", tosVersion);
        cmd.Parameters.AddWithValue("ip", (object?)ipAddress ?? DBNull.Value);
        cmd.Parameters.AddWithValue("userAgent", (object?)userAgent ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(ct);
        _logger.LogInformation("User {UserId} accepted ToS version {Version}", userId, tosVersion);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task<UserInfo?> GetUserByIdInternalAsync(NpgsqlConnection db, Guid userId, CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(@"
            SELECT u.id, u.email, u.display_name, u.created_at, u.last_login_at, u.has_seen_tour,
                   (SELECT provider_subject FROM user_identities WHERE user_id = u.id AND provider = 'keycloak' LIMIT 1) as keycloak_id
            FROM users u
            WHERE u.id = @id
        ", db);
        cmd.Parameters.AddWithValue("id", userId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;

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

    private async Task<List<TenantMembershipInfo>> GetTenantMembershipsAsync(NpgsqlConnection db, Guid userId, CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(@"
            SELECT t.id, t.slug, t.display_name, tm.role, t.status,
                   t.owner_user_id
            FROM tenant_memberships tm
            JOIN tenants t ON t.id = tm.tenant_id
            WHERE tm.user_id = @userId AND tm.status = 'active'
            ORDER BY t.display_name
        ", db);
        cmd.Parameters.AddWithValue("userId", userId);

        var rows = new List<(Guid TenantId, string Slug, string DisplayName, string Role, string State, bool IsOwner)>();
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                var ownerUserId = reader.IsDBNull(5) ? (Guid?)null : reader.GetGuid(5);
                rows.Add((
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    ownerUserId == userId));
            }
        }

        // The plan and its entitlements are commercial concepts owned by the edition, not
        // foundation. Entitlements ship with the session so clients present features from the
        // server's own plan → feature mapping instead of re-deriving it from the plan code.
        var tenantIds = rows.Select(r => r.TenantId).ToList();
        var planInfo = await _planInfoProvider.GetPlanInfoAsync(tenantIds, ct);
        var entitlements = await _entitlementProvider.GetEntitlementsAsync(tenantIds, ct);

        var memberships = rows.Select(r => new TenantMembershipInfo
        {
            TenantId = r.TenantId,
            Slug = r.Slug,
            DisplayName = r.DisplayName,
            Role = r.Role,
            State = r.State,
            IsOwner = r.IsOwner,
            IsTenantAdmin = r.Role == RoleConstants.Admin,
            // Machine plan CODE, never the display label — the SPA compares this against literal
            // lowercase codes, so "Enterprise" would silently degrade every gate to Free.
            Tier = planInfo.TryGetValue(r.TenantId, out var info) ? info.PlanCode : SinglePlanInfoProvider.PlanCode,
            Entitlements = entitlements.TryGetValue(r.TenantId, out var ent) ? ent : null,
        }).ToList();

        // Suspension metadata is a commercial/edition concept — SaaS fills
        // CanReactivate/SuspensionReason here; other editions pass through.
        return (await _membershipEnricher.EnrichAsync(memberships, ct)).ToList();
    }

    private async Task<bool> HasAcceptedTosInternalAsync(NpgsqlConnection db, Guid userId, string requiredVersion, CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(@"
            SELECT 1 FROM tos_acceptances WHERE user_id = @userId AND tos_version = @version LIMIT 1
        ", db);
        cmd.Parameters.AddWithValue("userId", userId);
        cmd.Parameters.AddWithValue("version", requiredVersion);

        return await cmd.ExecuteScalarAsync(ct) != null;
    }
}
