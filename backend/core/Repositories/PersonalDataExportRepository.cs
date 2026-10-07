using System.Text.Json;
using Api.Helpers;
using Api.Models.Account;
using Api.Services;
using Npgsql;

namespace Api.Repositories;

/// <summary>
/// The reads behind a person's data export. Control-plane scoped like
/// <see cref="UserPurgeRepository"/>, and for the same reason: the export must reach every
/// organization database the person belongs to, so it resolves those databases from the
/// control plane and opens each one by identifier. What the purge erases, this returns.
/// </summary>
public sealed class PersonalDataExportRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public PersonalDataExportRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<PersonalProfile?> GetProfileAsync(Guid userId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateControlPlaneConnection();
        return await conn.QuerySingleOrDefaultAsync(@"
            SELECT id, email, display_name, status, created_at, last_login_at, pending_email, lifecycle_status
            FROM users WHERE id = @id",
            p => p.AddWithValue("id", userId),
            r => new PersonalProfile
            {
                Id = r.GetGuid("id"),
                Email = r.GetString("email"),
                DisplayName = r.GetString("display_name"),
                Status = r.GetString("status"),
                CreatedAt = r.GetDateTime("created_at"),
                LastLoginAt = r.GetNullableDateTime("last_login_at"),
                PendingEmail = r.GetNullableString("pending_email"),
                LifecycleStatus = r.GetNullableString("lifecycle_status"),
            }, ct);
    }

    public async Task<List<PersonalTermsAcceptance>> GetTermsAcceptancesAsync(Guid userId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateControlPlaneConnection();
        return await conn.QueryListAsync(@"
            SELECT tos_version, accepted_at, accepted_ip, accepted_user_agent
            FROM tos_acceptances WHERE user_id = @id ORDER BY accepted_at",
            p => p.AddWithValue("id", userId),
            r => new PersonalTermsAcceptance
            {
                TosVersion = r.GetString("tos_version"),
                AcceptedAt = r.GetDateTime("accepted_at"),
                AcceptedIp = r.GetNullableString("accepted_ip"),
                AcceptedUserAgent = r.GetNullableString("accepted_user_agent"),
            }, ct);
    }

    public async Task<List<PersonalSession>> GetSessionsAsync(Guid userId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateControlPlaneConnection();
        return await conn.QueryListAsync(@"
            SELECT ip_address, browser, operating_system, device_type, created_at, last_seen_at
            FROM user_sessions WHERE user_id = @id ORDER BY created_at",
            p => p.AddWithValue("id", userId),
            r => new PersonalSession
            {
                IpAddress = r.GetNullableString("ip_address"),
                Browser = r.GetNullableString("browser"),
                OperatingSystem = r.GetNullableString("operating_system"),
                DeviceType = r.GetNullableString("device_type"),
                CreatedAt = r.GetDateTime("created_at"),
                LastSeenAt = r.GetDateTime("last_seen_at"),
            }, ct);
    }

    public async Task<List<PersonalFeedback>> GetFeedbackAsync(Guid userId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateControlPlaneConnection();
        return await conn.QueryListAsync(@"
            SELECT feedback_type, title, description, page_url, status, created_at
            FROM feedback WHERE user_id = @id ORDER BY created_at",
            p => p.AddWithValue("id", userId),
            r => new PersonalFeedback
            {
                FeedbackType = r.GetString("feedback_type"),
                Title = r.GetString("title"),
                Description = r.GetNullableString("description"),
                PageUrl = r.GetNullableString("page_url"),
                Status = r.GetString("status"),
                CreatedAt = r.GetDateTime("created_at"),
            }, ct);
    }

    /// <summary>Every organization database the person has a membership in, with the organization's slug and name.</summary>
    public async Task<List<(string DbIdentifier, string Slug, string Name)>> ListOrganizationDatabasesAsync(Guid userId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateControlPlaneConnection();
        return await conn.QueryListAsync(@"
            SELECT DISTINCT t.db_identifier, t.slug, t.display_name
            FROM tenant_memberships m JOIN tenants t ON t.id = m.tenant_id
            WHERE m.user_id = @id ORDER BY t.slug",
            p => p.AddWithValue("id", userId),
            r => (r.GetString("db_identifier"), r.GetString("slug"), r.GetString("display_name")), ct);
    }

    /// <summary>The person's rows in one organization database.</summary>
    public async Task<PersonalOrganizationData> GetOrganizationDataAsync(
        string dbIdentifier, string slug, string name, Guid userId, CancellationToken ct = default)
    {
        await using var db = _connectionFactory.CreateConnectionForDatabase(dbIdentifier);
        void Bind(NpgsqlParameterCollection p) => p.AddWithValue("id", userId);

        var preferences = await db.QuerySingleOrDefaultAsync(
            "SELECT preferences FROM user_preferences WHERE user_id = @id",
            Bind, r => (JsonElement?)r.GetJsonElement("preferences"), ct);

        var sites = await db.QueryListAsync(@"
            SELECT s.name AS site_name, m.role, m.created_at
            FROM memberships m JOIN sites s ON s.id = m.site_id
            WHERE m.user_id = @id ORDER BY s.name",
            Bind, r => new PersonalSiteMembership
            {
                SiteName = r.GetString("site_name"),
                Role = r.GetString("role"),
                CreatedAt = r.GetDateTime("created_at"),
            }, ct);

        var feeds = await db.QueryListAsync(@"
            SELECT f.label, s.name AS site_name, f.created_at, f.last_used_at, f.revoked_at
            FROM calendar_feed_tokens f LEFT JOIN sites s ON s.id = f.site_id
            WHERE f.user_id = @id ORDER BY f.created_at",
            Bind, r => new PersonalCalendarFeed
            {
                Label = r.GetNullableString("label"),
                SiteName = r.GetNullableString("site_name"),
                CreatedAt = r.GetDateTime("created_at"),
                LastUsedAt = r.GetNullableDateTime("last_used_at"),
                RevokedAt = r.GetNullableDateTime("revoked_at"),
            }, ct);

        var conversations = await db.QueryListAsync(
            "SELECT title, transcript, created_at, updated_at FROM ai_conversations WHERE user_id = @id ORDER BY created_at",
            Bind, r => new PersonalAssistantConversation
            {
                Title = r.GetString("title"),
                Transcript = r.GetJsonElement("transcript"),
                CreatedAt = r.GetDateTime("created_at"),
                UpdatedAt = r.GetDateTime("updated_at"),
            }, ct);

        var actions = await db.QueryListAsync(
            "SELECT action, target_type, target_id, created_at FROM audit_events WHERE actor_user_id = @id ORDER BY created_at",
            Bind, r => new PersonalAuditAction
            {
                Action = r.GetString("action"),
                TargetType = r.GetNullableString("target_type"),
                TargetId = r.GetNullableString("target_id"),
                CreatedAt = r.GetDateTime("created_at"),
            }, ct);

        return new PersonalOrganizationData
        {
            TenantSlug = slug,
            TenantName = name,
            Preferences = preferences,
            SiteMemberships = sites,
            CalendarFeeds = feeds,
            AssistantConversations = conversations,
            AuditActions = actions,
        };
    }
}
