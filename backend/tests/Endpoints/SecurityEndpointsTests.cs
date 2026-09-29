using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Integrations.Keycloak;
using Api.Services;
using Api.Services.BffSession;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Orkyo.Foundation.Tests.Mocks;

namespace Orkyo.Foundation.Tests.Endpoints;

[Collection("Database collection")]
public class SecurityEndpointsTests
{
    private readonly HttpClient _client;
    private readonly FoundationWebApplicationFactory _factory;
    private readonly MockKeycloakAdminService _mockKeycloak;
    private readonly DatabaseFixture _databaseFixture;
    private const string TenantSlug = TestConstants.TenantSlug;

    private readonly string _testKeycloakSub = Guid.NewGuid().ToString();
    private readonly string _testSessionId = Guid.NewGuid().ToString();

    public SecurityEndpointsTests(DatabaseFixture databaseFixture)
    {
        _databaseFixture = databaseFixture;
        _factory = databaseFixture.Factory;
        _mockKeycloak = _factory.MockKeycloakAdminService;
        _mockKeycloak.Reset(); // Start fresh for each test

        _client = _factory.CreateClient();
        _client.DefaultRequestHeaders.Add(HeaderConstants.TenantSlug, TenantSlug);
    }

    private string GetAuthToken(string? keycloakSub = null, string? sessionId = null, Guid? userId = null)
    {
        return TestConstants.BearerToken((userId ?? Guid.NewGuid()).ToString(), $"securitytest_{Guid.NewGuid()}@example.com", "Security Test User", TestConstants.TenantId.ToString(), TestConstants.TenantSlug,
            isTenantAdmin: false, role: "admin", sub: keycloakSub ?? _testKeycloakSub, sid: sessionId ?? _testSessionId);
    }

    /// <summary>
    /// Creates a real user in control_plane and links it to a Keycloak subject.
    /// Required for endpoints that call principal.RequireUserId() — the
    /// ContextEnrichmentMiddleware resolves UserId via user_identities lookup.
    /// </summary>
    private async Task<(Guid userId, string keycloakSub)> CreateLinkedTestUserAsync()
    {
        var email = $"profile_{Guid.NewGuid():N}@example.com";
        var keycloakSub = Guid.NewGuid().ToString();
        var userId = await DatabaseTestUtils.CreateTestUserAsync(email, "Profile User", role: "viewer", active: true);

        var controlPlaneConn = _databaseFixture.ControlPlaneConnectionString;
        await using var conn = new NpgsqlConnection(controlPlaneConn);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            @"INSERT INTO user_identities (id, user_id, provider, provider_subject, provider_email, created_at)
              VALUES (@id, @userId, 'keycloak', @sub, @email, NOW())",
            conn);
        cmd.Parameters.AddWithValue("id", Guid.NewGuid());
        cmd.Parameters.AddWithValue("userId", userId);
        cmd.Parameters.AddWithValue("sub", keycloakSub);
        cmd.Parameters.AddWithValue("email", email);
        await cmd.ExecuteNonQueryAsync();

        return (userId, keycloakSub);
    }

    #region Change Password Tests

    [Fact]
    public async Task ChangePassword_WithValidData_ShouldReturn200()
    {
        // Arrange
        var token = GetAuthToken();
        _mockKeycloak.ChangePasswordSuccess = true;

        var request = TestHelpers.AuthRequest(HttpMethod.Post, "/api/account/password", token, new
        {
            currentPassword = "OldPass123!",
            newPassword = "NewPass456!",
            confirmPassword = "NewPass456!"
        });

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadFromJsonAsync<JsonElement>();
        content.GetProperty("message").GetString().Should().Be("Password changed successfully");

        _mockKeycloak.ChangePasswordCallCount.Should().Be(1);
        _mockKeycloak.LastChangePasswordCall.newPassword.Should().Be("NewPass456!");
    }

    [Fact]
    public async Task ChangePassword_WithMissingCurrentPassword_ShouldReturn400()
    {
        // Arrange
        var token = GetAuthToken();

        var request = TestHelpers.AuthRequest(HttpMethod.Post, "/api/account/password", token, new
        {
            newPassword = "NewPass456!",
            confirmPassword = "NewPass456!"
        });

        // Act
        var response = await _client.SendAsync(request);

        // Assert — FluentValidation returns a ValidationProblem with an "errors"
        // dictionary, not the {"error":...} shape that ErrorResponses.BadRequest uses.
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var content = await response.Content.ReadFromJsonAsync<JsonElement>();
        var allMessages = content.GetProperty("errors").EnumerateObject()
            .SelectMany(p => p.Value.EnumerateArray().Select(v => v.GetString() ?? ""))
            .ToList();
        allMessages.Should().Contain(m => m.Contains("Current password", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ChangePassword_WithMissingNewPassword_ShouldReturn400()
    {
        // Arrange
        var token = GetAuthToken();

        var request = TestHelpers.AuthRequest(HttpMethod.Post, "/api/account/password", token, new
        {
            currentPassword = "OldPass123!"
        });

        // Act
        var response = await _client.SendAsync(request);

        // Assert — FluentValidation ValidationProblem uses "errors" (plural), not "error".
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var content = await response.Content.ReadFromJsonAsync<JsonElement>();
        var allMessages = content.GetProperty("errors").EnumerateObject()
            .SelectMany(p => p.Value.EnumerateArray().Select(v => v.GetString() ?? ""))
            .ToList();
        allMessages.Should().Contain(m => m.Contains("New password", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ChangePassword_WithShortPassword_ShouldReturn400()
    {
        // Arrange
        var token = GetAuthToken();

        var request = TestHelpers.AuthRequest(HttpMethod.Post, "/api/account/password", token, new
        {
            currentPassword = "OldPass123!",
            newPassword = "Short1!",
            confirmPassword = "Short1!"
        });

        // Act
        var response = await _client.SendAsync(request);

        // Assert — FluentValidation ValidationProblem uses "errors" (plural), not "error".
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var content = await response.Content.ReadFromJsonAsync<JsonElement>();
        var allMessages = content.GetProperty("errors").EnumerateObject()
            .SelectMany(p => p.Value.EnumerateArray().Select(v => v.GetString() ?? ""))
            .ToList();
        allMessages.Should().Contain(m => m.Contains("at least 8 characters", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ChangePassword_WithMismatchedPasswords_ShouldReturn400()
    {
        // Arrange
        var token = GetAuthToken();

        var request = TestHelpers.AuthRequest(HttpMethod.Post, "/api/account/password", token, new
        {
            currentPassword = "OldPass123!",
            newPassword = "NewPass456!",
            confirmPassword = "DifferentPass789!"
        });

        // Act
        var response = await _client.SendAsync(request);

        // Assert — FluentValidation ValidationProblem uses "errors" (plural), not "error".
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var content = await response.Content.ReadFromJsonAsync<JsonElement>();
        var allMessages = content.GetProperty("errors").EnumerateObject()
            .SelectMany(p => p.Value.EnumerateArray().Select(v => v.GetString() ?? ""))
            .ToList();
        allMessages.Should().Contain(m => m.Contains("do not match", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ChangePassword_WhenKeycloakFails_ShouldReturn400()
    {
        // Arrange
        var token = GetAuthToken();
        _mockKeycloak.ChangePasswordSuccess = false;
        _mockKeycloak.ChangePasswordError = "Current password is incorrect";

        var request = TestHelpers.AuthRequest(HttpMethod.Post, "/api/account/password", token, new
        {
            currentPassword = "WrongPass123!",
            newPassword = "NewPass456!",
            confirmPassword = "NewPass456!"
        });

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var content = await response.Content.ReadFromJsonAsync<JsonElement>();
        content.GetProperty("detail").GetString().Should().Contain("incorrect");
    }

    #endregion

    #region List Sessions Tests

    [Fact]
    public async Task GetSessions_WhenNoSessions_ShouldReturnEmptyList()
    {
        // Arrange
        var token = GetAuthToken();
        _mockKeycloak.MockSessions.Clear();

        var request = TestHelpers.AuthRequest(HttpMethod.Get, "/api/account/sessions", token);

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var sessions = await response.Content.ReadFromJsonAsync<JsonElement>();
        sessions.GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task GetSessions_WithMultipleSessions_ShouldReturnAll()
    {
        // Arrange
        var currentSessionId = Guid.NewGuid().ToString();
        var token = GetAuthToken(sessionId: currentSessionId);

        _mockKeycloak.MockSessions = new List<KeycloakSession>
        {
            MockKeycloakAdminService.CreateMockSession(currentSessionId, "192.168.1.1"),
            MockKeycloakAdminService.CreateMockSession(Guid.NewGuid().ToString(), "10.0.0.1"),
            MockKeycloakAdminService.CreateMockSession(Guid.NewGuid().ToString(), "172.16.0.1")
        };

        var request = TestHelpers.AuthRequest(HttpMethod.Get, "/api/account/sessions", token);

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var sessions = await response.Content.ReadFromJsonAsync<JsonElement>();
        sessions.GetArrayLength().Should().Be(3);

        // First session should be marked as current
        var currentSession = sessions.EnumerateArray().First(s => s.GetProperty("id").GetString() == currentSessionId);
        currentSession.GetProperty("isCurrent").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task GetSessions_EnrichesWithCapturedDevice_FallsBackForUnmatched_AndPrunesStale()
    {
        // Arrange — a linked user so the endpoint resolves a real UserId.
        var (userId, sub) = await CreateLinkedTestUserAsync();
        var currentSid = Guid.NewGuid().ToString();
        var otherSid = Guid.NewGuid().ToString();
        var staleSid = Guid.NewGuid().ToString();
        var token = GetAuthToken(keycloakSub: sub, sessionId: currentSid, userId: userId);

        // Capture device metadata for the current session + a stale row whose
        // Keycloak session no longer exists (should be pruned on list).
        using (var scope = _factory.Services.CreateScope())
        {
            var userSessions = scope.ServiceProvider.GetRequiredService<IUserSessionService>();
            await userSessions.UpsertAsync(userId, currentSid, "203.0.113.7",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/120.0 Safari/537.36");
            await userSessions.UpsertAsync(userId, staleSid, "203.0.113.9", "Chrome");
        }

        _mockKeycloak.MockSessions = new List<KeycloakSession>
        {
            MockKeycloakAdminService.CreateMockSession(currentSid, "172.16.0.1"), // proxy IP from Keycloak
            MockKeycloakAdminService.CreateMockSession(otherSid, "10.0.0.1")      // no captured row
        };

        var request = TestHelpers.AuthRequest(HttpMethod.Get, "/api/account/sessions", token);

        // Act
        var response = await _client.SendAsync(request);

        // Assert — enrichment + fallback
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var sessions = await response.Content.ReadFromJsonAsync<JsonElement>();
        var current = sessions.EnumerateArray().First(s => s.GetProperty("id").GetString() == currentSid);
        current.GetProperty("deviceLabel").GetString().Should().Be("Chrome on Windows");
        current.GetProperty("browser").GetString().Should().Be("Chrome");
        current.GetProperty("operatingSystem").GetString().Should().Be("Windows");
        current.GetProperty("deviceType").GetString().Should().Be("desktop");
        current.GetProperty("ipAddress").GetString().Should().Be("203.0.113.7"); // real, not the proxy IP

        var other = sessions.EnumerateArray().First(s => s.GetProperty("id").GetString() == otherSid);
        other.GetProperty("deviceLabel").ValueKind.Should().Be(JsonValueKind.Null);
        other.GetProperty("ipAddress").GetString().Should().Be("10.0.0.1"); // falls back to Keycloak's value

        // Assert — the stale captured row was pruned
        using var verifyScope = _factory.Services.CreateScope();
        var verify = verifyScope.ServiceProvider.GetRequiredService<IUserSessionService>();
        var remaining = (await verify.GetByUserAsync(userId)).Select(r => r.KeycloakSessionId).ToList();
        remaining.Should().Contain(currentSid);
        remaining.Should().NotContain(staleSid);
    }

    #endregion

    #region Revoke Session Tests

    [Fact]
    public async Task RevokeSession_WithValidSession_ShouldReturn200()
    {
        // Arrange
        var sessionToRevoke = Guid.NewGuid().ToString();
        var token = GetAuthToken();

        _mockKeycloak.MockSessions = new List<KeycloakSession>
        {
            MockKeycloakAdminService.CreateMockSession(_testSessionId, "192.168.1.1"),
            MockKeycloakAdminService.CreateMockSession(sessionToRevoke, "10.0.0.1")
        };

        var request = TestHelpers.AuthRequest(HttpMethod.Delete, $"/api/account/sessions/{sessionToRevoke}", token);

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadFromJsonAsync<JsonElement>();
        content.GetProperty("message").GetString().Should().Be("Session revoked");

        _mockKeycloak.RevokeSessionCallCount.Should().Be(1);
        _mockKeycloak.LastRevokedSessionId.Should().Be(sessionToRevoke);
    }

    [Fact]
    public async Task RevokeSession_WithNonExistentSession_ShouldReturn404()
    {
        // Arrange
        var token = GetAuthToken();
        var nonExistentSessionId = Guid.NewGuid().ToString();

        _mockKeycloak.MockSessions = new List<KeycloakSession>
        {
            MockKeycloakAdminService.CreateMockSession(_testSessionId, "192.168.1.1")
        };

        var request = TestHelpers.AuthRequest(HttpMethod.Delete, $"/api/account/sessions/{nonExistentSessionId}", token);

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var content = await response.Content.ReadFromJsonAsync<JsonElement>();
        content.GetProperty("detail").GetString().Should().Contain("not found");
    }

    #endregion

    #region Logout All Tests

    [Fact]
    public async Task LogoutAll_ShouldReturn200()
    {
        // Arrange
        var token = GetAuthToken();
        _mockKeycloak.MockSessions = new List<KeycloakSession>
        {
            MockKeycloakAdminService.CreateMockSession(_testSessionId, "192.168.1.1"),
            MockKeycloakAdminService.CreateMockSession(Guid.NewGuid().ToString(), "10.0.0.1")
        };

        var request = TestHelpers.AuthRequest(HttpMethod.Post, "/api/account/logout-all", token);

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadFromJsonAsync<JsonElement>();
        content.GetProperty("message").GetString().Should().Contain("Logged out");

        _mockKeycloak.LogoutAllCallCount.Should().Be(1);
    }

    #endregion

    #region Security Info Tests

    [Fact]
    public async Task GetSecurityInfo_ForLocalUser_ShouldReturnCanChangePassword()
    {
        // Arrange
        var token = GetAuthToken();
        _mockKeycloak.IsFederatedUser = false;

        var request = TestHelpers.AuthRequest(HttpMethod.Get, "/api/account/security-info", token);

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadFromJsonAsync<JsonElement>();
        content.GetProperty("isFederated").GetBoolean().Should().BeFalse();
        content.GetProperty("canChangePassword").GetBoolean().Should().BeTrue();
        content.TryGetProperty("identityProvider", out _).Should().BeTrue(); // Property exists but null
    }

    [Fact]
    public async Task GetSecurityInfo_ForFederatedUser_ShouldReturnCannotChangePassword()
    {
        // Arrange
        var token = GetAuthToken();
        _mockKeycloak.IsFederatedUser = true;
        _mockKeycloak.FederatedIdentityProvider = "google";

        var request = TestHelpers.AuthRequest(HttpMethod.Get, "/api/account/security-info", token);

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadFromJsonAsync<JsonElement>();
        content.GetProperty("isFederated").GetBoolean().Should().BeTrue();
        content.GetProperty("canChangePassword").GetBoolean().Should().BeFalse();
        content.GetProperty("identityProvider").GetString().Should().Be("google");
    }

    #endregion

    #region MFA Status Tests

    [Fact]
    public async Task GetMfaStatus_WhenTotpDisabled_ShouldReturnDisabled()
    {
        var token = GetAuthToken();
        _mockKeycloak.MockMfaStatus = new MfaStatus { TotpEnabled = false, RecoveryCodesConfigured = false };

        var request = TestHelpers.AuthRequest(HttpMethod.Get, "/api/account/mfa-status", token);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadFromJsonAsync<JsonElement>();
        content.GetProperty("totpEnabled").GetBoolean().Should().BeFalse();
        content.GetProperty("recoveryCodesConfigured").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task GetMfaStatus_WhenTotpEnabled_ShouldReturnDetails()
    {
        var token = GetAuthToken();
        _mockKeycloak.MockMfaStatus = new MfaStatus
        {
            TotpEnabled = true,
            TotpCredentialId = "cred-123",
            TotpLabel = "Google Authenticator",
            TotpCreatedDate = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            RecoveryCodesConfigured = true,
        };

        var request = TestHelpers.AuthRequest(HttpMethod.Get, "/api/account/mfa-status", token);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadFromJsonAsync<JsonElement>();
        content.GetProperty("totpEnabled").GetBoolean().Should().BeTrue();
        content.GetProperty("totpCredentialId").GetString().Should().Be("cred-123");
        content.GetProperty("totpLabel").GetString().Should().Be("Google Authenticator");
        content.GetProperty("recoveryCodesConfigured").GetBoolean().Should().BeTrue();
    }

    #endregion

    #region Enable MFA Tests

    [Fact]
    public async Task EnableMfa_WhenNotEnabled_ShouldReturn200()
    {
        var token = GetAuthToken();
        _mockKeycloak.MockMfaStatus = new MfaStatus { TotpEnabled = false };

        var request = TestHelpers.AuthRequest(HttpMethod.Post, "/api/account/mfa", token);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _mockKeycloak.EnableMfaCallCount.Should().Be(1);
    }

    [Fact]
    public async Task EnableMfa_WhenAlreadyEnabled_ShouldReturn400()
    {
        var token = GetAuthToken();
        _mockKeycloak.MockMfaStatus = new MfaStatus { TotpEnabled = true, TotpCredentialId = "cred-1" };

        var request = TestHelpers.AuthRequest(HttpMethod.Post, "/api/account/mfa", token);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _mockKeycloak.EnableMfaCallCount.Should().Be(0);
        var content = await response.Content.ReadFromJsonAsync<JsonElement>();
        content.GetProperty("detail").GetString().Should().Contain("already enabled");
    }

    #endregion

    #region Remove MFA Tests

    private static HttpRequestMessage RemoveMfaRequest(string? currentPassword, string? currentCode = "123456") =>
        new(HttpMethod.Delete, "/api/account/mfa") { Content = JsonContent.Create(new { currentPassword, currentCode }) };

    [Fact]
    public async Task RemoveMfa_WithoutCurrentPassword_Returns400AndKeepsMfa()
    {
        var token = GetAuthToken();
        _mockKeycloak.MockMfaStatus = new MfaStatus { TotpEnabled = true, TotpCredentialId = "totp-cred-id" };

        foreach (var request in new[] { new HttpRequestMessage(HttpMethod.Delete, "/api/account/mfa"), RemoveMfaRequest(null) })
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            var response = await _client.SendAsync(request);
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        _mockKeycloak.DeleteCredentialCallCount.Should().Be(0);
    }

    [Fact]
    public async Task RemoveMfa_WithWrongPassword_Returns400AndKeepsMfa()
    {
        var token = GetAuthToken();
        _mockKeycloak.MockMfaStatus = new MfaStatus { TotpEnabled = true, TotpCredentialId = "totp-cred-id" };
        _mockKeycloak.VerifyPasswordSuccess = false;

        var request = RemoveMfaRequest("wrong-password");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _mockKeycloak.VerifyPasswordCallCount.Should().Be(1);
        _mockKeycloak.DeleteCredentialCallCount.Should().Be(0);
    }

    [Fact]
    public async Task RemoveMfa_TotpUser_WithPasswordAndCurrentCode_RemovesMfa()
    {
        // The realm's direct-grant flow rejects a TOTP user's password grant without the code;
        // the endpoint must hand the code through or MFA removal can never succeed.
        var token = GetAuthToken();
        _mockKeycloak.MockMfaStatus = new MfaStatus { TotpEnabled = true, TotpCredentialId = "totp-cred-id" };
        _mockKeycloak.RequireTotpForPasswordGrant = true;
        _mockKeycloak.AcceptedTotp = "654321";

        var request = RemoveMfaRequest("current-password", "654321");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _mockKeycloak.LastVerifiedTotp.Should().Be("654321");
        _mockKeycloak.DeleteCredentialCallCount.Should().Be(1);
    }

    [Fact]
    public async Task RemoveMfa_TotpUser_WithWrongCode_Returns400AndKeepsMfa()
    {
        var token = GetAuthToken();
        _mockKeycloak.MockMfaStatus = new MfaStatus { TotpEnabled = true, TotpCredentialId = "totp-cred-id" };
        _mockKeycloak.RequireTotpForPasswordGrant = true;
        _mockKeycloak.AcceptedTotp = "654321";

        var request = RemoveMfaRequest("current-password", "111111");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _mockKeycloak.VerifyPasswordCallCount.Should().Be(1);
        _mockKeycloak.DeleteCredentialCallCount.Should().Be(0);
    }

    [Fact]
    public async Task RemoveMfa_WithoutCurrentCode_Returns400BeforeKeycloak()
    {
        var token = GetAuthToken();
        _mockKeycloak.MockMfaStatus = new MfaStatus { TotpEnabled = true, TotpCredentialId = "totp-cred-id" };

        foreach (var code in new[] { null, "", "12345", "abcdef" })
        {
            var request = RemoveMfaRequest("current-password", code);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            var response = await _client.SendAsync(request);
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest, $"code '{code}' must fail validation");
        }

        _mockKeycloak.VerifyPasswordCallCount.Should().Be(0);
        _mockKeycloak.DeleteCredentialCallCount.Should().Be(0);
    }

    [Fact]
    public async Task RemoveMfa_WhenEnabled_ShouldDeleteTotpCredential()
    {
        var token = GetAuthToken();
        _mockKeycloak.MockMfaStatus = new MfaStatus
        {
            TotpEnabled = true,
            TotpCredentialId = "totp-cred-id",
            RecoveryCodesConfigured = false,
        };

        var request = RemoveMfaRequest("current-password");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _mockKeycloak.DeleteCredentialCallCount.Should().Be(1);
        _mockKeycloak.LastDeletedCredentialId.Should().Be("totp-cred-id");
    }

    [Fact]
    public async Task RemoveMfa_WithRecoveryCodes_ShouldDeleteBothCredentials()
    {
        var token = GetAuthToken();
        _mockKeycloak.MockMfaStatus = new MfaStatus
        {
            TotpEnabled = true,
            TotpCredentialId = "totp-cred-id",
            RecoveryCodesConfigured = true,
            RecoveryCodesCredentialId = "recovery-cred-id",
        };

        var request = RemoveMfaRequest("current-password");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _mockKeycloak.DeleteCredentialCallCount.Should().Be(2);
    }

    [Fact]
    public async Task RemoveMfa_WhenNotEnabled_ShouldReturn400()
    {
        var token = GetAuthToken();
        _mockKeycloak.MockMfaStatus = new MfaStatus { TotpEnabled = false };

        var request = RemoveMfaRequest("current-password");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _mockKeycloak.DeleteCredentialCallCount.Should().Be(0);
    }

    #endregion

    #region Get Profile Tests

    [Fact]
    public async Task GetProfile_ShouldReturnUserProfile()
    {
        var token = GetAuthToken();
        _mockKeycloak.MockUserProfile = new UserProfile
        {
            Email = "alice@example.com",
            FirstName = "Alice",
            LastName = "Smith",
            EmailVerified = true,
        };

        var request = TestHelpers.AuthRequest(HttpMethod.Get, "/api/account/profile", token);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadFromJsonAsync<JsonElement>();
        content.GetProperty("email").GetString().Should().Be("alice@example.com");
        content.GetProperty("firstName").GetString().Should().Be("Alice");
        content.GetProperty("lastName").GetString().Should().Be("Smith");
        content.GetProperty("emailVerified").GetBoolean().Should().BeTrue();
    }

    #endregion

    #region Update Profile Tests

    [Fact]
    public async Task UpdateProfile_WithValidNames_ShouldReturn200AndSyncDisplayName()
    {
        var (userId, sub) = await CreateLinkedTestUserAsync();
        var token = GetAuthToken(keycloakSub: sub, userId: userId);

        var request = TestHelpers.AuthRequest(HttpMethod.Put, "/api/account/profile", token, new { firstName = "Alice", lastName = "Smith" });

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadFromJsonAsync<JsonElement>();
        content.GetProperty("displayName").GetString().Should().Be("Alice Smith");

        _mockKeycloak.UpdateProfileCallCount.Should().Be(1);
        _mockKeycloak.LastUpdateProfileCall.firstName.Should().Be("Alice");
        _mockKeycloak.LastUpdateProfileCall.lastName.Should().Be("Smith");
    }

    [Fact]
    public async Task UpdateProfile_WithBothNamesEmpty_ShouldReturn400()
    {
        var token = GetAuthToken();

        var request = TestHelpers.AuthRequest(HttpMethod.Put, "/api/account/profile", token, new { firstName = "   ", lastName = "" });

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _mockKeycloak.UpdateProfileCallCount.Should().Be(0);
    }

    #endregion

    #region Shared/locked account session privacy

    // On the shared demo identity every visitor is the same Keycloak user, so exposing or affecting
    // other sessions leaks IPs and lets one visitor sign out others. When IAccountMutationGuard reports
    // the account locked, the session endpoints restrict to the caller's own current session. The guard
    // is toggled here (see TestAccountMutationGuard); the demo -> locked detection is covered by the
    // SaaS DemoAccountLockTests. Locked is always reset in a finally so it can't bleed across tests.
    private static System.Net.Http.Headers.AuthenticationHeaderValue Bearer(string token) => new("Bearer", token);

    [Fact]
    public async Task GetSessions_LockedAccount_ReturnsOnlyCurrentSession()
    {
        var (userId, sub) = await CreateLinkedTestUserAsync();
        var currentSid = Guid.NewGuid().ToString();
        var token = GetAuthToken(keycloakSub: sub, sessionId: currentSid, userId: userId);

        _mockKeycloak.MockSessions = new List<KeycloakSession>
        {
            MockKeycloakAdminService.CreateMockSession(currentSid, "203.0.113.7"),              // the caller's own
            MockKeycloakAdminService.CreateMockSession(Guid.NewGuid().ToString(), "10.0.0.1"),  // another visitor
            MockKeycloakAdminService.CreateMockSession(Guid.NewGuid().ToString(), "172.16.0.1"),// another visitor
        };

        _factory.AccountGuard.Locked = true;
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/api/account/sessions");
            request.Headers.Authorization = Bearer(token);
            var response = await _client.SendAsync(request);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var sessions = await response.Content.ReadFromJsonAsync<JsonElement>();
            sessions.GetArrayLength().Should().Be(1, "a shared/locked account must not see other visitors' sessions");
            sessions[0].GetProperty("id").GetString().Should().Be(currentSid);
            sessions[0].GetProperty("isCurrent").GetBoolean().Should().BeTrue();
        }
        finally { _factory.AccountGuard.Locked = false; }
    }

    [Fact]
    public async Task RevokeSession_LockedAccount_CannotRevokeAnotherVisitorsSession_Returns404()
    {
        var (userId, sub) = await CreateLinkedTestUserAsync();
        var currentSid = Guid.NewGuid().ToString();
        var otherSid = Guid.NewGuid().ToString();
        var token = GetAuthToken(keycloakSub: sub, sessionId: currentSid, userId: userId);

        _mockKeycloak.MockSessions = new List<KeycloakSession>
        {
            MockKeycloakAdminService.CreateMockSession(currentSid, "203.0.113.7"),
            MockKeycloakAdminService.CreateMockSession(otherSid, "10.0.0.1"),
        };

        _factory.AccountGuard.Locked = true;
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/account/sessions/{otherSid}");
            request.Headers.Authorization = Bearer(token);
            var response = await _client.SendAsync(request);

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
            _mockKeycloak.RevokeSessionCallCount.Should().Be(0, "another visitor's session must never be revocable");
        }
        finally { _factory.AccountGuard.Locked = false; }
    }

    [Fact]
    public async Task RevokeSession_LockedAccount_CanRevokeOwnCurrentSession()
    {
        var (userId, sub) = await CreateLinkedTestUserAsync();
        var currentSid = Guid.NewGuid().ToString();
        var token = GetAuthToken(keycloakSub: sub, sessionId: currentSid, userId: userId);

        _mockKeycloak.MockSessions = new List<KeycloakSession>
        {
            MockKeycloakAdminService.CreateMockSession(currentSid, "203.0.113.7"),
            MockKeycloakAdminService.CreateMockSession(Guid.NewGuid().ToString(), "10.0.0.1"),
        };

        _factory.AccountGuard.Locked = true;
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/account/sessions/{currentSid}");
            request.Headers.Authorization = Bearer(token);
            var response = await _client.SendAsync(request);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            _mockKeycloak.LastRevokedSessionId.Should().Be(currentSid);
        }
        finally { _factory.AccountGuard.Locked = false; }
    }

    [Fact]
    public async Task LogoutAll_LockedAccount_ScopesToCurrentSessionOnly()
    {
        var (userId, sub) = await CreateLinkedTestUserAsync();
        var currentSid = Guid.NewGuid().ToString();
        var token = GetAuthToken(keycloakSub: sub, sessionId: currentSid, userId: userId);

        _factory.AccountGuard.Locked = true;
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/account/logout-all");
            request.Headers.Authorization = Bearer(token);
            var response = await _client.SendAsync(request);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            // Must NOT globally sign out every visitor -- only the caller's own current session.
            _mockKeycloak.LogoutAllCallCount.Should().Be(0);
            _mockKeycloak.RevokeSessionCallCount.Should().Be(1);
            _mockKeycloak.LastRevokedSessionId.Should().Be(currentSid);
        }
        finally { _factory.AccountGuard.Locked = false; }
    }

    [Fact]
    public async Task LogoutAll_NormalAccount_EndsEveryBffSessionOfTheCaller()
    {
        // The BFF sessions authenticate from their stored access token, so a Keycloak-side
        // logout alone would leave the caller's other devices signed in.
        var (userId, sub) = await CreateLinkedTestUserAsync();
        var token = GetAuthToken(keycloakSub: sub, userId: userId);
        var store = _factory.Services.GetRequiredService<IBffSessionStore>();
        BffSessionRecord Session(Guid owner) => new()
        {
            SessionId = Guid.NewGuid().ToString("N"),
            UserId = owner.ToString(),
            ExternalSubject = sub,
            AccessToken = "a",
            RefreshToken = "r",
            IdToken = "i",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            CreatedAt = DateTimeOffset.UtcNow,
        };
        var otherDevice = Session(userId);
        var someoneElse = Session(Guid.NewGuid());
        await store.SetAsync(otherDevice);
        await store.SetAsync(someoneElse);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/account/logout-all");
        request.Headers.Authorization = Bearer(token);
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await store.GetAsync(otherDevice.SessionId)).Should().BeNull();
        (await store.GetAsync(someoneElse.SessionId)).Should().NotBeNull();
    }

    [Fact]
    public async Task LogoutAll_NormalAccount_SignsOutGloballyAsBefore()
    {
        var (userId, sub) = await CreateLinkedTestUserAsync();
        var token = GetAuthToken(keycloakSub: sub, userId: userId);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/account/logout-all");
        request.Headers.Authorization = Bearer(token);
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _mockKeycloak.LogoutAllCallCount.Should().Be(1, "non-locked accounts keep the global sign-out-everywhere");
    }

    #endregion
}
