using System.Net;
using System.Text.Json;
using Api.Services.BffSession;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Orkyo.Foundation.Tests.Endpoints;

/// <summary>
/// Integration tests for BFF auth endpoints.
/// BFF_ENABLED=true is set in the shared FoundationWebApplicationFactory config,
/// so BFF endpoints are always mapped alongside existing JWT endpoints.
/// Tests focus on login redirect generation, returnTo validation, and error paths.
/// Callback tests that require real Keycloak token exchange are deferred to E2E.
/// </summary>
[Collection("Database collection")]
public class BffAuthEndpointsTests
{
    private readonly HttpClient _client;
    private readonly FoundationWebApplicationFactory _factory;

    public BffAuthEndpointsTests(DatabaseFixture databaseFixture)
    {
        _factory = databaseFixture.Factory;
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    // ── GET /api/auth/bff/login ──────────────────────────────────────────────

    [Fact]
    public async Task Login_WithValidReturnTo_Redirects302ToKeycloak()
    {
        var response = await _client.GetAsync("/api/auth/bff/login?returnTo=https://demo.orkyo.com/dashboard");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var location = response.Headers.Location?.ToString();
        location.Should().NotBeNull();
        location.Should().Contain("/protocol/openid-connect/auth");
        location.Should().Contain("response_type=code");
        location.Should().Contain("client_id=test-backend");
        location.Should().Contain("code_challenge=");
        location.Should().Contain("code_challenge_method=S256");
        location.Should().Contain("state=");
    }

    [Fact]
    public async Task Login_WithInvalidReturnTo_Returns400()
    {
        var response = await _client.GetAsync("/api/auth/bff/login?returnTo=https://evil.com/phish");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Login_WithJavascriptScheme_Returns400()
    {
        var response = await _client.GetAsync("/api/auth/bff/login?returnTo=javascript:alert(1)");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Login_WithLocalhostReturnTo_Redirects()
    {
        // BFF_COOKIE_SECURE=false in test config, so http:// localhost is allowed
        var response = await _client.GetAsync("/api/auth/bff/login?returnTo=http://localhost:5173/");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task Login_StoresStateInPkceStore()
    {
        var response = await _client.GetAsync("/api/auth/bff/login?returnTo=https://orkyo.com/");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var location = response.Headers.Location?.ToString();

        // Extract state from the redirect URL
        var uri = new Uri(location!);
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        var state = query["state"];
        state.Should().NotBeNullOrEmpty();

        // Verify state was stored in IBffPkceStateStore (not IDistributedCache)
        using var scope = _factory.Services.CreateScope();
        var pkceStore = scope.ServiceProvider.GetRequiredService<IBffPkceStateStore>();
        var pkceState = await pkceStore.GetAndRemoveAsync(state!);
        pkceState.Should().NotBeNull();
        pkceState!.ReturnTo.Should().Be("https://orkyo.com/");
    }

    // ── GET /api/auth/bff/callback ───────────────────────────────────────────

    [Fact]
    public async Task Callback_WithMissingParams_Returns400()
    {
        var response = await _client.GetAsync("/api/auth/bff/callback");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Callback_WithInvalidState_Returns400()
    {
        var response = await _client.GetAsync("/api/auth/bff/callback?code=test-code&state=invalid-state");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── GET /api/auth/bff/logout ─────────────────────────────────────────────
    //
    // Logout is now a GET that the SPA navigates to via window.location.href.
    // The server clears the session, then 302-redirects to Keycloak's end-session
    // endpoint. id_token_hint is in the Location header, never in a JSON body.

    [Fact]
    public async Task Logout_WithoutSession_RedirectsToKeycloakEndSession()
    {
        var response = await _client.GetAsync("/api/auth/bff/logout");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var location = response.Headers.Location?.ToString();
        location.Should().NotBeNull();
        location.Should().Contain("/protocol/openid-connect/logout");
    }

    [Fact]
    public async Task Logout_PostMethod_Returns405()
    {
        // Guard: the old POST endpoint must no longer exist
        var response = await _client.PostAsync("/api/auth/bff/logout", null);

        response.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
    }

    // ── GET /api/auth/bff/callback (additional error paths) ────────────────────

    [Fact]
    public async Task Callback_WithCodeButNoState_Returns400()
    {
        var response = await _client.GetAsync("/api/auth/bff/callback?code=test-code");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Callback_WithStateButNoCode_Returns400()
    {
        var response = await _client.GetAsync("/api/auth/bff/callback?state=test-state");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── GET /api/auth/bff/login (additional paths) ──────────────────────────────

    [Fact]
    public async Task Login_WithoutReturnTo_UsesDefaultAndRedirects()
    {
        var response = await _client.GetAsync("/api/auth/bff/login");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var location = response.Headers.Location?.ToString();
        location.Should().Contain("/protocol/openid-connect/auth");
    }

    [Fact]
    public async Task Login_WithDataScheme_Returns400()
    {
        var response = await _client.GetAsync("/api/auth/bff/login?returnTo=data:text/html,<h1>xss</h1>");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── GET /api/auth/bff/me ─────────────────────────────────────────────────

    [Fact]
    public async Task Me_WithoutSession_Returns200WithAuthenticatedFalse()
    {
        var response = await _client.GetAsync("/api/auth/bff/me");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);
        doc.RootElement.TryGetProperty("authenticated", out var authenticated).Should().BeTrue();
        authenticated.GetBoolean().Should().BeFalse();
    }

    // ── Method guards ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Logout_PutMethod_Returns405()
    {
        var response = await _client.PutAsync("/api/auth/bff/logout", null);

        response.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
    }

    [Fact]
    public async Task Logout_DeleteMethod_Returns405()
    {
        var response = await _client.DeleteAsync("/api/auth/bff/logout");

        response.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
    }
}
