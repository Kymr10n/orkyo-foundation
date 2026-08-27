using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Orkyo.Foundation.Tests.Endpoints;

/// <summary>
/// Integration tests for GET /api/session/me.
///
/// The primary purpose is to act as a contract test between the backend
/// UserInfo record and the frontend AppUser interface.  Any time the SQL
/// SELECT in GetUserByIdInternalAsync is changed (column added, renamed, or
/// removed) one of these tests will fail at the backend-tests CI step — the
/// earliest possible point — rather than silently producing wrong runtime
/// values.
/// </summary>
[Collection("Database collection")]
public class SessionEndpointsTests
{
    private readonly HttpClient _client;
    private readonly FoundationWebApplicationFactory _factory;

    public SessionEndpointsTests(DatabaseFixture databaseFixture)
    {
        _factory = databaseFixture.Factory;
        _client = databaseFixture.Factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    // ─── helpers ────────────────────────────────────────────────────────────────

    private static async Task<string> MakeTokenAsync()
    {
        var email = $"sessionme_{Guid.NewGuid()}@example.com";
        var userId = await DatabaseTestUtils.CreateTestUserAsync(
            email,
            displayName: "Session Me Test",
            tenantSlug: null,
            active: true);

        var tokenData = new
        {
            UserId = userId.ToString(),
            Email = email,
            DisplayName = "Session Me Test",
            TenantId = "00000000-0000-0000-0000-000000000001",
            TenantSlug = TestConstants.TenantSlug,
            IsTenantAdmin = false,
            Role = "user"
        };

        var json = JsonSerializer.Serialize(tokenData);
        return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json));
    }

    private async Task<JsonElement> GetMeAsync(string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/session/me");
        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(body).RootElement;
    }

    // ─── 401 guard ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetMe_WithoutAuthentication_Returns401()
    {
        var response = await _client.GetAsync("/api/session/me");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ─── response shape (contract tests) ─────────────────────────────────────────
    // These tests guard the mapping between backend UserInfo and frontend AppUser.
    // If a field is missing from the SQL SELECT or accidentally renamed, the
    // corresponding test below fails at the backend-tests CI stage.

    [Fact]
    public async Task GetMe_ReturnsId()
    {
        var token = await MakeTokenAsync();
        var me = await GetMeAsync(token);
        me.TryGetProperty("id", out var id).Should().BeTrue("field 'id' must be present");
        id.GetString().Should().NotBeNullOrEmpty();
        Guid.TryParse(id.GetString(), out _).Should().BeTrue("'id' must be a valid GUID");
    }

    [Fact]
    public async Task GetMe_ReturnsEmail()
    {
        var token = await MakeTokenAsync();
        var me = await GetMeAsync(token);
        me.TryGetProperty("email", out var email).Should().BeTrue("field 'email' must be present");
        email.GetString().Should().Contain("@");
    }

    [Fact]
    public async Task GetMe_ReturnsDisplayName()
    {
        var token = await MakeTokenAsync();
        var me = await GetMeAsync(token);
        me.TryGetProperty("displayName", out var name).Should().BeTrue("field 'displayName' must be present");
        name.GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetMe_ReturnsHasSeenTour_FalseForNewUser()
    {
        // This is the most important contract test for the tour feature:
        // it verifies that has_seen_tour is read from the database and
        // returned in the JSON response, not just defined on UserInfo.
        var token = await MakeTokenAsync();
        var me = await GetMeAsync(token);
        me.TryGetProperty("hasSeenTour", out var hasSeenTour)
            .Should().BeTrue("field 'hasSeenTour' must be present — check the SQL SELECT in GetUserByIdInternalAsync");
        hasSeenTour.GetBoolean().Should().BeFalse("new users have never seen the tour");
    }

    [Fact]
    public async Task GetMe_ReturnsCreatedAt()
    {
        var token = await MakeTokenAsync();
        var me = await GetMeAsync(token);
        me.TryGetProperty("createdAt", out _).Should().BeTrue("field 'createdAt' must be present");
    }

    // ─── Tenants array ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetMe_ReturnsTenantsArray()
    {
        var token = await MakeTokenAsync();
        var me = await GetMeAsync(token);
        me.TryGetProperty("tenants", out var tenants).Should().BeTrue("field 'tenants' must be present");
        tenants.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task GetMe_TenantEntry_SendsPlanCodeNotDisplayLabel()
    {
        // Create a user with a membership in the seeded test tenant
        var email = $"tier_test_{Guid.NewGuid()}@example.com";
        var userId = await DatabaseTestUtils.CreateTestUserAsync(
            email,
            displayName: "Tier Test User",
            tenantSlug: TestConstants.TenantSlug,
            active: true);

        var tokenData = new
        {
            UserId = userId.ToString(),
            Email = email,
            DisplayName = "Tier Test User",
            TenantId = "00000000-0000-0000-0000-000000000001",
            TenantSlug = TestConstants.TenantSlug,
            IsTenantAdmin = false,
            Role = "viewer"
        };
        var json = JsonSerializer.Serialize(tokenData);
        var token = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json));

        var me = await GetMeAsync(token);
        me.TryGetProperty("tenants", out var tenants).Should().BeTrue();
        tenants.GetArrayLength().Should().BeGreaterThan(0, "user should have at least one tenant membership");

        var tenant = tenants[0];
        tenant.TryGetProperty("tier", out var tier).Should().BeTrue(
            "field 'tier' must be present on tenant entries — check the SQL in GetTenantMembershipsAsync");

        // The SPA compares this against literal lowercase plan codes, so sending the display
        // label silently degrades every feature gate to the free plan. Assert the exact code
        // AND that it is not the label — otherwise someone "fixes" a future mismatch by
        // lowercasing the label, which breaks again the day a code stops matching its label.
        tier.GetString().Should().Be(Api.Security.Features.SinglePlanInfoProvider.PlanCode,
            "/me must carry the machine plan code");
        tier.GetString().Should().NotBe(Api.Security.Features.SinglePlanInfoProvider.PlanLabel,
            "the display label must never go on the wire where a code is expected");
    }

    [Fact]
    public async Task GetMe_TenantEntry_IncludesServerComputedEntitlements()
    {
        // Clients present features from this map instead of re-deriving them from the plan
        // code. The test host registers AllFeaturesEntitlementProvider, so every enforced
        // feature is enabled here — the point is the shape and the key set, not the values.
        var email = $"entitlements_test_{Guid.NewGuid()}@example.com";
        var userId = await DatabaseTestUtils.CreateTestUserAsync(
            email,
            displayName: "Entitlements Test User",
            tenantSlug: TestConstants.TenantSlug,
            active: true);

        var tokenData = new
        {
            UserId = userId.ToString(),
            Email = email,
            DisplayName = "Entitlements Test User",
            TenantId = "00000000-0000-0000-0000-000000000001",
            TenantSlug = TestConstants.TenantSlug,
            IsTenantAdmin = false,
            Role = "viewer"
        };
        var token = Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(tokenData)));

        var me = await GetMeAsync(token);
        var tenant = me.GetProperty("tenants")[0];

        tenant.TryGetProperty("entitlements", out var entitlements).Should().BeTrue(
            "field 'entitlements' must be present so clients never re-derive features from the plan");

        foreach (var key in Api.Security.Features.FeatureKeys.Enforced)
        {
            entitlements.TryGetProperty(key, out var enabled).Should().BeTrue(
                $"every enforced feature key must be reported — missing '{key}'");
            enabled.GetBoolean().Should().BeTrue(
                "the test host's AllFeaturesEntitlementProvider enables everything");
        }
    }

    // ─── POST /api/session/tour/seen ─────────────────────────────────────────────

    [Fact]
    public async Task TourSeen_WithoutAuthentication_Returns401()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/session/tour/seen");
        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task TourSeen_WithAuthentication_ReturnsOk()
    {
        var token = await MakeTokenAsync();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/session/tour/seen");
        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);
        doc.RootElement.TryGetProperty("marked", out var marked).Should().BeTrue();
        marked.GetBoolean().Should().BeTrue();
    }

    // ─── POST /api/session/tos/accept ────────────────────────────────────────────

    [Fact]
    public async Task TosAccept_WithoutAuthentication_Returns401()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/session/tos/accept");
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { tosVersion = "1.0" }),
            System.Text.Encoding.UTF8, "application/json");
        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task TosAccept_WithWrongVersion_Returns400()
    {
        var token = await MakeTokenAsync();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/session/tos/accept");
        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { tosVersion = "99.0" }),
            System.Text.Encoding.UTF8, "application/json");

        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Invalid ToS version");
    }

    [Fact]
    public async Task TosAccept_WithCorrectVersion_ReturnsOk()
    {
        // The required version comes from appsettings.json Tos:RequiredVersion
        const string requiredVersion = "2026-02";
        var token = await MakeTokenAsync();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/session/tos/accept");
        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { tosVersion = requiredVersion }),
            System.Text.Encoding.UTF8, "application/json");

        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);
        doc.RootElement.TryGetProperty("accepted", out var accepted).Should().BeTrue();
        accepted.GetBoolean().Should().BeTrue();
        doc.RootElement.TryGetProperty("tosVersion", out var version).Should().BeTrue();
        version.GetString().Should().Be(requiredVersion);
    }

    // ─── GET /api/session/bootstrap ──────────────────────────────────────────────

    /// <summary>
    /// Bootstrap answers the canonical problem shape on every rejection. The route reaches
    /// IIdentityLinkService, which the factory fills with StubIdentityLinkService — set its
    /// LinkResult to choose which branch the endpoint takes.
    /// </summary>
    private static string MakeKeycloakToken(
        Guid userId, string email, string? sub, string[]? realmRoles = null)
    {
        var tokenData = new
        {
            UserId = userId.ToString(),
            Email = email,
            DisplayName = "Bootstrap Test",
            TenantId = "00000000-0000-0000-0000-000000000001",
            TenantSlug = TestConstants.TenantSlug,
            IsTenantAdmin = false,
            Role = "user",
            Sub = sub,
            RealmRoles = realmRoles
        };

        var json = JsonSerializer.Serialize(tokenData);
        return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json));
    }

    private async Task<HttpResponseMessage> BootstrapAsync(string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/session/bootstrap");
        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return await _client.SendAsync(request);
    }

    [Fact]
    public async Task Bootstrap_TokenWithoutSubClaim_Returns400InvalidToken()
    {
        var token = MakeKeycloakToken(Guid.NewGuid(), "nosub@example.com", sub: null);

        var response = await BootstrapAsync(token);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        problem.GetProperty("code").GetString()
            .Should().Be(Api.Constants.ApiErrorCodes.Auth.InvalidToken);
        problem.GetProperty("detail").GetString().Should().Be("Missing 'sub' claim in token");
        problem.GetProperty("title").GetString().Should().Be("Invalid authentication token");
    }

    [Fact]
    public async Task Bootstrap_WhenIdentityLinkFails_ReturnsTheServicesErrorCode()
    {
        var stub = _factory.Services.GetRequiredService<Mocks.StubIdentityLinkService>();
        stub.LinkResult = Api.Security.IdentityLinkResult.Failed(
            Api.Constants.AccessMessages.InvitationOnly,
            Api.Constants.ApiErrorCodes.Auth.NotInvited);

        var response = await BootstrapAsync(
            MakeKeycloakToken(Guid.NewGuid(), "notinvited@example.com", sub: Guid.NewGuid().ToString()));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        // The endpoint forwards the service's code rather than flattening every failure
        // into identity_not_linked — the frontend routes invitation refusals differently.
        problem.GetProperty("code").GetString()
            .Should().Be(Api.Constants.ApiErrorCodes.Auth.NotInvited);
        problem.GetProperty("detail").GetString()
            .Should().Be(Api.Constants.AccessMessages.InvitationOnly);
        problem.GetProperty("title").GetString().Should().Be("Authentication failed");
    }

    [Fact]
    public async Task Bootstrap_WhenLinkFailsWithoutACode_FallsBackToIdentityNotLinked()
    {
        var stub = _factory.Services.GetRequiredService<Mocks.StubIdentityLinkService>();
        stub.LinkResult = new Api.Security.IdentityLinkResult
        {
            Success = false,
            Error = "Keycloak unreachable",
            ErrorCode = null
        };

        var response = await BootstrapAsync(
            MakeKeycloakToken(Guid.NewGuid(), "nocode@example.com", sub: Guid.NewGuid().ToString()));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        problem.GetProperty("code").GetString()
            .Should().Be(Api.Constants.ApiErrorCodes.Auth.IdentityNotLinked);
    }

    [Fact]
    public async Task Bootstrap_WhenLinked_ReturnsSessionWithSiteAdminFlagFromToken()
    {
        var email = $"bootstrap_{Guid.NewGuid()}@example.com";
        var userId = await DatabaseTestUtils.CreateTestUserAsync(
            email, displayName: "Bootstrap Test", tenantSlug: null, active: true);

        var stub = _factory.Services.GetRequiredService<Mocks.StubIdentityLinkService>();
        stub.LinkResult = Api.Security.IdentityLinkResult.Linked(userId, email, "Bootstrap Test");

        var response = await BootstrapAsync(
            MakeKeycloakToken(userId, email, sub: Guid.NewGuid().ToString(),
                realmRoles: [Api.Integrations.Keycloak.KeycloakClaims.SiteAdminRole]));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        // IsSiteAdmin comes from the realm role in the token, never from the database.
        body.GetProperty("isSiteAdmin").GetBoolean().Should().BeTrue();
        stub.LastToken.Should().NotBeNull();
        stub.LastToken!.Email.Should().Be(email);
    }

    // ─── Session bootstrap response — ToS text contract ──────────────────────────

    /// <summary>
    /// Contract test between SessionBootstrapResponse.TosText and the frontend TosPage:
    /// while acceptance is pending the session response carries the resolved ToS text
    /// (compiled default in this host — the factory runs in tenant context, so the
    /// site-scoped override path is covered by TenantSettingsService tests); after
    /// acceptance the field is null again. Exercised at the service level because the
    /// /api/session/bootstrap route depends on IIdentityLinkService, which this factory
    /// stubs out.
    /// </summary>
    [Fact]
    public async Task SessionResponse_TosTextPresentWhilePending_NullAfterAcceptance()
    {
        const string requiredVersion = "2026-02"; // from factory config ToS:RequiredVersion
        var email = $"tostext_{Guid.NewGuid()}@example.com";
        var userId = await DatabaseTestUtils.CreateTestUserAsync(
            email, displayName: "ToS Text Test", tenantSlug: null, active: true);

        using var scope = _factory.Services.CreateScope();
        var sessionService = scope.ServiceProvider.GetRequiredService<Api.Services.ISessionService>();

        // Fresh user → ToS pending → text present and matching the compiled default
        var pending = await sessionService.GetSessionByUserIdAsync(userId);
        pending.Should().NotBeNull();
        pending!.TosRequired.Should().BeTrue();
        pending.RequiredTosVersion.Should().Be(requiredVersion);
        pending.TosText.Should().Be(new Api.Models.TenantSettings().Tos_Text);

        // Accept the required version
        await sessionService.AcceptTosAsync(userId, requiredVersion, ipAddress: null, userAgent: null);

        // Accepted → ToS no longer pending → text no longer sent
        var after = await sessionService.GetSessionByUserIdAsync(userId);
        after.Should().NotBeNull();
        after!.TosRequired.Should().BeFalse();
        after.TosText.Should().BeNull();
    }

    // ─── POST /api/auth/create-account ───────────────────────────────────────────

    [Fact]
    public async Task CreateAccount_WhenSelfRegistrationClosed_Returns403()
    {
        var options = _factory.Services
            .GetRequiredService<Api.Configuration.IdentityProvisioningOptions>();
        options.AllowSelfRegistration = false;
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/create-account");
            request.Content = new StringContent(
                JsonSerializer.Serialize(new
                {
                    email = $"invite-only-{Guid.NewGuid():N}@example.com",
                    password = "SecurePass123!"
                }),
                System.Text.Encoding.UTF8, "application/json");

            var response = await _client.SendAsync(request);
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

            var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
            problem.GetProperty("code").GetString()
                .Should().Be(Api.Constants.ApiErrorCodes.Auth.NotInvited);
            problem.GetProperty("detail").GetString()
                .Should().Be(Api.Constants.AccessMessages.InvitationOnly);
        }
        finally
        {
            options.AllowSelfRegistration = true;
        }
    }

    [Fact]
    public async Task CreateAccount_MissingEmail_Returns400()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/create-account");
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { email = "", password = "SecurePass123!" }),
            System.Text.Encoding.UTF8, "application/json");

        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Email is required");
    }

    [Fact]
    public async Task CreateAccount_MissingPassword_Returns400()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/create-account");
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { email = "test@example.com", password = "" }),
            System.Text.Encoding.UTF8, "application/json");

        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Password is required");
    }

    [Fact]
    public async Task CreateAccount_ShortPassword_Returns400()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/create-account");
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { email = "test@example.com", password = "short" }),
            System.Text.Encoding.UTF8, "application/json");

        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Password must be at least");
    }

    [Fact]
    public async Task CreateAccount_InvalidEmail_Returns400()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/create-account");
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { email = "not-an-email", password = "SecurePass123!" }),
            System.Text.Encoding.UTF8, "application/json");

        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Invalid email format");
    }

    [Fact]
    public async Task CreateAccount_ValidRequest_ReturnsOk()
    {
        var email = $"create-test-{Guid.NewGuid():N}@example.com";
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/create-account");
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { email, password = "SecurePass123!", displayName = "Test User" }),
            System.Text.Encoding.UTF8, "application/json");

        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Account created");
    }

    [Fact]
    public async Task CreateAccount_DisplayNameSplitsIntoFirstLast()
    {
        var email = $"create-name-{Guid.NewGuid():N}@example.com";
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/create-account");
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { email, password = "SecurePass123!", displayName = "John Doe" }),
            System.Text.Encoding.UTF8, "application/json");

        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
