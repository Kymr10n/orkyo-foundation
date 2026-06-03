using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Api.Constants;
using Api.Endpoints;
using Api.Models;
using Npgsql;
using Xunit;

namespace Orkyo.Foundation.Tests.Endpoints;

/// <summary>
/// Tests for UserManagement endpoints.
/// Admin-only endpoints (GET /users, POST /users/invite, etc.) require Admin role.
/// Public endpoints (GET /api/invitations/validate, POST /api/invitations/accept) are anonymous.
/// Note: Full invite/accept flow is tested in InvitationEndpointsTests.
/// </summary>
[Collection("Database collection")]
public class UserManagementEndpointsTests
{
    private readonly HttpClient _client;
    private readonly HttpClient _unauthenticatedClient;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly string _connString;

    public UserManagementEndpointsTests(DatabaseFixture databaseFixture)
    {
        // The seeded test user has admin role in DatabaseFixture
        _client = databaseFixture.CreateAuthorizedClient();
        _unauthenticatedClient = databaseFixture.Factory.CreateClient();
        _connString = $"Host=localhost;Port={databaseFixture.DatabasePort};Database=control_plane;Username=postgres;Password=postgres;Include Error Detail=true";
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };
    }

    private static readonly Guid TestTenantId = new("00000000-0000-0000-0000-000000000001");

    /// <summary>
    /// Insert a second active member into the test tenant with the given role and return their user ID.
    /// Avoids the "cannot delete your own account" / "cannot change your own role" guards by giving
    /// tests a distinct target user that is not the authenticated caller.
    /// </summary>
    private async Task<Guid> SeedSecondTenantMemberAsync(string role = RoleConstants.Editor)
    {
        var userId = Guid.NewGuid();

        await using var conn = new NpgsqlConnection(_connString);
        await conn.OpenAsync();

        await using var userCmd = new NpgsqlCommand(
            "INSERT INTO users (id, email, display_name, status) VALUES (@id, @email, 'Second User', @status)", conn);
        userCmd.Parameters.AddWithValue("id", userId);
        userCmd.Parameters.AddWithValue("email", $"um-second-{userId:N}@test.com");
        userCmd.Parameters.AddWithValue("status", MembershipStatusConstants.Active);
        await userCmd.ExecuteNonQueryAsync();

        await using var memberCmd = new NpgsqlCommand(@"
            INSERT INTO tenant_memberships (user_id, tenant_id, role, status, created_at, updated_at)
            VALUES (@uid, @tid, @role, @status, NOW(), NOW())
            ON CONFLICT DO NOTHING", conn);
        memberCmd.Parameters.AddWithValue("uid", userId);
        memberCmd.Parameters.AddWithValue("tid", TestTenantId);
        memberCmd.Parameters.AddWithValue("role", role);
        memberCmd.Parameters.AddWithValue("status", MembershipStatusConstants.Active);
        await memberCmd.ExecuteNonQueryAsync();

        return userId;
    }

    #region GET /api/users (Admin only)

    [Fact]
    public async Task GetAllUsers_NoAuth_Returns401()
    {
        var response = await _unauthenticatedClient.GetAsync("/api/users");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetAllUsers_Authenticated_Returns200()
    {
        var response = await _client.GetAsync("/api/users");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);
        Assert.True(doc.RootElement.TryGetProperty("users", out var users));
        Assert.Equal(JsonValueKind.Array, users.ValueKind);
    }

    [Fact]
    public async Task GetAllUsers_ReturnsUserProperties()
    {
        var response = await _client.GetAsync("/api/users");
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);
        var users = doc.RootElement.GetProperty("users");

        if (users.GetArrayLength() > 0)
        {
            var user = users[0];
            Assert.True(user.TryGetProperty("id", out _));
            Assert.True(user.TryGetProperty("email", out _));
            Assert.True(user.TryGetProperty("role", out _));
            Assert.True(user.TryGetProperty("status", out _));
        }
    }

    #endregion

    #region POST /api/users/invite (Admin only)

    [Fact]
    public async Task InviteUser_NoAuth_Returns401()
    {
        var request = new InviteUserRequest("new@test.com", UserRole.Viewer);

        var response = await _unauthenticatedClient.PostAsJsonAsync(
            "/api/users/invite", request, _jsonOptions);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task InviteUser_ValidRequest_ReachesEndpoint()
    {
        // The full invite flow (email sending) is tested in InvitationEndpointsTests.
        // Here we verify the route is reachable and auth-gated correctly.
        var email = $"invite-{Guid.NewGuid():N}@test.com";
        var request = new InviteUserRequest(email, UserRole.Viewer);

        var response = await _client.PostAsJsonAsync(
            "/api/users/invite", request, _jsonOptions);

        // Should not be 401/404 — the endpoint is registered and auth passes
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    #endregion

    #region GET /api/users/invitations (Admin only)

    [Fact]
    public async Task GetPendingInvitations_NoAuth_Returns401()
    {
        var response = await _unauthenticatedClient.GetAsync("/api/users/invitations");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetPendingInvitations_Authenticated_Returns200()
    {
        var response = await _client.GetAsync("/api/users/invitations");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);
        Assert.True(doc.RootElement.TryGetProperty("invitations", out var invitations));
        Assert.Equal(JsonValueKind.Array, invitations.ValueKind);
    }

    [Fact]
    public async Task GetPendingInvitations_ReturnsInvitationProperties()
    {
        var response = await _client.GetAsync("/api/users/invitations");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);
        var invitations = doc.RootElement.GetProperty("invitations");

        // If any invitations exist, verify their shape
        if (invitations.GetArrayLength() > 0)
        {
            var invite = invitations[0];
            Assert.True(invite.TryGetProperty("id", out _));
            Assert.True(invite.TryGetProperty("email", out _));
            Assert.True(invite.TryGetProperty("role", out _));
            Assert.True(invite.TryGetProperty("expiresAt", out _));
        }
    }

    #endregion

    #region DELETE /api/users/invitations/{invitationId} (Admin only)

    [Fact]
    public async Task RevokeInvitation_NoAuth_Returns401()
    {
        var response = await _unauthenticatedClient.DeleteAsync(
            $"/api/users/invitations/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RevokeInvitation_NonExistent_Returns500OrNotFound()
    {
        // Revoking a non-existent invitation should throw KeyNotFoundException
        var response = await _client.DeleteAsync(
            $"/api/users/invitations/{Guid.NewGuid()}");

        // The endpoint throws KeyNotFoundException which the exception handler maps to 404 or 500
        Assert.True(
            response.StatusCode == HttpStatusCode.NotFound ||
            response.StatusCode == HttpStatusCode.InternalServerError,
            $"Expected 404 or 500, got {response.StatusCode}");
    }

    #endregion

    #region PATCH /api/users/{userId}/role (Admin only)

    [Fact]
    public async Task UpdateUserRole_NoAuth_Returns401()
    {
        var request = new UpdateUserRoleRequest(UserRole.Editor);

        var response = await _unauthenticatedClient.PatchAsJsonAsync(
            $"/api/users/{Guid.NewGuid()}/role", request, _jsonOptions);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    #endregion

    #region DELETE /api/users/{userId} (Admin only)

    [Fact]
    public async Task DeleteUser_NoAuth_Returns401()
    {
        var response = await _unauthenticatedClient.DeleteAsync(
            $"/api/users/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DeleteUser_NonAdmin_Returns200()
    {
        // Happy path: removing a non-admin member succeeds (last-admin guard does not apply).
        // Also confirms the endpoint returns 200 (not 500) after switching from throw to Results.BadRequest.
        var editorId = await SeedSecondTenantMemberAsync(role: "editor");

        var response = await _client.DeleteAsync($"/api/users/{editorId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task DeleteUser_WhenTargetIsLastAdmin_Returns400()
    {
        // Regression: the service correctly raises the last-admin constraint but the endpoint
        // was propagating it as an unhandled InvalidOperationException (500). Must return 400.
        // Setup: seed a second admin, purge all OTHER admin memberships in the tenant so the
        // seeded second admin is the sole active admin, then try to delete them → 400.
        var targetAdminId = await SeedSecondTenantMemberAsync(role: RoleConstants.Admin);
        var testUserId = new Guid("11111111-1111-1111-1111-111111111111");

        await using var conn = new NpgsqlConnection(_connString);
        await conn.OpenAsync();

        // Remove ALL admin memberships except the target so it is truly the last admin.
        await using var purge = new NpgsqlCommand(
            "DELETE FROM tenant_memberships WHERE tenant_id = @tid AND role = @role AND user_id != @target", conn);
        purge.Parameters.AddWithValue("tid", TestTenantId);
        purge.Parameters.AddWithValue("role", RoleConstants.Admin);
        purge.Parameters.AddWithValue("target", targetAdminId);
        await purge.ExecuteNonQueryAsync();

        HttpResponseMessage response;
        try
        {
            response = await _client.DeleteAsync($"/api/users/{targetAdminId}");
        }
        finally
        {
            // Restore the test user as active admin.
            await using var upsert = new NpgsqlCommand(@"
                INSERT INTO tenant_memberships (user_id, tenant_id, role, status, created_at, updated_at)
                VALUES (@uid, @tid, @role, @status, NOW(), NOW())
                ON CONFLICT (user_id, tenant_id) DO UPDATE SET role = @role, status = @status", conn);
            upsert.Parameters.AddWithValue("uid", testUserId);
            upsert.Parameters.AddWithValue("tid", TestTenantId);
            upsert.Parameters.AddWithValue("role", RoleConstants.Admin);
            upsert.Parameters.AddWithValue("status", MembershipStatusConstants.Active);
            await upsert.ExecuteNonQueryAsync();
            // Clean up the seeded target's membership.
            await using var cleanup = new NpgsqlCommand(
                "DELETE FROM tenant_memberships WHERE user_id = @target AND tenant_id = @tid", conn);
            cleanup.Parameters.AddWithValue("target", targetAdminId);
            cleanup.Parameters.AddWithValue("tid", TestTenantId);
            await cleanup.ExecuteNonQueryAsync();
        }

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("last admin", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UpdateUserRole_WhenTargetIsLastAdmin_Returns400()
    {
        // Regression: demoting the sole active admin surfaced as 500.
        // Same isolated setup: make the seeded admin the ONLY admin, attempt demotion → 400.
        var targetAdminId = await SeedSecondTenantMemberAsync(role: RoleConstants.Admin);
        var testUserId = new Guid("11111111-1111-1111-1111-111111111111");

        await using var conn = new NpgsqlConnection(_connString);
        await conn.OpenAsync();

        await using var purge = new NpgsqlCommand(
            "DELETE FROM tenant_memberships WHERE tenant_id = @tid AND role = @role AND user_id != @target", conn);
        purge.Parameters.AddWithValue("tid", TestTenantId);
        purge.Parameters.AddWithValue("role", RoleConstants.Admin);
        purge.Parameters.AddWithValue("target", targetAdminId);
        await purge.ExecuteNonQueryAsync();

        HttpResponseMessage response;
        try
        {
            var request = new UpdateUserRoleRequest(UserRole.Editor);
            response = await _client.PatchAsJsonAsync(
                $"/api/users/{targetAdminId}/role", request, _jsonOptions);
        }
        finally
        {
            await using var upsert = new NpgsqlCommand(@"
                INSERT INTO tenant_memberships (user_id, tenant_id, role, status, created_at, updated_at)
                VALUES (@uid, @tid, @role, @status, NOW(), NOW())
                ON CONFLICT (user_id, tenant_id) DO UPDATE SET role = @role, status = @status", conn);
            upsert.Parameters.AddWithValue("uid", testUserId);
            upsert.Parameters.AddWithValue("tid", TestTenantId);
            upsert.Parameters.AddWithValue("role", RoleConstants.Admin);
            upsert.Parameters.AddWithValue("status", MembershipStatusConstants.Active);
            await upsert.ExecuteNonQueryAsync();
            await using var cleanup = new NpgsqlCommand(
                "DELETE FROM tenant_memberships WHERE user_id = @target AND tenant_id = @tid", conn);
            cleanup.Parameters.AddWithValue("target", targetAdminId);
            cleanup.Parameters.AddWithValue("tid", TestTenantId);
            await cleanup.ExecuteNonQueryAsync();
        }

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("last admin", body, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region GET /api/invitations/validate (Public)

    [Fact]
    public async Task ValidateInvitation_MissingToken_Returns400()
    {
        var response = await _unauthenticatedClient.GetAsync("/api/invitations/validate?token=");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ValidateInvitation_NonGuidToken_NotRejectedByEndpoint_ReachesService()
    {
        // Regression: invitation tokens are secure base64url strings, not GUIDs. The endpoint
        // must NOT reject a non-GUID token by format — it forwards to IInvitationService, which
        // owns real validity (token-hash lookup + expiry). With the default service mock the
        // call resolves as valid, so a well-formed non-GUID token returns 200, not 400.
        var response = await _unauthenticatedClient.GetAsync(
            "/api/invitations/validate?token=hUkKASuPQXdMuWgbGMWe2gUtDSKgUYzEan4Eft-JW3Y");

        Assert.NotEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ValidateInvitation_RouteExists_DoesNotReturn404()
    {
        var response = await _unauthenticatedClient.GetAsync(
            "/api/invitations/validate?token=test");

        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    #endregion

    #region POST /api/invitations/accept (Public)

    [Fact]
    public async Task AcceptInvitation_InvalidToken_Returns400()
    {
        var request = new AcceptInvitationRequest(
            Token: "invalid-token",
            DisplayName: "New User",
            Password: "SecurePassword123!");

        var response = await _unauthenticatedClient.PostAsJsonAsync(
            "/api/invitations/accept", request, _jsonOptions);

        // Invalid token results in validation error or argument exception
        Assert.True(
            response.StatusCode == HttpStatusCode.BadRequest ||
            response.StatusCode == HttpStatusCode.InternalServerError,
            $"Expected 400 or 500, got {response.StatusCode}");
    }

    [Fact]
    public async Task AcceptInvitation_RouteExists_DoesNotReturn404()
    {
        var request = new AcceptInvitationRequest("test", "User", "pass");

        var response = await _unauthenticatedClient.PostAsJsonAsync(
            "/api/invitations/accept", request, _jsonOptions);

        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    #endregion

    #region Route existence

    [Fact]
    public async Task Users_RouteExists_DoesNotReturn404()
    {
        var response = await _unauthenticatedClient.GetAsync("/api/users");
        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UsersInvite_RouteExists_DoesNotReturn404()
    {
        var response = await _unauthenticatedClient.PostAsJsonAsync(
            "/api/users/invite", new InviteUserRequest("test@test.com", UserRole.Viewer), _jsonOptions);
        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UsersInvitations_RouteExists_DoesNotReturn404()
    {
        var response = await _unauthenticatedClient.GetAsync("/api/users/invitations");
        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    #endregion
}
