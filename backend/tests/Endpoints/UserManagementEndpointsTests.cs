using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Api.Endpoints;
using Api.Models;
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

    public UserManagementEndpointsTests(DatabaseFixture databaseFixture)
    {
        // The seeded test user has admin role in DatabaseFixture
        _client = databaseFixture.CreateAuthorizedClient();
        _unauthenticatedClient = databaseFixture.Factory.CreateClient();
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };
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

    #endregion

    #region GET /api/invitations/validate (Public)

    [Fact]
    public async Task ValidateInvitation_MissingToken_Returns400()
    {
        var response = await _unauthenticatedClient.GetAsync("/api/invitations/validate?token=");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ValidateInvitation_InvalidToken_Returns400()
    {
        var response = await _unauthenticatedClient.GetAsync(
            "/api/invitations/validate?token=invalid-token-value");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
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
