using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Constants;
using Microsoft.AspNetCore.Http;
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
[Trait(TestCategories.Name, TestCategories.Database)]
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

    private void ResetKeycloak() => _fixture.Factory.MockKeycloakAdminService.Reset();

    // ── Auth guards ───────────────────────────────────────────────────────────

    [Fact]
    public async Task GetUsers_RegularUser_Returns403()
    {
        var (_, token) = await DatabaseTestUtils.CreateLinkedUserAsync("ua-user");
        var response = await _client.SendAsync(TestHelpers.AuthRequest(HttpMethod.Get, "/api/admin/users", token));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── GET /api/admin/users ──────────────────────────────────────────────────

    [Fact]
    public async Task GetUsers_SiteAdmin_Returns200WithUserList()
    {
        var (_, adminToken) = await DatabaseTestUtils.CreateLinkedUserAsync("ua-admin", siteAdmin: true);
        var response = await _client.SendAsync(TestHelpers.AuthRequest(HttpMethod.Get, "/api/admin/users", adminToken));

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
        var (_, adminToken) = await DatabaseTestUtils.CreateLinkedUserAsync("ua-admin", siteAdmin: true);

        var response = await _client.SendAsync(TestHelpers.AuthRequest(HttpMethod.Get,
            $"/api/admin/users?search={Uri.EscapeDataString(marker + "_a")}", adminToken));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var ids = body.GetProperty("users").EnumerateArray().Select(u => u.GetProperty("id").GetGuid());
        Assert.Equal([literal], ids);

        // Both filters name columns the joined tenants table also has; unqualified, they failed.
        var byStatus = await _client.SendAsync(TestHelpers.AuthRequest(HttpMethod.Get, "/api/admin/users?status=active", adminToken));
        Assert.Equal(HttpStatusCode.OK, byStatus.StatusCode);
    }

    // ── GET /api/admin/users/{id} ─────────────────────────────────────────────

    [Fact]
    public async Task GetUser_ExistingUser_Returns200WithDetail()
    {
        var (adminId, adminToken) = await DatabaseTestUtils.CreateLinkedUserAsync("ua-admin", siteAdmin: true);

        var response = await _client.SendAsync(
            TestHelpers.AuthRequest(HttpMethod.Get, $"/api/admin/users/{adminId}", adminToken));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(adminId.ToString(), body.GetProperty("id").GetString());
        Assert.True(body.TryGetProperty("identities", out _));
        Assert.True(body.TryGetProperty("memberships", out _));
    }

    // ── GET /api/admin/users/{id}/memberships ─────────────────────────────────

    [Fact]
    public async Task GetUserMemberships_ExistingUser_Returns200WithList()
    {
        var (userId, _) = await DatabaseTestUtils.CreateLinkedUserAsync("ua-user");
        var (_, adminToken) = await DatabaseTestUtils.CreateLinkedUserAsync("ua-admin", siteAdmin: true);

        var response = await _client.SendAsync(
            TestHelpers.AuthRequest(HttpMethod.Get, $"/api/admin/users/{userId}/memberships", adminToken));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.TryGetProperty("memberships", out var memberships));
        Assert.Equal(JsonValueKind.Array, memberships.ValueKind);
    }

    // ── POST /api/admin/users/{id}/promote-site-admin ────────────────────────

    [Fact]
    public async Task PromoteSiteAdmin_UserWithNoKeycloakIdentity_Returns422()
    {
        var (_, adminToken) = await DatabaseTestUtils.CreateLinkedUserAsync("ua-admin", siteAdmin: true);

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
            TestHelpers.AuthRequest(HttpMethod.Post, $"/api/admin/users/{targetId}/promote-site-admin", adminToken));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task GetUsers_FlagsSiteAdmins_FromOneRoleMembersRead()
    {
        // One Keycloak call for the whole list, not one per user.
        ResetKeycloak();
        var prefix = $"ua-flag-{Guid.NewGuid():N}"[..16];
        var (adminTarget, _) = await DatabaseTestUtils.CreateLinkedUserAsync(prefix);
        await DatabaseTestUtils.CreateLinkedUserAsync(prefix);
        var keycloak = _fixture.Factory.MockKeycloakAdminService;
        keycloak.RealmRoleMemberIds.Add($"kc-{adminTarget}");
        var (_, adminToken) = await DatabaseTestUtils.CreateLinkedUserAsync("ua-admin", siteAdmin: true);

        var response = await _client.SendAsync(TestHelpers.AuthRequest(HttpMethod.Get, $"/api/admin/users?search={prefix}", adminToken));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var users = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("users").EnumerateArray().ToList();
        Assert.Equal(2, users.Count);
        Assert.True(users.Single(u => u.GetProperty("id").GetGuid() == adminTarget).GetProperty("isSiteAdmin").GetBoolean());
        Assert.Single(users, u => u.GetProperty("isSiteAdmin").GetBoolean());
        Assert.Equal(1, keycloak.GetRealmRoleMemberIdsCallCount);
        Assert.Equal(0, keycloak.HasRealmRoleCallCount);
        ResetKeycloak();
    }

    [Fact]
    public async Task PromoteSiteAdmin_AlreadySiteAdmin_Returns409()
    {
        ResetKeycloak();
        // HasRealmRoleAsync returns MockRealmRoles.Contains(roleName) — pre-seed the role
        _fixture.Factory.MockKeycloakAdminService.MockRealmRoles.Add("site-admin");
        var (targetId, _) = await DatabaseTestUtils.CreateLinkedUserAsync("ua-promote-conflict");
        var (_, adminToken) = await DatabaseTestUtils.CreateLinkedUserAsync("ua-promote-conflict-admin", siteAdmin: true);

        var response = await _client.SendAsync(
            TestHelpers.AuthRequest(HttpMethod.Post, $"/api/admin/users/{targetId}/promote-site-admin", adminToken));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        _fixture.Factory.MockKeycloakAdminService.MockRealmRoles.Remove("site-admin");
    }

    // ── POST /api/admin/users/{id}/revoke-site-admin ─────────────────────────

    [Fact]
    public async Task RevokeSiteAdmin_SelfRevoke_Returns400()
    {
        var (adminId, adminToken) = await DatabaseTestUtils.CreateLinkedUserAsync("ua-self-revoke", siteAdmin: true);
        var response = await _client.SendAsync(
            TestHelpers.AuthRequest(HttpMethod.Post, $"/api/admin/users/{adminId}/revoke-site-admin", adminToken));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RevokeSiteAdmin_UserWithNoKeycloakIdentity_Returns422()
    {
        var (_, adminToken) = await DatabaseTestUtils.CreateLinkedUserAsync("ua-admin", siteAdmin: true);
        var targetId = Guid.NewGuid();
        await using var conn = new Npgsql.NpgsqlConnection(_connString);
        await conn.OpenAsync();
        await using var cmd = new Npgsql.NpgsqlCommand(
            "INSERT INTO users (id, email, display_name, status) VALUES (@id, @email, 'NoKC2', 'active')", conn);
        cmd.Parameters.AddWithValue("id", targetId);
        cmd.Parameters.AddWithValue("email", $"nokc2-{targetId}@test.com");
        await cmd.ExecuteNonQueryAsync();

        var response = await _client.SendAsync(
            TestHelpers.AuthRequest(HttpMethod.Post, $"/api/admin/users/{targetId}/revoke-site-admin", adminToken));
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
    [InlineData("GET", "")]
    [InlineData("GET", "/memberships")]
    [InlineData("POST", "/deactivate")]
    [InlineData("POST", "/reactivate")]
    [InlineData("POST", "/promote-site-admin")]
    [InlineData("POST", "/revoke-site-admin")]
    [InlineData("DELETE", "")]
    public async Task EachAction_OnAnUnknownUser_Returns404(string method, string route)
    {
        var (_, adminToken) = await DatabaseTestUtils.CreateLinkedUserAsync("ua-admin", siteAdmin: true);

        var response = await _client.SendAsync(
            TestHelpers.AuthRequest(new HttpMethod(method), $"/api/admin/users/{Guid.NewGuid()}{route}", adminToken));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
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
        var (targetId, _) = await DatabaseTestUtils.CreateLinkedUserAsync("ua-audit");
        var (adminId, adminToken) = await DatabaseTestUtils.CreateLinkedUserAsync("ua-audit-admin", siteAdmin: true);

        var response = route == ""
            ? await _client.SendAsync(TestHelpers.AuthRequest(HttpMethod.Delete, $"/api/admin/users/{targetId}", adminToken))
            : await _client.SendAsync(TestHelpers.AuthRequest(HttpMethod.Post, $"/api/admin/users/{targetId}/{route}", adminToken));
        ResetKeycloak();

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(1, await CountAuditAsync(action, adminId, targetId));
    }

    [Fact]
    public async Task DeactivateUser_WhenKeycloakFails_ReturnsAnErrorAndLeavesTheUserActive()
    {
        ResetKeycloak();
        _fixture.Factory.MockKeycloakAdminService.DisableUserSuccess = false;
        var (targetId, _) = await DatabaseTestUtils.CreateLinkedUserAsync("ua-kc-fail");
        var (adminId, adminToken) = await DatabaseTestUtils.CreateLinkedUserAsync("ua-kc-fail-admin", siteAdmin: true);

        var response = await _client.SendAsync(
            TestHelpers.AuthRequest(HttpMethod.Post, $"/api/admin/users/{targetId}/deactivate", adminToken));
        ResetKeycloak();

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal(UserStatusConstants.Active, await GetStatusAsync(targetId));
        Assert.Equal(0, await CountAuditAsync(SecurityAuditActions.UserDeactivated, adminId, targetId));
    }

    [Fact]
    public async Task DeactivateUser_WhenKeycloakNoLongerHasTheUser_StillDisablesTheUser()
    {
        // 404 from the identity provider means the account is already gone there; the
        // platform status and the audit row must still be written.
        ResetKeycloak();
        _fixture.Factory.MockKeycloakAdminService.DisableUserSuccess = false;
        _fixture.Factory.MockKeycloakAdminService.UserMutationErrorStatusCode = 404;
        var (targetId, _) = await DatabaseTestUtils.CreateLinkedUserAsync("ua-kc-gone");
        var (adminId, adminToken) = await DatabaseTestUtils.CreateLinkedUserAsync("ua-kc-gone-admin", siteAdmin: true);

        var response = await _client.SendAsync(
            TestHelpers.AuthRequest(HttpMethod.Post, $"/api/admin/users/{targetId}/deactivate", adminToken));
        ResetKeycloak();

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(UserStatusConstants.Disabled, await GetStatusAsync(targetId));
        Assert.Equal(1, await CountAuditAsync(SecurityAuditActions.UserDeactivated, adminId, targetId));
    }

    [Fact]
    public async Task DeleteUser_WhenKeycloakNoLongerHasTheUser_StillDeletesTheUser()
    {
        ResetKeycloak();
        _fixture.Factory.MockKeycloakAdminService.DeleteUserSuccess = false;
        _fixture.Factory.MockKeycloakAdminService.UserMutationErrorStatusCode = 404;
        var (targetId, _) = await DatabaseTestUtils.CreateLinkedUserAsync("ua-kc-del-gone");
        var (_, adminToken) = await DatabaseTestUtils.CreateLinkedUserAsync("ua-kc-del-gone-admin", siteAdmin: true);

        var response = await _client.SendAsync(
            TestHelpers.AuthRequest(HttpMethod.Delete, $"/api/admin/users/{targetId}", adminToken));
        ResetKeycloak();

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Null(await GetStatusAsync(targetId));
    }

    [Fact]
    public async Task DeleteUser_WhenKeycloakFails_ReturnsAnErrorAndKeepsTheUser()
    {
        ResetKeycloak();
        _fixture.Factory.MockKeycloakAdminService.DeleteUserSuccess = false;
        var (targetId, _) = await DatabaseTestUtils.CreateLinkedUserAsync("ua-kc-del-fail");
        var (_, adminToken) = await DatabaseTestUtils.CreateLinkedUserAsync("ua-kc-del-fail-admin", siteAdmin: true);

        var response = await _client.SendAsync(
            TestHelpers.AuthRequest(HttpMethod.Delete, $"/api/admin/users/{targetId}", adminToken));
        ResetKeycloak();

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.NotNull(await GetStatusAsync(targetId));
    }

    [Fact]
    public async Task DeleteUser_Self_Returns400AndKeepsTheAccount()
    {
        ResetKeycloak();
        var (adminId, adminToken) = await DatabaseTestUtils.CreateLinkedUserAsync("ua-self-delete", siteAdmin: true);

        var response = await _client.SendAsync(
            TestHelpers.AuthRequest(HttpMethod.Delete, $"/api/admin/users/{adminId}", adminToken));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, _fixture.Factory.MockKeycloakAdminService.DeleteUserCallCount);
        Assert.NotNull(await GetStatusAsync(adminId));
    }

    // ── Site-admin role edges ────────────────────────────────────────────────

    [Fact]
    public async Task RevokeSiteAdmin_TheLastSiteAdmin_Returns400AndKeepsTheRole()
    {
        ResetKeycloak();
        var keycloak = _fixture.Factory.MockKeycloakAdminService;
        keycloak.MockRealmRoles.Add("site-admin");
        keycloak.CountRealmRoleMembersResult = 1;
        var (targetId, _) = await DatabaseTestUtils.CreateLinkedUserAsync("ua-last-admin");
        var (adminId, adminToken) = await DatabaseTestUtils.CreateLinkedUserAsync("ua-last-admin-caller", siteAdmin: true);

        var response = await _client.SendAsync(
            TestHelpers.AuthRequest(HttpMethod.Post, $"/api/admin/users/{targetId}/revoke-site-admin", adminToken));
        var revokeCalls = keycloak.RevokeRealmRoleCallCount;
        ResetKeycloak();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, revokeCalls);
        Assert.Equal(0, await CountAuditAsync(SecurityAuditActions.SiteAdminRevoked, adminId, targetId));
    }

    [Fact]
    public async Task RevokeSiteAdmin_AUserWithoutTheRole_Returns409()
    {
        ResetKeycloak();
        var (targetId, _) = await DatabaseTestUtils.CreateLinkedUserAsync("ua-not-admin");
        var (_, adminToken) = await DatabaseTestUtils.CreateLinkedUserAsync("ua-not-admin-caller", siteAdmin: true);

        var response = await _client.SendAsync(
            TestHelpers.AuthRequest(HttpMethod.Post, $"/api/admin/users/{targetId}/revoke-site-admin", adminToken));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(0, _fixture.Factory.MockKeycloakAdminService.RevokeRealmRoleCallCount);
    }

    [Theory]
    [InlineData("promote-site-admin")]
    [InlineData("revoke-site-admin")]
    public async Task SiteAdminRoleChange_WhenTheKeycloakIdentityIsStale_Returns422(string route)
    {
        // The link row exists, Keycloak no longer has the subject: a 404 on the role read.
        ResetKeycloak();
        var keycloak = _fixture.Factory.MockKeycloakAdminService;
        keycloak.HasRealmRoleError_ = true;
        keycloak.HasRealmRoleErrorStatusCode = StatusCodes.Status404NotFound;
        var (targetId, _) = await DatabaseTestUtils.CreateLinkedUserAsync("ua-stale");
        var (_, adminToken) = await DatabaseTestUtils.CreateLinkedUserAsync("ua-stale-caller", siteAdmin: true);

        var response = await _client.SendAsync(
            TestHelpers.AuthRequest(HttpMethod.Post, $"/api/admin/users/{targetId}/{route}", adminToken));
        ResetKeycloak();

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task ReactivateUser_WhenKeycloakFails_LeavesTheUserDisabled()
    {
        ResetKeycloak();
        var (targetId, _) = await DatabaseTestUtils.CreateLinkedUserAsync("ua-react-fail");
        var (adminId, adminToken) = await DatabaseTestUtils.CreateLinkedUserAsync("ua-react-fail-admin", siteAdmin: true);
        var deactivate = await _client.SendAsync(
            TestHelpers.AuthRequest(HttpMethod.Post, $"/api/admin/users/{targetId}/deactivate", adminToken));
        Assert.Equal(HttpStatusCode.NoContent, deactivate.StatusCode);
        _fixture.Factory.MockKeycloakAdminService.EnableUserSuccess = false;

        var response = await _client.SendAsync(
            TestHelpers.AuthRequest(HttpMethod.Post, $"/api/admin/users/{targetId}/reactivate", adminToken));
        ResetKeycloak();

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal(UserStatusConstants.Disabled, await GetStatusAsync(targetId));
        Assert.Equal(0, await CountAuditAsync(SecurityAuditActions.UserReactivated, adminId, targetId));
    }
}
