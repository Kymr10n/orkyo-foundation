using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;

namespace Orkyo.Foundation.Tests.Endpoints;

/// <summary>
/// GET /api/account/export and POST /api/account/delete: the person's own data, downloaded and
/// erased. Each test creates its own linked user, so the shared test user is never touched.
/// </summary>
[Collection("Database collection")]
public class AccountSelfServiceEndpointsTests
{
    private readonly DatabaseFixture _fixture;
    private readonly MockKeycloakAdminService _mockKeycloak;

    public AccountSelfServiceEndpointsTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
        _mockKeycloak = fixture.Factory.MockKeycloakAdminService;
        _mockKeycloak.Reset();
        fixture.Factory.AccountGuard.Locked = false;
    }

    // ─── helpers ────────────────────────────────────────────────────────────────

    /// <summary>
    /// A linked user who is a <paramref name="role"/> member of the test tenant, mirrored into
    /// its database with one assistant conversation and a preferences row.
    /// </summary>
    private async Task<(LinkedTestUser User, HttpClient Client)> CreateMemberAsync(string role = "viewer")
    {
        var user = await DatabaseTestUtils.CreateLinkedUserAsync("selfservice");

        await using (var cp = new NpgsqlConnection(_fixture.ControlPlaneConnectionString))
        {
            await cp.OpenAsync();
            await using var cmd = new NpgsqlCommand(@"
                INSERT INTO tenant_memberships (user_id, tenant_id, role, status) VALUES (@id, @tenantId, @role, 'active');
                INSERT INTO tos_acceptances (user_id, tos_version, accepted_ip, accepted_user_agent) VALUES (@id, '2026-01', '203.0.113.7', 'TestAgent/1.0');
                INSERT INTO user_sessions (user_id, keycloak_session_id, ip_address, user_agent, browser, operating_system, device_type)
                VALUES (@id, @sid, '203.0.113.7', 'TestAgent/1.0', 'Firefox', 'Linux', 'desktop');
                INSERT INTO feedback (tenant_id, user_id, feedback_type, title, description, page_url)
                VALUES (@tenantId, @id, 'feature', 'Dark mode please', 'Night shifts', '/app/settings')", cp);
            cmd.Parameters.AddWithValue("id", user.UserId);
            cmd.Parameters.AddWithValue("tenantId", TestConstants.TenantId);
            cmd.Parameters.AddWithValue("role", role);
            cmd.Parameters.AddWithValue("sid", $"sid-{user.UserId}");
            await cmd.ExecuteNonQueryAsync();
        }

        await using (var tenant = new NpgsqlConnection(_fixture.TenantConnectionString))
        {
            await tenant.OpenAsync();
            await using var cmd = new NpgsqlCommand(@"
                INSERT INTO users (id, email, display_name) VALUES (@id, @email, 'selfservice');
                INSERT INTO user_preferences (user_id, preferences) VALUES (@id, '{""theme"":""dark""}'::jsonb);
                INSERT INTO ai_conversations (id, user_id, title, entries, transcript)
                VALUES (gen_random_uuid(), @id, 'Shift planning', '[]'::jsonb, '[{""role"":""user"",""text"":""hi""}]'::jsonb);
                INSERT INTO memberships (user_id, site_id, role) VALUES (@id, @siteId, 'writer');
                INSERT INTO calendar_feed_tokens (user_id, token_hash, label, site_id) VALUES (@id, @tokenHash, 'Outlook, laptop', @siteId);
                INSERT INTO audit_events (actor_user_id, action, target_type, target_id) VALUES (@id, 'request.created', 'request', 'r-1')", tenant);
            cmd.Parameters.AddWithValue("id", user.UserId);
            cmd.Parameters.AddWithValue("email", $"selfservice-{user.UserId}@test.com");
            cmd.Parameters.AddWithValue("siteId", DatabaseFixture.SiteId);
            cmd.Parameters.AddWithValue("tokenHash", Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(user.UserId.ToByteArray())).ToLowerInvariant());
            await cmd.ExecuteNonQueryAsync();
        }

        return (user, _fixture.CreateClientWithToken(user.Token));
    }

    private async Task<long> CountAsync(string connectionString, string sql, Guid userId)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", userId);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    private Task<long> ControlPlaneUserRowsAsync(Guid userId) =>
        CountAsync(_fixture.ControlPlaneConnectionString, "SELECT count(*) FROM users WHERE id = @id", userId);

    private Task<long> TenantRowsAsync(Guid userId) =>
        CountAsync(_fixture.TenantConnectionString, @"
            SELECT (SELECT count(*) FROM users WHERE id = @id)
                 + (SELECT count(*) FROM user_preferences WHERE user_id = @id)
                 + (SELECT count(*) FROM ai_conversations WHERE user_id = @id)", userId);

    private static Task<HttpResponseMessage> DeleteAsync(HttpClient client, string confirmEmail) =>
        client.PostAsJsonAsync("/api/account/delete", new { confirmEmail });

    private static string EmailOf(LinkedTestUser user) => $"selfservice-{user.UserId}@test.com";

    // ─── export ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Export_ReturnsProfileMembershipsAndTheRowsFromEachOrganization()
    {
        var (user, client) = await CreateMemberAsync();

        var response = await client.GetAsync("/api/account/export");

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("schemaVersion").GetString().Should().Be("1.0");
        body.GetProperty("profile").GetProperty("email").GetString().Should().Be(EmailOf(user));
        body.GetProperty("identities").GetArrayLength().Should().Be(1);
        body.GetProperty("identities")[0].GetProperty("providerSubject").GetString().Should().Be($"kc-{user.UserId}");
        body.GetProperty("memberships").GetArrayLength().Should().Be(1);
        body.GetProperty("memberships")[0].GetProperty("tenantSlug").GetString().Should().Be(TestConstants.TenantSlug);

        var org = body.GetProperty("organizations")[0];
        org.GetProperty("tenantSlug").GetString().Should().Be(TestConstants.TenantSlug);
        org.GetProperty("preferences").GetProperty("theme").GetString().Should().Be("dark");
        org.GetProperty("assistantConversations").GetArrayLength().Should().Be(1);
        org.GetProperty("assistantConversations")[0].GetProperty("title").GetString().Should().Be("Shift planning");
        org.GetProperty("siteMemberships")[0].GetProperty("role").GetString().Should().Be("writer");
        org.GetProperty("calendarFeeds")[0].GetProperty("label").GetString().Should().Be("Outlook, laptop");
        org.GetProperty("calendarFeeds")[0].TryGetProperty("tokenHash", out _).Should().BeFalse("the token never leaves the server");
        org.GetProperty("auditActions")[0].GetProperty("action").GetString().Should().Be("request.created");
        body.GetProperty("termsAcceptances")[0].GetProperty("tosVersion").GetString().Should().Be("2026-01");
        body.GetProperty("sessions")[0].GetProperty("browser").GetString().Should().Be("Firefox");
        body.GetProperty("feedback")[0].GetProperty("title").GetString().Should().Be("Dark mode please");
    }

    [Fact]
    public async Task Export_WithoutAuthentication_Returns401()
    {
        var response = await _fixture.Factory.CreateClient().GetAsync("/api/account/export");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ─── delete: refusals ────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_WithTheWrongEmail_Returns400AndErasesNothing()
    {
        var (user, client) = await CreateMemberAsync();

        var response = await DeleteAsync(client, "someone-else@test.com");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ControlPlaneUserRowsAsync(user.UserId)).Should().Be(1);
        (await TenantRowsAsync(user.UserId)).Should().Be(3);
        _mockKeycloak.DeleteUserCallCount.Should().Be(0);
    }

    [Fact]
    public async Task Delete_WithAnEmptyEmail_FailsValidation()
    {
        var (_, client) = await CreateMemberAsync();

        var response = await DeleteAsync(client, "");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()
            .Should().Be(ApiErrorCodes.ValidationError);
    }

    [Fact]
    public async Task Delete_OnALockedSharedAccount_Returns403()
    {
        var (user, client) = await CreateMemberAsync();
        _fixture.Factory.AccountGuard.Locked = true;
        try
        {
            var response = await DeleteAsync(client, EmailOf(user));

            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()
                .Should().Be(ApiErrorCodes.AccountLocked);
            (await ControlPlaneUserRowsAsync(user.UserId)).Should().Be(1);
        }
        finally
        {
            _fixture.Factory.AccountGuard.Locked = false;
        }
    }

    [Fact]
    public async Task Delete_WhenTheCallerOwnsAnOrganization_Returns409NamingIt()
    {
        var (user, client) = await CreateMemberAsync();
        var (tenantId, _) = await DatabaseTestUtils.CreateTestTenantAsync("owned");
        try
        {
            await using (var cp = new NpgsqlConnection(_fixture.ControlPlaneConnectionString))
            {
                await cp.OpenAsync();
                await using var cmd = new NpgsqlCommand(
                    "UPDATE tenants SET owner_user_id = @id, display_name = 'Owned Org' WHERE id = @tenantId", cp);
                cmd.Parameters.AddWithValue("id", user.UserId);
                cmd.Parameters.AddWithValue("tenantId", tenantId);
                await cmd.ExecuteNonQueryAsync();
            }

            var response = await DeleteAsync(client, EmailOf(user));

            response.StatusCode.Should().Be(HttpStatusCode.Conflict);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            body.GetProperty("code").GetString().Should().Be(ApiErrorCodes.AccountOwnsOrganizations);
            body.GetProperty("detail").GetString().Should().Contain("Owned Org");
            (await ControlPlaneUserRowsAsync(user.UserId)).Should().Be(1);
        }
        finally
        {
            await DatabaseTestUtils.DeleteTestTenantAsync(tenantId);
        }
    }

    [Fact]
    public async Task Delete_WhenTheCallerIsTheOnlyActiveAdmin_Returns409NamingTheOrganization()
    {
        var (user, client) = await CreateMemberAsync();
        var (tenantId, _) = await DatabaseTestUtils.CreateTestTenantAsync("soleadmin");
        try
        {
            await using (var cp = new NpgsqlConnection(_fixture.ControlPlaneConnectionString))
            {
                await cp.OpenAsync();
                await using var cmd = new NpgsqlCommand(@"
                    UPDATE tenants SET display_name = 'Sole Admin Org' WHERE id = @tenantId;
                    INSERT INTO tenant_memberships (user_id, tenant_id, role, status) VALUES (@id, @tenantId, 'admin', 'active')", cp);
                cmd.Parameters.AddWithValue("id", user.UserId);
                cmd.Parameters.AddWithValue("tenantId", tenantId);
                await cmd.ExecuteNonQueryAsync();
            }

            var response = await DeleteAsync(client, EmailOf(user));

            response.StatusCode.Should().Be(HttpStatusCode.Conflict);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            body.GetProperty("code").GetString().Should().Be(ApiErrorCodes.AccountLastAdmin);
            body.GetProperty("detail").GetString().Should().Contain("Sole Admin Org");
            (await ControlPlaneUserRowsAsync(user.UserId)).Should().Be(1);
        }
        finally
        {
            await DatabaseTestUtils.DeleteTestTenantAsync(tenantId);
        }
    }

    [Fact]
    public async Task Delete_AsASecondAdmin_IsAllowed()
    {
        // The shared test user is the test tenant's admin, so this admin is never the last one.
        var (user, client) = await CreateMemberAsync(role: "admin");

        var response = await DeleteAsync(client, EmailOf(user));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ControlPlaneUserRowsAsync(user.UserId)).Should().Be(0);
    }

    // ─── delete: erasure ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_ErasesEveryRow_DeletesTheKeycloakAccount_AndRecordsTheAuditEvent()
    {
        var (user, client) = await CreateMemberAsync();

        var response = await DeleteAsync(client, EmailOf(user).ToUpperInvariant());

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ControlPlaneUserRowsAsync(user.UserId)).Should().Be(0);
        (await TenantRowsAsync(user.UserId)).Should().Be(0);
        _mockKeycloak.LastDeletedKeycloakId.Should().Be($"kc-{user.UserId}");

        await using var cp = new NpgsqlConnection(_fixture.ControlPlaneConnectionString);
        await cp.OpenAsync();
        await using var cmd = new NpgsqlCommand(@"
            SELECT actor_user_id IS NULL FROM audit_events
            WHERE action = @action AND target_id = @target", cp);
        cmd.Parameters.AddWithValue("action", SecurityAuditActions.AccountDeleted);
        cmd.Parameters.AddWithValue("target", user.UserId.ToString());
        var actorIsNull = await cmd.ExecuteScalarAsync();
        actorIsNull.Should().Be(true, "the audit row survives the purge with its actor set to NULL");
    }

    [Fact]
    public async Task Delete_WhenKeycloakFails_StillErasesTheRows()
    {
        var (user, client) = await CreateMemberAsync();
        _mockKeycloak.DeleteUserSuccess = false;
        try
        {
            var response = await DeleteAsync(client, EmailOf(user));

            response.StatusCode.Should().Be(HttpStatusCode.NoContent);
            (await ControlPlaneUserRowsAsync(user.UserId)).Should().Be(0);
            (await TenantRowsAsync(user.UserId)).Should().Be(0);
            _mockKeycloak.DeleteUserCallCount.Should().Be(1);
        }
        finally
        {
            _mockKeycloak.DeleteUserSuccess = true;
        }
    }

    [Fact]
    public async Task Delete_ForAUserWhoNoLongerExists_Returns404()
    {
        var (user, client) = await CreateMemberAsync();
        (await DeleteAsync(client, EmailOf(user))).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var second = await DeleteAsync(client, EmailOf(user));

        second.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }
}
