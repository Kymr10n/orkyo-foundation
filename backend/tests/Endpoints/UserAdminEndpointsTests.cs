using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
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
        _connString = $"Host=localhost;Port={fixture.DatabasePort};Database=control_plane;Username=postgres;Password=postgres";
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private HttpRequestMessage Auth(HttpMethod method, string url, string token)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return req;
    }

    private async Task<(Guid UserId, string Token)> CreateSiteAdminAsync(string prefix = "ua-admin")
    {
        var id = Guid.NewGuid();
        var email = $"{prefix}-{id}@test.com";
        var sub = $"kc-{id}";

        await using var conn = new Npgsql.NpgsqlConnection(_connString);
        await conn.OpenAsync();

        await using var cmd = new Npgsql.NpgsqlCommand(
            "INSERT INTO users (id, email, display_name, status) VALUES (@id, @email, 'Admin', 'active')", conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("email", email);
        await cmd.ExecuteNonQueryAsync();

        await using var linkCmd = new Npgsql.NpgsqlCommand(
            "INSERT INTO user_identities (id, user_id, provider, provider_subject, provider_email) VALUES (@id, @uid, 'keycloak', @sub, @email)", conn);
        linkCmd.Parameters.AddWithValue("id", Guid.NewGuid());
        linkCmd.Parameters.AddWithValue("uid", id);
        linkCmd.Parameters.AddWithValue("sub", sub);
        linkCmd.Parameters.AddWithValue("email", email);
        await linkCmd.ExecuteNonQueryAsync();

        var token = MakeToken(id, email, sub, isSiteAdmin: true);
        return (id, token);
    }

    private async Task<(Guid UserId, string Token)> CreateRegularUserAsync(string prefix = "ua-user")
    {
        var id = Guid.NewGuid();
        var email = $"{prefix}-{id}@test.com";
        var sub = $"kc-{id}";

        await using var conn = new Npgsql.NpgsqlConnection(_connString);
        await conn.OpenAsync();

        await using var cmd = new Npgsql.NpgsqlCommand(
            "INSERT INTO users (id, email, display_name, status) VALUES (@id, @email, 'Regular', 'active')", conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("email", email);
        await cmd.ExecuteNonQueryAsync();

        await using var linkCmd = new Npgsql.NpgsqlCommand(
            "INSERT INTO user_identities (id, user_id, provider, provider_subject, provider_email) VALUES (@id, @uid, 'keycloak', @sub, @email)", conn);
        linkCmd.Parameters.AddWithValue("id", Guid.NewGuid());
        linkCmd.Parameters.AddWithValue("uid", id);
        linkCmd.Parameters.AddWithValue("sub", sub);
        linkCmd.Parameters.AddWithValue("email", email);
        await linkCmd.ExecuteNonQueryAsync();

        var token = MakeToken(id, email, sub, isSiteAdmin: false);
        return (id, token);
    }

    private static string MakeToken(Guid userId, string email, string sub, bool isSiteAdmin)
    {
        var roles = isSiteAdmin ? new[] { "user", "site-admin" } : new[] { "user" };
        var data = new
        {
            UserId = userId.ToString(),
            Email = email,
            DisplayName = isSiteAdmin ? "Admin" : "Regular",
            TenantId = Guid.Empty.ToString(),
            TenantSlug = "",
            IsTenantAdmin = false,
            Role = "viewer",
            Sub = sub,
            RealmRoles = roles,
        };
        return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(data)));
    }

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
}
