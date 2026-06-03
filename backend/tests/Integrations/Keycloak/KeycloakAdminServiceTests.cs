using System.Net;
using System.Text.Json;
using Api.Integrations.Keycloak;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Orkyo.Shared;
using Orkyo.Shared.Keycloak;

namespace Orkyo.Foundation.Tests.Integrations.Keycloak;

/// <summary>
/// Unit tests for <see cref="KeycloakAdminService"/>.
/// All HTTP calls are intercepted by a programmatic <see cref="HttpMessageHandler"/>
/// so no live Keycloak is required.
/// </summary>
public class KeycloakAdminServiceTests
{
    // ── factory helpers ────────────────────────────────────────────────────

    private static KeycloakOptions DefaultOptions => new()
    {
        BaseUrl = "http://keycloak:8080",
        Realm = "test-realm",
        BackendClientId = "backend",
        BackendClientSecret = "secret"
    };

    private static IConfiguration DefaultConfiguration =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [ConfigKeys.AppBaseUrl] = "https://app.example.com"
            })
            .Build();

    private static string TokenJson(string token = "admin-token", int expiresIn = 3600) =>
        JsonSerializer.Serialize(new { access_token = token, expires_in = expiresIn });

    private static string UsersJson(string userId = "kc-user-id") =>
        JsonSerializer.Serialize(new[] { new { id = userId, username = "user@example.com", email = "user@example.com" } });

    private static string UserJson(string userId = "kc-user-id") =>
        JsonSerializer.Serialize(new { id = userId, username = "user@example.com", email = "user@example.com" });

    private static string CredentialsJson(string id = "cred-1", string type = "otp", long? createdDate = null) =>
        JsonSerializer.Serialize(new[] { new { id, type, userLabel = "Authenticator App", createdDate } });

    private static string EmptyArrayJson() => "[]";

    /// <summary>
    /// Build a service with a custom handler that returns pre-programmed responses
    /// per URL fragment match.
    /// </summary>
    private static KeycloakAdminService Build(IEnumerable<(string urlFragment, HttpStatusCode status, string body)> routes)
    {
        var handler = new DispatchHandler(routes);
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://keycloak:8080") };
        return new KeycloakAdminService(client, DefaultConfiguration, NullLogger<KeycloakAdminService>.Instance, DefaultOptions);
    }

    /// <summary>Build a service where every request returns 200 with <paramref name="body"/>.</summary>
    private static KeycloakAdminService BuildSimple(string body) =>
        Build(new[] { ("", HttpStatusCode.OK, body) });

    /// <summary>
    /// Build a service whose responses are produced by <paramref name="responder"/> and whose
    /// requests (and their bodies) are captured for assertion — used where header control or
    /// call-sequence assertions are needed (e.g. the create-user + reset-password flow).
    /// </summary>
    private static (KeycloakAdminService svc, CapturingHandler handler) BuildCapturing(
        Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var handler = new CapturingHandler(responder);
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://keycloak:8080") };
        return (new KeycloakAdminService(client, DefaultConfiguration, NullLogger<KeycloakAdminService>.Instance, DefaultOptions), handler);
    }

    // Token + user lookup preamble that every method needs.
    // Test-specific routes are added BEFORE the generic user-lookup fallback so
    // they take priority when both match the same URL fragment.
    private static IEnumerable<(string, HttpStatusCode, string)> TokenAndUser(
        IEnumerable<(string, HttpStatusCode, string)>? additional = null)
    {
        var routes = new List<(string, HttpStatusCode, string)>
        {
            ("openid-connect/token", HttpStatusCode.OK, TokenJson()),
        };
        if (additional != null)
            routes.AddRange(additional);
        // Generic user-lookup fallback — must come after test-specific routes.
        routes.Add(("/users/kc-user-id", HttpStatusCode.OK, UserJson()));
        return routes;
    }

    // ── GetAdminTokenAsync / auth failure ─────────────────────────────────

    [Fact]
    public async Task GetAdminToken_Failure_ThrowsKeycloakAdminException()
    {
        var svc = Build([("openid-connect/token", HttpStatusCode.Unauthorized, "")]);

        var act = () => svc.GetUserSessionsAsync("kc-user-id");
        await act.Should().ThrowAsync<KeycloakAdminException>();
    }

    // ── GetUserSessionsAsync ───────────────────────────────────────────────

    [Fact]
    public async Task GetUserSessionsAsync_ReturnsSessions_OnSuccess()
    {
        var sessionsJson = JsonSerializer.Serialize(new[]
        {
            new { id = "sess-1", username = "u", ipAddress = "1.2.3.4", start = 1700000000000L, lastAccess = 1700000001000L }
        });

        var svc = Build(TokenAndUser(new[]
        {
            ("/users/kc-user-id/sessions", HttpStatusCode.OK, sessionsJson)
        }));

        var sessions = await svc.GetUserSessionsAsync("kc-user-id");

        sessions.Should().HaveCount(1);
        sessions[0].Id.Should().Be("sess-1");
    }

    [Fact]
    public async Task GetUserSessionsAsync_ReturnsEmpty_WhenResponseIsEmptyArray()
    {
        var svc = Build(TokenAndUser(new[]
        {
            ("/users/kc-user-id/sessions", HttpStatusCode.OK, EmptyArrayJson())
        }));

        var sessions = await svc.GetUserSessionsAsync("kc-user-id");

        sessions.Should().BeEmpty();
    }

    // ── RevokeSessionAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task RevokeSessionAsync_Succeeds_On204()
    {
        var svc = Build(
        [
            ("openid-connect/token", HttpStatusCode.OK, TokenJson()),
            ("/sessions/sess-99", HttpStatusCode.NoContent, "")
        ]);

        var act = () => svc.RevokeSessionAsync("sess-99");
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RevokeSessionAsync_Throws_OnUpstreamError()
    {
        var svc = Build(
        [
            ("openid-connect/token", HttpStatusCode.OK, TokenJson()),
            ("/sessions/sess-99", HttpStatusCode.InternalServerError, "error")
        ]);

        var act = () => svc.RevokeSessionAsync("sess-99");
        await act.Should().ThrowAsync<KeycloakAdminException>();
    }

    // ── LogoutAllSessionsAsync ─────────────────────────────────────────────

    [Fact]
    public async Task LogoutAllSessionsAsync_Succeeds_On204()
    {
        var svc = Build(TokenAndUser(new[]
        {
            ("/users/kc-user-id/logout", HttpStatusCode.NoContent, "")
        }));

        await svc.LogoutAllSessionsAsync("kc-user-id");
    }

    // ── GetUserFederationStatusAsync ───────────────────────────────────────

    [Fact]
    public async Task GetUserFederationStatusAsync_ReturnsFederated_WhenIdentitiesPresent()
    {
        var fedJson = JsonSerializer.Serialize(new[]
        {
            new { identityProvider = "google", userId = "gid", userName = "u" }
        });

        var svc = Build(TokenAndUser(new[]
        {
            ("/users/kc-user-id/federated-identity", HttpStatusCode.OK, fedJson)
        }));

        var result = await svc.GetUserFederationStatusAsync("kc-user-id");

        result.IsFederated.Should().BeTrue();
        result.IdentityProvider.Should().Be("google");
    }

    [Fact]
    public async Task GetUserFederationStatusAsync_ReturnsNotFederated_WhenEmptyArray()
    {
        var svc = Build(TokenAndUser(new[]
        {
            ("/users/kc-user-id/federated-identity", HttpStatusCode.OK, EmptyArrayJson())
        }));

        var result = await svc.GetUserFederationStatusAsync("kc-user-id");

        result.IsFederated.Should().BeFalse();
    }

    [Fact]
    public async Task GetUserFederationStatusAsync_ReturnsNotFederated_WhenUpstreamFails()
    {
        // Non-success status → method swallows and returns (false, null)
        var svc = Build(TokenAndUser(new[]
        {
            ("/users/kc-user-id/federated-identity", HttpStatusCode.InternalServerError, "error")
        }));

        var result = await svc.GetUserFederationStatusAsync("kc-user-id");

        result.IsFederated.Should().BeFalse();
    }

    [Fact]
    public async Task GetUserFederationStatusAsync_ReturnsNotFederated_WhenExceptionThrown()
    {
        // Exception during ResolveUser → best-effort catch swallows it
        var svc = Build(
        [
            ("openid-connect/token", HttpStatusCode.OK, TokenJson()),
            ("/users/kc-user-id", HttpStatusCode.NotFound, "")   // user not found → ResolveUser throws → caught by outer try/catch
        ]);

        var result = await svc.GetUserFederationStatusAsync("kc-user-id");

        result.IsFederated.Should().BeFalse();
    }

    // ── UserExistsAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task UserExistsAsync_ReturnsTrue_WhenUsersListNonEmpty()
    {
        var svc = Build(
        [
            ("openid-connect/token", HttpStatusCode.OK, TokenJson()),
            ("/users?email=", HttpStatusCode.OK, UsersJson())
        ]);

        var result = await svc.UserExistsAsync("u@example.com");

        result.Should().BeTrue();
    }

    [Fact]
    public async Task UserExistsAsync_ReturnsFalse_WhenUsersListEmpty()
    {
        var svc = Build(
        [
            ("openid-connect/token", HttpStatusCode.OK, TokenJson()),
            ("/users?email=", HttpStatusCode.OK, EmptyArrayJson())
        ]);

        var result = await svc.UserExistsAsync("new@example.com");

        result.Should().BeFalse();
    }

    // ── CreateUserAsync ────────────────────────────────────────────────────

    /// <summary>Responder for the happy-path create-user flow: token → user-exists check →
    /// POST /users (201 + Location) → PUT reset-password (204).</summary>
    private static HttpResponseMessage CreateUserResponder(HttpRequestMessage req, string newUserId = "new-user-id")
    {
        var url = req.RequestUri!.ToString();
        if (url.Contains("openid-connect/token"))
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(TokenJson()) };
        if (url.Contains("/users?email="))
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(EmptyArrayJson()) };
        if (req.Method == HttpMethod.Put && url.Contains("/reset-password"))
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        if (req.Method == HttpMethod.Post && url.EndsWith("/users"))
        {
            var resp = new HttpResponseMessage(HttpStatusCode.Created);
            resp.Headers.Location = new Uri($"http://keycloak:8080/admin/realms/test-realm/users/{newUserId}");
            return resp;
        }
        return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent($"No route: {url}") };
    }

    [Fact]
    public async Task CreateUserAsync_Succeeds_WhenUserDoesNotExist()
    {
        var (svc, _) = BuildCapturing(req => CreateUserResponder(req));

        var act = () => svc.CreateUserAsync("new@example.com", "pass123", emailVerified: true);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task CreateUserAsync_SetsPassword_ViaResetPasswordEndpoint()
    {
        var (svc, handler) = BuildCapturing(req => CreateUserResponder(req));

        await svc.CreateUserAsync("new@example.com", "s3cret-pass", emailVerified: true);

        // Keycloak 26 ignores inline credentials on creation, so the password must be set by a
        // dedicated reset-password PUT for the newly created user, carrying a non-temporary credential.
        var pwIndex = handler.Requests.FindIndex(r =>
            r.Method == HttpMethod.Put &&
            r.RequestUri!.ToString().Contains("/users/new-user-id/reset-password"));
        pwIndex.Should().BeGreaterThanOrEqualTo(0, "the password must be set via reset-password");
        handler.Bodies[pwIndex].Should().Contain("s3cret-pass");
        handler.Bodies[pwIndex].Should().Contain("\"temporary\":false");
    }

    [Fact]
    public async Task CreateUserAsync_Throws_WhenLocationHeaderMissing()
    {
        // POST /users succeeds but returns no Location header → cannot resolve the new user id.
        var (svc, _) = BuildCapturing(req =>
        {
            var url = req.RequestUri!.ToString();
            if (url.Contains("openid-connect/token"))
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(TokenJson()) };
            if (url.Contains("/users?email="))
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(EmptyArrayJson()) };
            // POST /users → 201 with NO Location header.
            return new HttpResponseMessage(HttpStatusCode.Created);
        });

        var act = () => svc.CreateUserAsync("new@example.com", "pass123", emailVerified: true);
        await act.Should().ThrowAsync<KeycloakAdminException>().WithMessage("*user ID*");
    }

    [Fact]
    public async Task CreateUserAsync_Throws409_WhenUserAlreadyExists()
    {
        var svc = Build(
        [
            ("openid-connect/token", HttpStatusCode.OK, TokenJson()),
            ("/users?email=", HttpStatusCode.OK, UsersJson())    // already exists
        ]);

        var act = () => svc.CreateUserAsync("existing@example.com", "pass");
        var ex = await act.Should().ThrowAsync<KeycloakAdminException>();
        ex.Which.StatusCode.Should().Be(StatusCodes.Status409Conflict);
    }

    [Fact]
    public async Task CreateUserAsync_Throws409_WhenKeycloakReturns409()
    {
        // UserExistsAsync returns false but Keycloak returns 409 (race)
        var svc = Build(
        [
            ("openid-connect/token", HttpStatusCode.OK, TokenJson()),
            ("/users?email=", HttpStatusCode.OK, EmptyArrayJson()),
            ("/users", HttpStatusCode.Conflict, "")
        ]);

        var act = () => svc.CreateUserAsync("new@example.com", "pass");
        var ex = await act.Should().ThrowAsync<KeycloakAdminException>();
        ex.Which.StatusCode.Should().Be(StatusCodes.Status409Conflict);
    }

    [Fact]
    public async Task CreateUserAsync_ThrowsGenericError_WhenKeycloakReturnsBadRequest()
    {
        var svc = Build(
        [
            ("openid-connect/token", HttpStatusCode.OK, TokenJson()),
            ("/users?email=", HttpStatusCode.OK, EmptyArrayJson()),
            ("/users", HttpStatusCode.BadRequest, "bad request body")
        ]);

        var act = () => svc.CreateUserAsync("new@example.com", "pass");
        await act.Should().ThrowAsync<KeycloakAdminException>()
            .WithMessage("*create account*");
    }

    // ── DisableUserAsync / EnableUserAsync ────────────────────────────────

    [Fact]
    public async Task DisableUserAsync_Succeeds_On204()
    {
        var svc = Build(
        [
            ("openid-connect/token", HttpStatusCode.OK, TokenJson()),
            ("/users/kc-id-1", HttpStatusCode.NoContent, "")
        ]);

        await svc.DisableUserAsync("kc-id-1");
    }

    [Fact]
    public async Task EnableUserAsync_Succeeds_On204()
    {
        var svc = Build(
        [
            ("openid-connect/token", HttpStatusCode.OK, TokenJson()),
            ("/users/kc-id-1", HttpStatusCode.NoContent, "")
        ]);

        await svc.EnableUserAsync("kc-id-1");
    }

    // ── DeleteUserAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteUserAsync_Succeeds_On204()
    {
        var svc = Build(
        [
            ("openid-connect/token", HttpStatusCode.OK, TokenJson()),
            ("/users/kc-id-del", HttpStatusCode.NoContent, "")
        ]);

        await svc.DeleteUserAsync("kc-id-del");
    }

    [Fact]
    public async Task DeleteUserAsync_Throws_WhenUpstreamReturnsError()
    {
        var svc = Build(
        [
            ("openid-connect/token", HttpStatusCode.OK, TokenJson()),
            ("/users/kc-id-del", HttpStatusCode.InternalServerError, "")
        ]);

        var act = () => svc.DeleteUserAsync("kc-id-del");
        await act.Should().ThrowAsync<KeycloakAdminException>();
    }

    // ── GetMfaStatusAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task GetMfaStatusAsync_ReturnsTotpEnabled_WhenOtpCredentialPresent()
    {
        var credJson = CredentialsJson("cred-1", "otp", 1700000000000L);

        var svc = Build(TokenAndUser(new[]
        {
            ("/users/kc-user-id/credentials", HttpStatusCode.OK, credJson)
        }));

        var status = await svc.GetMfaStatusAsync("kc-user-id");

        status.TotpEnabled.Should().BeTrue();
        status.TotpCredentialId.Should().Be("cred-1");
        status.TotpCreatedDate.Should().NotBeNull();
    }

    [Fact]
    public async Task GetMfaStatusAsync_ReturnsNotEnabled_WhenNoOtpCredential()
    {
        var svc = Build(TokenAndUser(new[]
        {
            ("/users/kc-user-id/credentials", HttpStatusCode.OK, EmptyArrayJson())
        }));

        var status = await svc.GetMfaStatusAsync("kc-user-id");

        status.TotpEnabled.Should().BeFalse();
        status.RecoveryCodesConfigured.Should().BeFalse();
    }

    // ── DeleteUserCredentialAsync ──────────────────────────────────────────

    [Fact]
    public async Task DeleteUserCredentialAsync_Succeeds_WhenCredentialBelongsToUser()
    {
        var credJson = CredentialsJson("cred-abc", "otp");

        var svc = Build(TokenAndUser(new[]
        {
            ("/users/kc-user-id/credentials", HttpStatusCode.OK, credJson),   // ownership check
            ("/users/kc-user-id/credentials/cred-abc", HttpStatusCode.NoContent, "") // delete
        }));

        var act = () => svc.DeleteUserCredentialAsync("kc-user-id", "cred-abc");
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DeleteUserCredentialAsync_Throws404_WhenCredentialNotFound()
    {
        var credJson = CredentialsJson("other-cred", "otp");

        var svc = Build(TokenAndUser(new[]
        {
            ("/users/kc-user-id/credentials", HttpStatusCode.OK, credJson)
        }));

        var act = () => svc.DeleteUserCredentialAsync("kc-user-id", "missing-cred");
        var ex = await act.Should().ThrowAsync<KeycloakAdminException>();
        ex.Which.StatusCode.Should().Be(StatusCodes.Status404NotFound);
    }

    // ── GetUserProfileAsync ────────────────────────────────────────────────

    [Fact]
    public async Task GetUserProfileAsync_MapsFields_OnSuccess()
    {
        var profileJson = JsonSerializer.Serialize(new
        {
            id = "kc-user-id",
            email = "alice@example.com",
            firstName = "Alice",
            lastName = "Smith",
            emailVerified = true
        });

        var svc = Build(TokenAndUser(new[]
        {
            ("/users/kc-user-id", HttpStatusCode.OK, profileJson)
        }));

        var profile = await svc.GetUserProfileAsync("kc-user-id");

        profile.Email.Should().Be("alice@example.com");
        profile.FirstName.Should().Be("Alice");
        profile.LastName.Should().Be("Smith");
        profile.EmailVerified.Should().BeTrue();
    }

    // ── UpdateUserProfileAsync ─────────────────────────────────────────────

    [Fact]
    public async Task UpdateUserProfileAsync_Succeeds_On204()
    {
        var svc = Build(TokenAndUser(new[]
        {
            ("/users/kc-user-id", HttpStatusCode.NoContent, "")
        }));

        await svc.UpdateUserProfileAsync("kc-user-id", "Bob", "Jones");
    }

    // ── UpdateEmailForAccountAsync ─────────────────────────────────────────

    [Fact]
    public async Task UpdateEmailForAccountAsync_UsesSubject_WhenSubjectResolves()
    {
        var svc = Build(
        [
            ("openid-connect/token", HttpStatusCode.OK, TokenJson()),
            ("/users/kc-user-id", HttpStatusCode.NoContent, "")
        ]);

        await svc.UpdateEmailForAccountAsync("kc-user-id", "old@example.com", "new@example.com");
    }

    [Fact]
    public async Task UpdateEmailForAccountAsync_FallsBackToCurrentEmail_WhenSubjectMissing()
    {
        var svc = Build(
        [
            ("openid-connect/token", HttpStatusCode.OK, TokenJson()),
            ("/users/missing-sub", HttpStatusCode.NotFound, ""),
            ("/users?email=", HttpStatusCode.OK, UsersJson("fallback-id")),
            ("/users/fallback-id", HttpStatusCode.NoContent, "")
        ]);

        await svc.UpdateEmailForAccountAsync("missing-sub", "old@example.com", "new@example.com");
    }

    [Fact]
    public async Task UpdateEmailForAccountAsync_FallsBackToCurrentEmail_WhenSubjectNotStored()
    {
        var svc = Build(
        [
            ("openid-connect/token", HttpStatusCode.OK, TokenJson()),
            ("/users?email=", HttpStatusCode.OK, UsersJson("fallback-id")),
            ("/users/fallback-id", HttpStatusCode.NoContent, "")
        ]);

        await svc.UpdateEmailForAccountAsync(null, "old@example.com", "new@example.com");
    }

    // ── EnableMfaAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task EnableMfaAsync_Succeeds_On204()
    {
        var svc = Build(TokenAndUser(new[]
        {
            ("/users/kc-user-id", HttpStatusCode.NoContent, "")
        }));

        await svc.EnableMfaAsync("kc-user-id");
    }

    // ── HasRealmRoleAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task HasRealmRoleAsync_ReturnsTrue_WhenRoleInList()
    {
        var rolesJson = JsonSerializer.Serialize(new[]
        {
            new { id = "role-id", name = "site-admin" }
        });

        var svc = Build(
        [
            ("openid-connect/token", HttpStatusCode.OK, TokenJson()),
            ("/role-mappings/realm", HttpStatusCode.OK, rolesJson)
        ]);

        var result = await svc.HasRealmRoleAsync("kc-user-id", "site-admin");

        result.Should().BeTrue();
    }

    [Fact]
    public async Task HasRealmRoleAsync_ReturnsFalse_WhenRoleAbsent()
    {
        var svc = Build(
        [
            ("openid-connect/token", HttpStatusCode.OK, TokenJson()),
            ("/role-mappings/realm", HttpStatusCode.OK, EmptyArrayJson())
        ]);

        var result = await svc.HasRealmRoleAsync("kc-user-id", "site-admin");

        result.Should().BeFalse();
    }

    // ── CountRealmRoleMembersAsync ─────────────────────────────────────────

    [Fact]
    public async Task CountRealmRoleMembersAsync_ReturnsCount()
    {
        var twoUsers = JsonSerializer.Serialize(new[] { new { id = "u1" }, new { id = "u2" } });

        var svc = Build(
        [
            ("openid-connect/token", HttpStatusCode.OK, TokenJson()),
            ("/roles/site-admin/users", HttpStatusCode.OK, twoUsers)
        ]);

        var count = await svc.CountRealmRoleMembersAsync("site-admin");

        count.Should().Be(2);
    }

    // ── EnsureSuccessAsync (via any method that hits it) ──────────────────

    [Fact]
    public async Task EnsureSuccessAsync_Throws_WhenResponseIsNonSuccess()
    {
        var svc = Build(TokenAndUser(new[]
        {
            ("/users/kc-user-id/logout", HttpStatusCode.ServiceUnavailable, "upstream down")
        }));

        var act = () => svc.LogoutAllSessionsAsync("kc-user-id");
        var ex = await act.Should().ThrowAsync<KeycloakAdminException>();
        // Default status should be 502
        ex.Which.StatusCode.Should().Be(StatusCodes.Status502BadGateway);
    }

    // ── User not found (ResolveUserAsync) ─────────────────────────────────

    [Fact]
    public async Task ResolveUserAsync_Throws404_WhenUserNotFoundInKeycloak()
    {
        var svc = Build(
        [
            ("openid-connect/token", HttpStatusCode.OK, TokenJson()),
            ("/users/missing-user", HttpStatusCode.NotFound, "")
        ]);

        var act = () => svc.GetUserSessionsAsync("missing-user");
        var ex = await act.Should().ThrowAsync<KeycloakAdminException>();
        ex.Which.StatusCode.Should().Be(StatusCodes.Status404NotFound);
    }

    // ── Token caching (second call uses cached token) ─────────────────────

    [Fact]
    public async Task GetAdminToken_IsCached_BetweenCalls()
    {
        var callCount = 0;
        var tokenFetched = false; // tracks lifetime of the single token fetch
        var handler = new CountingHandler(() =>
        {
            callCount++;
            if (!tokenFetched)
            {
                tokenFetched = true;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(TokenJson("cached-token", 600))
                };
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(EmptyArrayJson())
            };
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri("http://keycloak:8080") };
        var svc = new KeycloakAdminService(client, DefaultConfiguration,
            NullLogger<KeycloakAdminService>.Instance, DefaultOptions);

        // Two calls — token should only be fetched once
        await svc.UserExistsAsync("a@a.com");
        callCount = 0;                          // reset counter

        // If the token is cached the first HTTP request sent for this call
        // will be the actual user-search request (not another token request).
        await svc.UserExistsAsync("b@b.com");

        callCount.Should().Be(1);   // only the users-search, no token request
    }

    // ── Private dispatch handler ───────────────────────────────────────────

    private sealed class DispatchHandler : HttpMessageHandler
    {
        private readonly IReadOnlyList<(string urlFragment, HttpStatusCode status, string body)> _routes;

        // Sort by descending fragment length so the most-specific fragment wins when
        // one fragment is a prefix of another (e.g. /users/id vs /users/id/credentials).
        public DispatchHandler(IEnumerable<(string, HttpStatusCode, string)> routes)
            => _routes = routes.OrderByDescending(r => r.Item1.Length).ToList();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri?.ToString() ?? string.Empty;

            foreach (var (fragment, status, body) in _routes)
            {
                if (string.IsNullOrEmpty(fragment) || url.Contains(fragment, StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult(new HttpResponseMessage(status)
                    {
                        Content = new StringContent(body)
                    });
                }
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent($"No route matched: {url}")
            });
        }
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        private readonly Func<HttpResponseMessage> _factory;
        public CountingHandler(Func<HttpResponseMessage> factory) => _factory = factory;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_factory());
    }

    /// <summary>
    /// Records every request (method, URI, and serialized body) and delegates the response
    /// to a caller-supplied responder. Lets tests assert on call sequence and request bodies
    /// and lets responders set headers (e.g. a Location header on user creation).
    /// </summary>
    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public CapturingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        public List<HttpRequestMessage> Requests { get; } = [];
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Bodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
            Requests.Add(request);
            return _responder(request);
        }
    }
}
