using System.Net;
using System.Text.Json;
using Api.Services.BffSession;
using Microsoft.AspNetCore.DataProtection;
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
    public async Task Login_WithLoginHint_ForwardsLoginHintToKeycloak()
    {
        // The SPA sends the OIDC-standard `login_hint` query key; it must be bound
        // (via [FromQuery(Name = "login_hint")]) and forwarded onto the Keycloak auth URL
        // so the email is pre-filled after invite signup.
        var response = await _client.GetAsync(
            "/api/auth/bff/login?returnTo=http://localhost:5173/login?auto=1&login_hint=user%40acme.com");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var location = response.Headers.Location?.ToString();
        location.Should().NotBeNull();
        location.Should().Contain("login_hint=user@acme.com");
    }

    [Fact]
    public async Task Login_WithoutLoginHint_OmitsLoginHintParam()
    {
        var response = await _client.GetAsync("/api/auth/bff/login?returnTo=http://localhost:5173/");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var location = response.Headers.Location?.ToString();
        location.Should().NotBeNull();
        location.Should().NotContain("login_hint");
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
    public async Task Callback_WithInvalidState_RedirectsToLoginWithError()
    {
        // An expired/replayed state can't be recovered. Rather than a blank 400, the callback
        // redirects to the SPA login with a friendly error code so the user sees a clear message.
        var response = await _client.GetAsync("/api/auth/bff/callback?code=test-code&state=invalid-state");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var location = response.Headers.Location?.ToString();
        location.Should().NotBeNull();
        location.Should().Contain("/login?error=invalid_state");
    }

    // ── GET /api/auth/bff/logout ─────────────────────────────────────────────
    //
    // Logout is now a GET that the SPA navigates to via window.location.href.
    // The server clears the session, then 302-redirects to Keycloak's end-session
    // endpoint. id_token_hint is in the Location header, never in a JSON body.

    [Fact]
    public async Task Logout_WithoutSession_RedirectsToKeycloakEndSessionWithClientId()
    {
        var response = await _client.GetAsync("/api/auth/bff/logout");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var location = response.Headers.Location?.ToString();
        location.Should().NotBeNull();
        location.Should().Contain("/protocol/openid-connect/logout");
        // With no session there is no id_token_hint — client_id is then the only
        // way Keycloak can validate post_logout_redirect_uri. Without it Keycloak
        // returns a raw 400 error page (#103).
        location.Should().Contain("client_id=test-backend");
        location.Should().NotContain("id_token_hint=");
    }

    [Fact]
    public async Task Logout_WithSession_SendsBothIdTokenHintAndClientId()
    {
        // Seed a real session and present its data-protected cookie, mirroring what
        // HandleCallback stores — the logout handler unprotects the cookie, loads the
        // session, and must forward id_token_hint alongside the always-present client_id.
        var sessionId = Guid.NewGuid().ToString("N");
        var sessionStore = _factory.Services.GetRequiredService<IBffSessionStore>();
        await sessionStore.SetAsync(new BffSessionRecord
        {
            SessionId = sessionId,
            UserId = Guid.NewGuid().ToString(),
            ExternalSubject = Guid.NewGuid().ToString(),
            AccessToken = "at",
            RefreshToken = "rt",
            IdToken = "header.payload.signature",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            CreatedAt = DateTimeOffset.UtcNow,
        });
        var protector = _factory.Services
            .GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("BffSession");
        var cookieValue = protector.Protect(sessionId);

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/bff/logout");
        request.Headers.Add("Cookie", $"orkyo-session={cookieValue}");
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var location = response.Headers.Location?.ToString();
        location.Should().NotBeNull();
        location.Should().Contain("/protocol/openid-connect/logout");
        location.Should().Contain("id_token_hint=");
        location.Should().Contain("client_id=test-backend");

        // Logout is single-use: the session must be gone from the store.
        (await sessionStore.GetAsync(sessionId)).Should().BeNull();
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

    // Note: the "bff-auth" rate-limit policy applied to /login, /callback and /logout
    // is verified by orkyo-saas RateLimitPolicyRegistrationTests, which assert the
    // policy is declared and registered. Behavioural 429 testing is not possible in
    // this host because rate limiting is globally disabled here (DISABLE_RATE_LIMITING).
}
