using System.Net;
using Api.Configuration;
using Api.Middleware;
using Api.Security;
using Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Orkyo.Foundation.Tests.Mocks;
using Orkyo.Shared;

namespace Orkyo.Foundation.Tests.Endpoints;

[Trait(TestCategories.Name, TestCategories.Database)]
[Collection("Database collection")]
public class AccountLifecycleEndpointsTests
{
    private readonly HttpClient _client;
    private readonly FoundationWebApplicationFactory _factory;
    private readonly MockKeycloakAdminService _mockKeycloak;
    private readonly string _cpConnectionString;

    public AccountLifecycleEndpointsTests(DatabaseFixture databaseFixture)
    {
        var factory = databaseFixture.Factory;
        _factory = factory;
        _mockKeycloak = factory.MockKeycloakAdminService;
        _mockKeycloak.Reset();
        _cpConnectionString = databaseFixture.ControlPlaneConnectionString;

        _client = factory.CreateClient();
        // /api/account/* skips API-key and tenant-slug requirements — no default headers needed
    }

    // ─── helpers ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a user in control_plane.users with lifecycle columns set and returns
    /// the user's ID and confirm token. Optionally links a Keycloak identity (needed for
    /// testing the dormant re-enable flow).
    /// </summary>
    private async Task<(Guid userId, string token)> CreateUserWithLifecycleTokenAsync(
        string lifecycleStatus = "warned",
        string? keycloakId = null,
        int tokenExpiresInDays = 30)
    {
        var email = $"lifecycle_{Guid.NewGuid()}@example.com";
        // No tenant membership needed — the endpoint only touches control_plane.users
        var userId = await DatabaseTestUtils.CreateTestUserAsync(
            email,
            displayName: "Lifecycle Test",
            tenantSlug: null,
            active: true);

        var token = Guid.NewGuid().ToString();

        await using var conn = new NpgsqlConnection(_cpConnectionString);
        await conn.OpenAsync();

        if (keycloakId != null)
        {
            await using var kcCmd = new NpgsqlCommand(
                "INSERT INTO user_identities (user_id, provider, provider_subject) VALUES (@id, 'keycloak', @kcId)", conn);
            kcCmd.Parameters.AddWithValue("kcId", keycloakId);
            kcCmd.Parameters.AddWithValue("id", userId);
            await kcCmd.ExecuteNonQueryAsync();
        }

        await using var cmd = new NpgsqlCommand(@"
            UPDATE users
            SET lifecycle_status        = @status,
                lifecycle_warning_count = 1,
                lifecycle_last_warned_at = NOW(),
                lifecycle_confirm_token = @token,
                lifecycle_confirm_token_expires_at = NOW() + make_interval(days => @expiresInDays)
            WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("status", lifecycleStatus);
        cmd.Parameters.AddWithValue("token", SecureTokens.LifecycleConfirmTokenHash(token));
        cmd.Parameters.AddWithValue("expiresInDays", tokenExpiresInDays);
        cmd.Parameters.AddWithValue("id", userId);
        await cmd.ExecuteNonQueryAsync();

        return (userId, token);
    }

    /// <summary>
    /// Reads the lifecycle columns from control_plane.users for the given user.
    /// </summary>
    private async Task<(string? status, int warningCount, string? confirmToken)> GetUserLifecycleStateAsync(Guid userId)
    {
        await using var conn = new NpgsqlConnection(_cpConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT lifecycle_status, lifecycle_warning_count, lifecycle_confirm_token FROM users WHERE id = @id",
            conn);
        cmd.Parameters.AddWithValue("id", userId);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return (null, 0, null);
        var status = reader.IsDBNull(0) ? null : reader.GetString(0);
        var count = reader.GetInt32(1);
        var confirmToken = reader.IsDBNull(2) ? null : reader.GetString(2);
        return (status, count, confirmToken);
    }

    private Task<HttpResponseMessage> PostTokenAsync(string token) =>
        _client.PostAsync("/api/account/confirm-activity",
            new FormUrlEncodedContent([new KeyValuePair<string, string>("token", token)]));

    // ─── GET renders a confirmation form only ───────────────────────────────────

    [Fact]
    public async Task ConfirmActivity_Get_WithValidDormantToken_ShowsConfirmationForm_AndChangesNothing()
    {
        var keycloakId = Guid.NewGuid().ToString();
        var (userId, token) = await CreateUserWithLifecycleTokenAsync("dormant", keycloakId);

        var response = await _client.GetAsync($"/api/account/confirm-activity?token={token}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.ToString().Should().Contain("text/html");
        // The form's POST redirects into the app, and browsers check form-action against that
        // redirect too, so the app origin must be allowed alongside 'self'.
        var appBaseUrl = _factory.Services.GetRequiredService<IConfiguration>().GetRequired(ConfigKeys.AppBaseUrl);
        response.Headers.GetValues("Content-Security-Policy").Single()
            .Should().Be($"default-src 'none'; style-src 'unsafe-inline'; form-action 'self' {appBaseUrl}");
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("Keep your Orkyo account active");
        html.Should().Contain("<form method=\"post\">");
        html.Should().Contain($"<input type=\"hidden\" name=\"token\" value=\"{token}\">");

        // Link previews and mail scanners follow GET links; they must not confirm activity.
        _mockKeycloak.EnableUserCallCount.Should().Be(0);
        var (status, count, confirmToken) = await GetUserLifecycleStateAsync(userId);
        status.Should().Be("dormant");
        count.Should().Be(1);
        confirmToken.Should().Be(SecureTokens.LifecycleConfirmTokenHash(token));
    }

    [Fact]
    public async Task ConfirmActivity_Get_HtmlEncodesTheToken()
    {
        var response = await _client.GetAsync(
            $"/api/account/confirm-activity?token={Uri.EscapeDataString("a\"><b>x")}");

        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("value=\"a&quot;&gt;&lt;b&gt;x\"");
        html.Should().NotContain("<b>x");
    }

    [Fact]
    public void ConfirmActivity_Post_IsAnonymousCsrfExemptAndRateLimited()
    {
        var endpoint = _factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .Single(e => e.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName == "ConfirmAccountActivity");

        endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()!.HttpMethods.Should().Equal("POST");
        endpoint.Metadata.GetMetadata<IAllowAnonymous>().Should().NotBeNull();
        endpoint.Metadata.GetMetadata<SkipTenantResolutionAttribute>().Should().NotBeNull();
        endpoint.Metadata.GetMetadata<CsrfExemptAttribute>().Should().NotBeNull();
        endpoint.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName
            .Should().Be(FoundationRateLimitPolicies.BffAuth);
    }

    // ─── no-token / invalid-token ────────────────────────────────────────────────

    [Fact]
    public async Task ConfirmActivity_Get_WithNoToken_RedirectsToInvalid()
    {
        var response = await _client.GetAsync("/api/account/confirm-activity");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Contain("lifecycle=invalid");
    }

    [Fact]
    public async Task ConfirmActivity_Get_WithEmptyToken_RedirectsToInvalid()
    {
        var response = await _client.GetAsync("/api/account/confirm-activity?token=");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Contain("lifecycle=invalid");
    }

    [Fact]
    public async Task ConfirmActivity_Post_WithNoToken_RedirectsToInvalid()
    {
        var response = await _client.PostAsync("/api/account/confirm-activity", null);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Contain("lifecycle=invalid");
    }

    [Fact]
    public async Task ConfirmActivity_Post_WithUnknownToken_RedirectsToExpired()
    {
        var fakeToken = Guid.NewGuid().ToString();

        var response = await PostTokenAsync(fakeToken);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Contain("lifecycle=expired");
    }

    // ─── warned user ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task ConfirmActivity_Post_WithValidWarnedToken_ClearsLifecycleStateAndRedirectsToConfirmed()
    {
        var (userId, token) = await CreateUserWithLifecycleTokenAsync("warned");

        var response = await PostTokenAsync(token);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Contain("lifecycle=confirmed");

        // Lifecycle columns must be cleared
        var (status, count, confirmToken) = await GetUserLifecycleStateAsync(userId);
        status.Should().BeNull();
        count.Should().Be(0);
        confirmToken.Should().BeNull();

        // User was not dormant, so Keycloak re-enable should NOT be called
        _mockKeycloak.EnableUserCallCount.Should().Be(0);
    }

    // ─── dormant user ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task ConfirmActivity_Post_WithValidDormantToken_ReEnablesKeycloakAndClearsState()
    {
        var keycloakId = Guid.NewGuid().ToString();
        var (userId, token) = await CreateUserWithLifecycleTokenAsync("dormant", keycloakId);

        var response = await PostTokenAsync(token);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Contain("lifecycle=confirmed");

        // Keycloak should have been asked to re-enable the user
        _mockKeycloak.EnableUserCallCount.Should().Be(1);
        _mockKeycloak.LastEnabledKeycloakId.Should().Be(keycloakId);

        // DB state must be cleared regardless
        var (status, count, confirmToken) = await GetUserLifecycleStateAsync(userId);
        status.Should().BeNull();
        count.Should().Be(0);
        confirmToken.Should().BeNull();
    }

    [Fact]
    public async Task ConfirmActivity_Post_WhenKeycloakReEnableFails_StillClearsLifecycleState()
    {
        var keycloakId = Guid.NewGuid().ToString();
        var (userId, token) = await CreateUserWithLifecycleTokenAsync("dormant", keycloakId);
        _mockKeycloak.EnableUserSuccess = false;
        _mockKeycloak.EnableUserError = "Keycloak temporarily unavailable";

        var response = await PostTokenAsync(token);

        // Endpoint continues on Keycloak failure — state is still cleared and redirect is confirmed
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Contain("lifecycle=confirmed");

        var (status, count, confirmToken) = await GetUserLifecycleStateAsync(userId);
        status.Should().BeNull();
        count.Should().Be(0);
        confirmToken.Should().BeNull();
    }

    [Fact]
    public async Task ConfirmActivity_Post_DormantUserWithNoKeycloakId_StillClearsStateWithoutCallingKeycloak()
    {
        // keycloakId = null — user has no Keycloak account (e.g. created directly in DB)
        var (userId, token) = await CreateUserWithLifecycleTokenAsync("dormant", keycloakId: null);

        var response = await PostTokenAsync(token);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Contain("lifecycle=confirmed");

        _mockKeycloak.EnableUserCallCount.Should().Be(0);

        var (status, _, _) = await GetUserLifecycleStateAsync(userId);
        status.Should().BeNull();
    }

    // ─── expiry guard ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task ConfirmActivity_Post_WithExpiredToken_RedirectsToExpiredAndLeavesStateUntouched()
    {
        var (userId, token) = await CreateUserWithLifecycleTokenAsync("dormant", keycloakId: Guid.NewGuid().ToString(), tokenExpiresInDays: -1);

        var response = await PostTokenAsync(token);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Contain("lifecycle=expired");

        // An expired token must not re-enable the account or clear lifecycle state
        _mockKeycloak.EnableUserCallCount.Should().Be(0);
        var (status, count, confirmToken) = await GetUserLifecycleStateAsync(userId);
        status.Should().Be("dormant");
        count.Should().Be(1);
        confirmToken.Should().Be(SecureTokens.LifecycleConfirmTokenHash(token));
    }

    // ─── idempotency guard ────────────────────────────────────────────────────────

    [Fact]
    public async Task ConfirmActivity_Post_CannotBeConfirmedTwice_SecondCallRedirectsToExpired()
    {
        var (_, token) = await CreateUserWithLifecycleTokenAsync("warned");

        var first = await PostTokenAsync(token);
        first.StatusCode.Should().Be(HttpStatusCode.Redirect);
        first.Headers.Location!.ToString().Should().Contain("lifecycle=confirmed");

        // Token is consumed on first use; second call should not find it
        var second = await PostTokenAsync(token);
        second.StatusCode.Should().Be(HttpStatusCode.Redirect);
        second.Headers.Location!.ToString().Should().Contain("lifecycle=expired");
    }
}
