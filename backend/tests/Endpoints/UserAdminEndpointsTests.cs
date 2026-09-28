using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Constants;
using Orkyo.Foundation.Tests.Mocks;

namespace Orkyo.Foundation.Tests.Endpoints;

/// <summary>
/// Integration tests for the user admin endpoints (site-admin only).
/// GET /api/admin/users              — list users
/// GET /api/admin/users/{id}         — get user detail
/// GET /api/admin/users/{id}/memberships — list memberships
/// POST /api/admin/users/{id}/deactivate   — disable
/// POST /api/admin/users/{id}/reactivate   — enable
/// DELETE /api/admin/users/{id}      — permanent delete
/// POST /api/admin/users/{id}/promote-site-admin
/// POST /api/admin/users/{id}/revoke-site-admin
/// </summary>
[Collection("Database collection")]
public class UserAdminEndpointsTests
{
    private readonly HttpClient _client;
    private readonly DatabaseFixture _fixture;
    private readonly string _connString;

    public UserAdminEndpointsTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.Factory.CreateClient();
        _connString = fixture.ControlPlaneConnectionString;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private HttpRequestMessage Auth(HttpMethod method, string url, string token)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return req;
    }

    private static Task<LinkedTestUser> CreateSiteAdminAsync(string prefix = "ua-admin")
        => DatabaseTestUtils.CreateLinkedUserAsync(prefix, siteAdmin: true);

    private static Task<LinkedTestUser> CreateRegularUserAsync(string prefix = "ua-user")
        => DatabaseTestUtils.CreateLinkedUserAsync(prefix);

    private void ResetKeycloak() => _fixture.Factory.MockKeycloakAdminService.Reset();

    // ── Auth guards ───────────────────────────────────────────────────────────

    [Fact]
    public async Task GetUsers_Unauthenticated_Returns401()
    {
        var response = await _client.GetAsync("/api/admin/users");
        Assert.True(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task GetUsers_RegularUser_Returns403()
    {
        var (_, token) = await CreateRegularUserAsync();
        var response = await _client.SendAsync(Auth(HttpMethod.Get, "/api/admin/users", token));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── GET /api/admin/users ──────────────────────────────────────────────────

    [Fact]
    public async Task GetUsers_SiteAdmin_Returns200WithUserList()
    {
        var (_, adminToken) = await CreateSiteAdminAsync();
        var response = await _client.SendAsync(Auth(HttpMethod.Get, "/api/admin/users", adminToken));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.TryGetProperty("users", out var users));
        Assert.Equal(JsonValueKind.Array, users.ValueKind);
        // The list is capped; the real total and the truncation flag travel with it.
        Assert.True(body.GetProperty("totalItems").GetInt32() >= users.GetArrayLength());
        Assert.Equal(JsonValueKind.False, body.GetProperty("hasNextPage").ValueKind);
    }

    [Fact]
    public async Task GetUsers_SearchWithAnUnderscore_MatchesItLiterally()
    {
        // An unescaped `_` matched any one character, so "x_a" also found "xba".
        var marker = $"m12{Guid.NewGuid():N}"[..14];
        var literal = await DatabaseTestUtils.CreateTestUserAsync($"{marker}_a@test.local");
        await DatabaseTestUtils.CreateTestUserAsync($"{marker}ba@test.local");
        var (_, adminToken) = await CreateSiteAdminAsync();

        var response = await _client.SendAsync(Auth(HttpMethod.Get,
            $"/api/admin/users?search={Uri.EscapeDataString(marker + "_a")}", adminToken));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var ids = body.GetProperty("users").EnumerateArray().Select(u => u.GetProperty("id").GetGuid());
        Assert.Equal([literal], ids);

        // Both filters name columns the joined tenants table also has; unqualified, they failed.
        var byStatus = await _client.SendAsync(Auth(HttpMethod.Get, "/api/admin/users?status=active", adminToken));
        Assert.Equal(HttpStatusCode.OK, byStatus.StatusCode);
    }

    // ── GET /api/admin/users/{id} ─────────────────────────────────────────────

    [Fact]
    public async Task GetUser_ExistingUser_Returns200WithDetail()
    {
        var (adminId, adminToken) = await CreateSiteAdminAsync();

        var response = await _client.SendAsync(
            Auth(HttpMethod.Get, $"/api/admin/users/{adminId}", adminToken));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(adminId.ToString(), body.GetProperty("id").GetString());
        Assert.True(body.TryGetProperty("identities", out _));
        Assert.True(body.TryGetProperty("memberships", out _));
    }

    [Fact]
    public async Task GetUser_NonExistentUser_Returns404()
    {
        var (_, adminToken) = await CreateSiteAdminAsync();
        var response = await _client.SendAsync(
            Auth(HttpMethod.Get, $"/api/admin/users/{Guid.NewGuid()}", adminToken));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── GET /api/admin/users/{id}/memberships ─────────────────────────────────

    [Fact]
    public async Task GetUserMemberships_ExistingUser_Returns200WithList()
    {
        var (userId, _) = await CreateRegularUserAsync();
        var (_, adminToken) = await CreateSiteAdminAsync();

        var response = await _client.SendAsync(
            Auth(HttpMethod.Get, $"/api/admin/users/{userId}/memberships", adminToken));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.TryGetProperty("memberships", out var memberships));
        Assert.Equal(JsonValueKind.Array, memberships.ValueKind);
    }

    [Fact]
    public async Task GetUserMemberships_NonExistentUser_Returns404()
    {
        var (_, adminToken) = await CreateSiteAdminAsync();
        var response = await _client.SendAsync(
            Auth(HttpMethod.Get, $"/api/admin/users/{Guid.NewGuid()}/memberships", adminToken));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── POST /api/admin/users/{id}/deactivate ────────────────────────────────

    [Fact]
    public async Task DeactivateUser_ExistingUser_Returns204()
    {
        ResetKeycloak();
        var (userId, _) = await CreateRegularUserAsync();
        var (_, adminToken) = await CreateSiteAdminAsync();

        var response = await _client.SendAsync(
            Auth(HttpMethod.Post, $"/api/admin/users/{userId}/deactivate", adminToken));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    // ── POST /api/admin/users/{id}/reactivate ────────────────────────────────

    [Fact]
    public async Task ReactivateUser_ExistingUser_Returns204()
    {
        ResetKeycloak();
        var (userId, _) = await CreateRegularUserAsync();
        var (_, adminToken) = await CreateSiteAdminAsync();

        var response = await _client.SendAsync(
            Auth(HttpMethod.Post, $"/api/admin/users/{userId}/reactivate", adminToken));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    // ── DELETE /api/admin/users/{id} ─────────────────────────────────────────

    [Fact]
    public async Task DeleteUser_ExistingUser_Returns204()
    {
        ResetKeycloak();
        var (userId, _) = await CreateRegularUserAsync("ua-del");
        var (_, adminToken) = await CreateSiteAdminAsync();

        var response = await _client.SendAsync(
            Auth(HttpMethod.Delete, $"/api/admin/users/{userId}", adminToken));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    // ── POST /api/admin/users/{id}/promote-site-admin ────────────────────────

    [Fact]
    public async Task PromoteSiteAdmin_UserWithNoKeycloakIdentity_Returns422()
    {
        var (_, adminToken) = await CreateSiteAdminAsync();

        // Create a user with NO Keycloak identity
        var targetId = Guid.NewGuid();
        await using var conn = new Npgsql.NpgsqlConnection(_connString);
        await conn.OpenAsync();
        await using var cmd = new Npgsql.NpgsqlCommand(
            "INSERT INTO users (id, email, display_name, status) VALUES (@id, @email, 'NoKC', 'active')", conn);
        cmd.Parameters.AddWithValue("id", targetId);
        cmd.Parameters.AddWithValue("email", $"nokc-{targetId}@test.com");
        await cmd.ExecuteNonQueryAsync();

        var response = await _client.SendAsync(
            Auth(HttpMethod.Post, $"/api/admin/users/{targetId}/promote-site-admin", adminToken));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task PromoteSiteAdmin_AlreadySiteAdmin_Returns409()
    {
        ResetKeycloak();
        // HasRealmRoleAsync returns MockRealmRoles.Contains(roleName) — pre-seed the role
        _fixture.Factory.MockKeycloakAdminService.MockRealmRoles.Add("site-admin");
        var (targetId, _) = await CreateRegularUserAsync("ua-promote-conflict");
        var (_, adminToken) = await CreateSiteAdminAsync("ua-promote-conflict-admin");

        var response = await _client.SendAsync(
            Auth(HttpMethod.Post, $"/api/admin/users/{targetId}/promote-site-admin", adminToken));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        _fixture.Factory.MockKeycloakAdminService.MockRealmRoles.Remove("site-admin");
    }

    [Fact]
    public async Task PromoteSiteAdmin_NonExistentUser_Returns404()
    {
        var (_, adminToken) = await CreateSiteAdminAsync();
        var response = await _client.SendAsync(
            Auth(HttpMethod.Post, $"/api/admin/users/{Guid.NewGuid()}/promote-site-admin", adminToken));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── POST /api/admin/users/{id}/revoke-site-admin ─────────────────────────

    [Fact]
    public async Task RevokeSiteAdmin_SelfRevoke_Returns400()
    {
        var (adminId, adminToken) = await CreateSiteAdminAsync("ua-self-revoke");
        var response = await _client.SendAsync(
            Auth(HttpMethod.Post, $"/api/admin/users/{adminId}/revoke-site-admin", adminToken));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RevokeSiteAdmin_NonExistentUser_Returns404()
    {
        var (_, adminToken) = await CreateSiteAdminAsync();
        var response = await _client.SendAsync(
            Auth(HttpMethod.Post, $"/api/admin/users/{Guid.NewGuid()}/revoke-site-admin", adminToken));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RevokeSiteAdmin_UserWithNoKeycloakIdentity_Returns422()
    {
        var (_, adminToken) = await CreateSiteAdminAsync();
        var targetId = Guid.NewGuid();
        await using var conn = new Npgsql.NpgsqlConnection(_connString);
        await conn.OpenAsync();
        await using var cmd = new Npgsql.NpgsqlCommand(
            "INSERT INTO users (id, email, display_name, status) VALUES (@id, @email, 'NoKC2', 'active')", conn);
        cmd.Parameters.AddWithValue("id", targetId);
        cmd.Parameters.AddWithValue("email", $"nokc2-{targetId}@test.com");
        await cmd.ExecuteNonQueryAsync();

        var response = await _client.SendAsync(
            Auth(HttpMethod.Post, $"/api/admin/users/{targetId}/revoke-site-admin", adminToken));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    // ── Audit, Keycloak failures, self-delete ────────────────────────────────

    private async Task<long> CountAuditAsync(string action, Guid actorId, Guid targetId)
    {
        await using var conn = new Npgsql.NpgsqlConnection(_connString);
        await conn.OpenAsync();
        await using var cmd = new Npgsql.NpgsqlCommand(
            "SELECT COUNT(*) FROM audit_events WHERE action = @a AND actor_user_id = @actor AND target_type = 'user' AND target_id = @t", conn);
        cmd.Parameters.AddWithValue("a", action);
        cmd.Parameters.AddWithValue("actor", actorId);
        cmd.Parameters.AddWithValue("t", targetId.ToString());
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task<string?> GetStatusAsync(Guid userId)
    {
        await using var conn = new Npgsql.NpgsqlConnection(_connString);
        await conn.OpenAsync();
        await using var cmd = new Npgsql.NpgsqlCommand("SELECT status FROM users WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", userId);
        return (string?)await cmd.ExecuteScalarAsync();
    }

    [Theory]
    [InlineData("deactivate", SecurityAuditActions.UserDeactivated)]
    [InlineData("reactivate", SecurityAuditActions.UserReactivated)]
    [InlineData("promote-site-admin", SecurityAuditActions.SiteAdminGranted)]
    [InlineData("revoke-site-admin", SecurityAuditActions.SiteAdminRevoked)]
    [InlineData("", SecurityAuditActions.UserDeleted)]
    public async Task EachAction_WritesAnAuditRow(string route, string action)
    {
        ResetKeycloak();
        if (route == "revoke-site-admin")
            _fixture.Factory.MockKeycloakAdminService.MockRealmRoles.Add("site-admin");
        var (targetId, _) = await CreateRegularUserAsync("ua-audit");
        var (adminId, adminToken) = await CreateSiteAdminAsync("ua-audit-admin");

        var response = route == ""
            ? await _client.SendAsync(Auth(HttpMethod.Delete, $"/api/admin/users/{targetId}", adminToken))
            : await _client.SendAsync(Auth(HttpMethod.Post, $"/api/admin/users/{targetId}/{route}", adminToken));
        ResetKeycloak();

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(1, await CountAuditAsync(action, adminId, targetId));
    }

    [Fact]
    public async Task DeactivateUser_WhenKeycloakFails_ReturnsAnErrorAndLeavesTheUserActive()
    {
        ResetKeycloak();
        _fixture.Factory.MockKeycloakAdminService.DisableUserSuccess = false;
        var (targetId, _) = await CreateRegularUserAsync("ua-kc-fail");
        var (adminId, adminToken) = await CreateSiteAdminAsync("ua-kc-fail-admin");

        var response = await _client.SendAsync(
            Auth(HttpMethod.Post, $"/api/admin/users/{targetId}/deactivate", adminToken));
        ResetKeycloak();

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal(UserStatusConstants.Active, await GetStatusAsync(targetId));
        Assert.Equal(0, await CountAuditAsync(SecurityAuditActions.UserDeactivated, adminId, targetId));
    }

    [Fact]
    public async Task DeleteUser_WhenKeycloakFails_ReturnsAnErrorAndKeepsTheUser()
    {
        ResetKeycloak();
        _fixture.Factory.MockKeycloakAdminService.DeleteUserSuccess = false;
        var (targetId, _) = await CreateRegularUserAsync("ua-kc-del-fail");
        var (_, adminToken) = await CreateSiteAdminAsync("ua-kc-del-fail-admin");

        var response = await _client.SendAsync(
            Auth(HttpMethod.Delete, $"/api/admin/users/{targetId}", adminToken));
        ResetKeycloak();

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.NotNull(await GetStatusAsync(targetId));
    }

    [Fact]
    public async Task DeleteUser_Self_Returns400AndKeepsTheAccount()
    {
        ResetKeycloak();
        var (adminId, adminToken) = await CreateSiteAdminAsync("ua-self-delete");

        var response = await _client.SendAsync(
            Auth(HttpMethod.Delete, $"/api/admin/users/{adminId}", adminToken));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, _fixture.Factory.MockKeycloakAdminService.DeleteUserCallCount);
        Assert.NotNull(await GetStatusAsync(adminId));
    }
}
