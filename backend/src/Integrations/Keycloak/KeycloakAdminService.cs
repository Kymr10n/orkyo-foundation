using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Api.Configuration;
using Orkyo.Shared;
using Orkyo.Shared.Keycloak;
using System.Threading;

namespace Api.Integrations.Keycloak;

// Interface is in IKeycloakAdminService.cs
// Models are in KeycloakModels.cs

public class KeycloakAdminService : IKeycloakAdminService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<KeycloakAdminService> _logger;
    private readonly KeycloakOptions _kc;

    private string? _accessToken;
    private DateTime _tokenExpiry = DateTime.MinValue;

    public KeycloakAdminService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<KeycloakAdminService> logger,
        KeycloakOptions keycloakOptions)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
        _kc = keycloakOptions;
    }

    public async Task ChangePasswordAsync(string keycloakSub, string currentPassword, string newPassword)
    {
        // Verify current password first — "incorrect password" is a user error (400), not an upstream failure.
        await VerifyCurrentPasswordAsync(keycloakSub, currentPassword);

        var (token, userId) = await ResolveUserAsync(keycloakSub);

        var url = $"{_kc.EffectiveInternalBaseUrl}/admin/realms/{_kc.Realm}/users/{userId}/reset-password";
        var request = CreateAdminRequest(HttpMethod.Put, url, token,
            new { type = "password", value = newPassword, temporary = false });

        var response = await _httpClient.SendAsync(request);
        await EnsureSuccessAsync(response, "Failed to update password");

        _logger.LogInformation("Password changed for user {Sub}", keycloakSub);
    }

    public async Task<List<KeycloakSession>> GetUserSessionsAsync(string keycloakSub)
    {
        var (token, userId) = await ResolveUserAsync(keycloakSub);

        var url = $"{_kc.EffectiveInternalBaseUrl}/admin/realms/{_kc.Realm}/users/{userId}/sessions";
        var request = CreateAdminRequest(HttpMethod.Get, url, token);

        var response = await _httpClient.SendAsync(request);
        await EnsureSuccessAsync(response, "Failed to retrieve sessions");

        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<List<KeycloakSession>>(json) ?? new List<KeycloakSession>();
    }

    public async Task RevokeSessionAsync(string sessionId)
    {
        var token = await GetAdminTokenAsync();

        var url = $"{_kc.EffectiveInternalBaseUrl}/admin/realms/{_kc.Realm}/sessions/{sessionId}";
        var request = CreateAdminRequest(HttpMethod.Delete, url, token);

        var response = await _httpClient.SendAsync(request);
        await EnsureSuccessAsync(response, "Failed to revoke session");

        _logger.LogInformation("Session {SessionId} revoked", sessionId);
    }

    public async Task LogoutAllSessionsAsync(string keycloakSub)
    {
        var (token, userId) = await ResolveUserAsync(keycloakSub);

        var url = $"{_kc.EffectiveInternalBaseUrl}/admin/realms/{_kc.Realm}/users/{userId}/logout";
        var request = CreateAdminRequest(HttpMethod.Post, url, token);

        var response = await _httpClient.SendAsync(request);
        await EnsureSuccessAsync(response, "Failed to logout from all sessions");

        _logger.LogInformation("All sessions logged out for user {Sub}", keycloakSub);
    }

    public async Task<FederationStatus> GetUserFederationStatusAsync(string keycloakSub)
    {
        // Best-effort check — any failure yields "not federated" rather than throwing,
        // because this call drives UI state (show/hide "change password") and a Keycloak
        // outage shouldn't make that UI disappear.
        try
        {
            var (token, userId) = await ResolveUserAsync(keycloakSub);

            var url = $"{_kc.EffectiveInternalBaseUrl}/admin/realms/{_kc.Realm}/users/{userId}/federated-identity";
            var request = CreateAdminRequest(HttpMethod.Get, url, token);

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                return new FederationStatus(false, null);
            }

            var json = await response.Content.ReadAsStringAsync();
            var identities = JsonSerializer.Deserialize<List<FederatedIdentity>>(json);

            return identities is { Count: > 0 }
                ? new FederationStatus(true, identities[0].IdentityProvider)
                : new FederationStatus(false, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking federation status for {Sub}", keycloakSub);
            return new FederationStatus(false, null);
        }
    }

    public async Task CreateUserAsync(
        string email, string password, string? firstName = null, string? lastName = null, bool emailVerified = false)
    {
        var token = await GetAdminTokenAsync();

        if (await UserExistsAsync(email))
        {
            throw new KeycloakAdminException("An account with this email already exists", StatusCodes.Status409Conflict);
        }

        var userPayload = new
        {
            username = email,
            email,
            firstName = firstName ?? "",
            lastName = lastName ?? "",
            enabled = true,
            emailVerified,
            credentials = new[]
            {
                new { type = "password", value = password, temporary = false }
            },
            requiredActions = emailVerified ? Array.Empty<string>() : new[] { "VERIFY_EMAIL" }
        };

        var url = $"{_kc.EffectiveInternalBaseUrl}/admin/realms/{_kc.Realm}/users";
        var request = CreateAdminRequest(HttpMethod.Post, url, token, userPayload);

        var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            _logger.LogWarning("Failed to create user: {Status} - {Body}", response.StatusCode, body);
            if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                throw new KeycloakAdminException("An account with this email already exists", StatusCodes.Status409Conflict);
            }
            throw new KeycloakAdminException("Failed to create account");
        }

        // Get user ID from Location header and send verification email if needed
        var userId = response.Headers.Location?.ToString().Split('/').LastOrDefault();
        if (!string.IsNullOrEmpty(userId) && !emailVerified)
        {
            await SendVerificationEmailAsync(userId, token);
        }

        _logger.LogInformation("User created: {Email}", email);
    }

    public async Task<bool> UserExistsAsync(string email)
    {
        var token = await GetAdminTokenAsync();

        var url = $"{_kc.EffectiveInternalBaseUrl}/admin/realms/{_kc.Realm}/users?email={Uri.EscapeDataString(email)}&exact=true";
        var request = CreateAdminRequest(HttpMethod.Get, url, token);

        var response = await _httpClient.SendAsync(request);
        await EnsureSuccessAsync(response, "Failed to check if user exists");

        var json = await response.Content.ReadAsStringAsync();
        var users = JsonSerializer.Deserialize<List<KeycloakUser>>(json);
        return users is { Count: > 0 };
    }

    public Task DisableUserAsync(string keycloakId) => SetUserEnabledAsync(keycloakId, enabled: false);

    public Task EnableUserAsync(string keycloakId) => SetUserEnabledAsync(keycloakId, enabled: true);

    public async Task DeleteUserAsync(string keycloakId)
    {
        var token = await GetAdminTokenAsync();

        var url = $"{_kc.EffectiveInternalBaseUrl}/admin/realms/{_kc.Realm}/users/{keycloakId}";
        var request = CreateAdminRequest(HttpMethod.Delete, url, token);

        var response = await _httpClient.SendAsync(request);
        await EnsureSuccessAsync(response, "Failed to delete user from Keycloak");

        _logger.LogInformation("Deleted user {KeycloakId} from Keycloak", keycloakId);
    }

    public async Task<MfaStatus> GetMfaStatusAsync(string keycloakSub)
    {
        var (token, userId) = await ResolveUserAsync(keycloakSub);

        var url = $"{_kc.EffectiveInternalBaseUrl}/admin/realms/{_kc.Realm}/users/{userId}/credentials";
        var request = CreateAdminRequest(HttpMethod.Get, url, token);

        var response = await _httpClient.SendAsync(request);
        await EnsureSuccessAsync(response, "Failed to retrieve MFA status");

        var json = await response.Content.ReadAsStringAsync();
        var credentials = JsonSerializer.Deserialize<List<KeycloakCredential>>(json) ?? new();

        var totpCred = credentials.FirstOrDefault(c => c.Type == "otp");
        var recoveryCred = credentials.FirstOrDefault(c => c.Type == "recovery-authn-codes");

        return new MfaStatus
        {
            TotpEnabled = totpCred != null,
            TotpCredentialId = totpCred?.Id,
            TotpCreatedDate = totpCred?.CreatedDate != null
                ? DateTimeOffset.FromUnixTimeMilliseconds(totpCred.CreatedDate.Value).DateTime
                : null,
            TotpLabel = totpCred?.UserLabel,
            RecoveryCodesConfigured = recoveryCred != null,
            RecoveryCodesCredentialId = recoveryCred?.Id,
        };
    }

    public async Task DeleteUserCredentialAsync(string keycloakSub, string credentialId)
    {
        var (token, userId) = await ResolveUserAsync(keycloakSub);

        // Verify the credential belongs to this user before deleting
        var credUrl = $"{_kc.EffectiveInternalBaseUrl}/admin/realms/{_kc.Realm}/users/{userId}/credentials";
        var credRequest = CreateAdminRequest(HttpMethod.Get, credUrl, token);
        var credResponse = await _httpClient.SendAsync(credRequest);

        if (credResponse.IsSuccessStatusCode)
        {
            var credJson = await credResponse.Content.ReadAsStringAsync();
            var credentials = JsonSerializer.Deserialize<List<KeycloakCredential>>(credJson) ?? new();
            if (!credentials.Any(c => c.Id == credentialId))
            {
                throw new KeycloakAdminException("Credential not found for this user", StatusCodes.Status404NotFound);
            }
        }

        var url = $"{_kc.EffectiveInternalBaseUrl}/admin/realms/{_kc.Realm}/users/{userId}/credentials/{credentialId}";
        var request = CreateAdminRequest(HttpMethod.Delete, url, token);

        var response = await _httpClient.SendAsync(request);
        await EnsureSuccessAsync(response, "Failed to remove credential");

        _logger.LogInformation("Deleted credential {CredentialId} for user {Sub}", credentialId, keycloakSub);
    }

    public async Task<UserProfile> GetUserProfileAsync(string keycloakSub)
    {
        var (token, userId) = await ResolveUserAsync(keycloakSub);

        var url = $"{_kc.EffectiveInternalBaseUrl}/admin/realms/{_kc.Realm}/users/{userId}";
        var request = CreateAdminRequest(HttpMethod.Get, url, token);

        var response = await _httpClient.SendAsync(request);
        await EnsureSuccessAsync(response, "Failed to retrieve profile");

        var json = await response.Content.ReadAsStringAsync();
        var userData = JsonSerializer.Deserialize<KeycloakUserFull>(json)
            ?? throw new KeycloakAdminException("Failed to parse user profile");

        return new UserProfile
        {
            Email = userData.Email ?? string.Empty,
            FirstName = userData.FirstName ?? string.Empty,
            LastName = userData.LastName ?? string.Empty,
            EmailVerified = userData.EmailVerified,
        };
    }

    public async Task UpdateUserProfileAsync(string keycloakSub, string firstName, string lastName)
    {
        var (token, userId) = await ResolveUserAsync(keycloakSub);

        var url = $"{_kc.EffectiveInternalBaseUrl}/admin/realms/{_kc.Realm}/users/{userId}";
        var request = CreateAdminRequest(HttpMethod.Put, url, token, new { firstName, lastName });

        var response = await _httpClient.SendAsync(request);
        await EnsureSuccessAsync(response, "Failed to update profile");

        _logger.LogInformation("Profile updated for user {Sub}", keycloakSub);
    }

    public async Task EnableMfaAsync(string keycloakSub)
    {
        var (token, userId) = await ResolveUserAsync(keycloakSub);

        var url = $"{_kc.EffectiveInternalBaseUrl}/admin/realms/{_kc.Realm}/users/{userId}";
        var request = CreateAdminRequest(HttpMethod.Put, url, token, new
        {
            requiredActions = new[] { "CONFIGURE_TOTP" }
        });

        var response = await _httpClient.SendAsync(request);
        await EnsureSuccessAsync(response, "Failed to enable MFA");

        _logger.LogInformation("CONFIGURE_TOTP required action added for user {Sub}", keycloakSub);
    }

    public async Task<bool> HasRealmRoleAsync(string keycloakId, string roleName)
    {
        var token = await GetAdminTokenAsync();

        var url = $"{_kc.EffectiveInternalBaseUrl}/admin/realms/{_kc.Realm}/users/{keycloakId}/role-mappings/realm";
        var request = CreateAdminRequest(HttpMethod.Get, url, token);

        var response = await _httpClient.SendAsync(request);
        await EnsureSuccessAsync(response, "Failed to check realm roles");

        var json = await response.Content.ReadAsStringAsync();
        var roles = JsonSerializer.Deserialize<List<KeycloakRole>>(json) ?? new();
        return roles.Any(r => r.Name == roleName);
    }

    public Task AssignRealmRoleAsync(string keycloakId, string roleName)
        => ModifyRealmRoleAsync(keycloakId, roleName, assign: true);

    public Task RevokeRealmRoleAsync(string keycloakId, string roleName)
        => ModifyRealmRoleAsync(keycloakId, roleName, assign: false);

    public async Task<int> CountRealmRoleMembersAsync(string roleName)
    {
        var token = await GetAdminTokenAsync();

        var url = $"{_kc.EffectiveInternalBaseUrl}/admin/realms/{_kc.Realm}/roles/{Uri.EscapeDataString(roleName)}/users";
        var request = CreateAdminRequest(HttpMethod.Get, url, token);
        var response = await _httpClient.SendAsync(request);

        await EnsureSuccessAsync(response, $"Failed to count members of role '{roleName}'");

        var json = await response.Content.ReadAsStringAsync();
        var users = JsonSerializer.Deserialize<JsonElement[]>(json);
        return users?.Length ?? 0;
    }

    private async Task ModifyRealmRoleAsync(string keycloakId, string roleName, bool assign)
    {
        var token = await GetAdminTokenAsync();

        // Look up the role to get its ID (required by Keycloak API)
        var roleUrl = $"{_kc.EffectiveInternalBaseUrl}/admin/realms/{_kc.Realm}/roles/{Uri.EscapeDataString(roleName)}";
        var roleRequest = CreateAdminRequest(HttpMethod.Get, roleUrl, token);
        var roleResponse = await _httpClient.SendAsync(roleRequest);

        if (!roleResponse.IsSuccessStatusCode)
        {
            _logger.LogWarning("Realm role {Role} not found", roleName);
            throw new KeycloakAdminException($"Realm role '{roleName}' not found", StatusCodes.Status404NotFound);
        }

        var roleJson = await roleResponse.Content.ReadAsStringAsync();
        var role = JsonSerializer.Deserialize<KeycloakRole>(roleJson);
        if (role?.Id == null || role.Name == null)
        {
            throw new KeycloakAdminException($"Failed to parse realm role '{roleName}'");
        }

        var method = assign ? HttpMethod.Post : HttpMethod.Delete;
        var url = $"{_kc.EffectiveInternalBaseUrl}/admin/realms/{_kc.Realm}/users/{keycloakId}/role-mappings/realm";
        var request = CreateAdminRequest(method, url, token,
            new[] { new { id = role.Id, name = role.Name } });

        var response = await _httpClient.SendAsync(request);
        await EnsureSuccessAsync(response, $"Failed to {(assign ? "assign" : "revoke")} role");

        _logger.LogInformation("{Action} realm role {Role} for user {KeycloakId}",
            assign ? "Assigned" : "Revoked", roleName, keycloakId);
    }

    private async Task SetUserEnabledAsync(string keycloakId, bool enabled)
    {
        var token = await GetAdminTokenAsync();

        var url = $"{_kc.EffectiveInternalBaseUrl}/admin/realms/{_kc.Realm}/users/{keycloakId}";
        var request = CreateAdminRequest(HttpMethod.Put, url, token, new { enabled });

        var response = await _httpClient.SendAsync(request);
        await EnsureSuccessAsync(response, $"Failed to {(enabled ? "enable" : "disable")} user in Keycloak");

        _logger.LogInformation("Set enabled={Enabled} for user {KeycloakId}", enabled, keycloakId);
    }

    private async Task SendVerificationEmailAsync(string userId, string token)
    {
        // Best-effort — user creation succeeds even if the email dispatch fails
        try
        {
            var clientId = _kc.BackendClientId;
            var frontendUrl = _configuration.GetRequired(ConfigKeys.AppBaseUrl);
            var redirectUri = Uri.EscapeDataString(frontendUrl);

            var url = $"{_kc.EffectiveInternalBaseUrl}/admin/realms/{_kc.Realm}/users/{userId}/send-verify-email?client_id={clientId}&redirect_uri={redirectUri}";
            var request = new HttpRequestMessage(HttpMethod.Put, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Failed to send verification email: {Error}", error);
            }
            else
            {
                _logger.LogInformation("Verification email sent to user {UserId}", userId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending verification email to {UserId}", userId);
        }
    }

    private async Task VerifyCurrentPasswordAsync(string keycloakSub, string password)
    {
        var adminToken = await GetAdminTokenAsync();

        var userId = await GetKeycloakUserIdAsync(keycloakSub, adminToken)
            ?? throw new KeycloakAdminException("User not found in Keycloak", StatusCodes.Status404NotFound);

        // Get user details to get username
        var userUrl = $"{_kc.EffectiveInternalBaseUrl}/admin/realms/{_kc.Realm}/users/{userId}";
        var userRequest = CreateAdminRequest(HttpMethod.Get, userUrl, adminToken);

        var userResponse = await _httpClient.SendAsync(userRequest);
        await EnsureSuccessAsync(userResponse, "Failed to get user details");

        var userJson = await userResponse.Content.ReadAsStringAsync();
        var userData = JsonSerializer.Deserialize<KeycloakUser>(userJson);
        var username = userData?.Username ?? userData?.Email;

        if (string.IsNullOrEmpty(username))
        {
            throw new KeycloakAdminException("Could not determine username");
        }

        // Verify the user's password by attempting an ROPC token exchange.
        // ROPC (directAccessGrantsEnabled) is intentionally kept on orkyo-backend
        // for this single use case — Keycloak has no Admin API for password verification.
        // This is safe because: (1) it requires the confidential client_secret,
        // (2) Keycloak brute-force protection applies (5 failures → 900s lockout),
        // (3) the token endpoint is rate-limited in Nginx.
        var tokenUrl = $"{_kc.EffectiveInternalBaseUrl}/realms/{_kc.Realm}/protocol/openid-connect/token";

        var verifyRequest = new HttpRequestMessage(HttpMethod.Post, tokenUrl)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["client_id"] = _kc.BackendClientId,
                ["client_secret"] = _kc.BackendClientSecret,
                ["username"] = username,
                ["password"] = password
            })
        };
        SetInternalProxyHeaders(verifyRequest);

        var response = await _httpClient.SendAsync(verifyRequest);
        if (!response.IsSuccessStatusCode)
        {
            throw new KeycloakAdminException("Current password is incorrect", StatusCodes.Status400BadRequest);
        }
    }

    // ── helpers ─────────────────────────────────────────────────────

    /// <summary>
    /// Throws <see cref="KeycloakAdminException"/> with <paramref name="publicErrorMessage"/>
    /// if the response isn't successful, logging the upstream body first.
    /// </summary>
    private async Task EnsureSuccessAsync(HttpResponseMessage response, string publicErrorMessage, int statusCode = StatusCodes.Status502BadGateway)
    {
        if (response.IsSuccessStatusCode) return;

        var body = await response.Content.ReadAsStringAsync();
        _logger.LogWarning("Keycloak admin request failed: {Status} - {Body}", response.StatusCode, body);
        throw new KeycloakAdminException(publicErrorMessage, statusCode);
    }

    /// <summary>
    /// Resolve admin token + Keycloak user ID for a given subject.
    /// Throws <see cref="KeycloakAdminException"/> if either step fails.
    /// </summary>
    private async Task<(string token, string userId)> ResolveUserAsync(string keycloakSub)
    {
        var token = await GetAdminTokenAsync();
        var userId = await GetKeycloakUserIdAsync(keycloakSub, token)
            ?? throw new KeycloakAdminException("User not found in Keycloak", StatusCodes.Status404NotFound);
        return (token, userId);
    }

    /// <summary>
    /// Create an HttpRequestMessage pre-configured with admin Bearer token.
    /// When using an internal URL (e.g. http://keycloak:8080), sets X-Forwarded headers
    /// so Keycloak's hostname validation accepts the request.
    /// </summary>
    private HttpRequestMessage CreateAdminRequest(HttpMethod method, string url, string token)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        SetInternalProxyHeaders(request);
        return request;
    }

    /// <summary>
    /// Create an HttpRequestMessage with admin Bearer token and JSON body.
    /// </summary>
    private HttpRequestMessage CreateAdminRequest(HttpMethod method, string url, string token, object body)
    {
        var request = CreateAdminRequest(method, url, token);
        request.Content = new StringContent(
            JsonSerializer.Serialize(body),
            Encoding.UTF8,
            "application/json");
        return request;
    }

    /// <summary>
    /// When the internal base URL differs from the public URL, set X-Forwarded headers
    /// so Keycloak (configured with KC_PROXY_HEADERS=xforwarded) resolves the correct
    /// public hostname and HTTPS scheme. Without these headers, the token issuer
    /// (https://auth.orkyo.com/realms/orkyo) won't match the request context and
    /// Admin API calls are rejected with 401/403.
    /// </summary>
    private void SetInternalProxyHeaders(HttpRequestMessage request)
    {
        if (KeycloakInternalProxyPolicy.ShouldSetForwardedHeaders(_kc.BaseUrl, _kc.InternalBaseUrl))
        {
            request.Headers.TryAddWithoutValidation(
                KeycloakInternalProxyPolicy.ForwardedProtoHeader,
                KeycloakInternalProxyPolicy.BuildForwardedProto(_kc.BaseUrl));
            request.Headers.TryAddWithoutValidation(
                KeycloakInternalProxyPolicy.ForwardedHostHeader,
                KeycloakInternalProxyPolicy.BuildForwardedHost(_kc.BaseUrl));
        }
    }

    private static readonly SemaphoreSlim _tokenLock = new(1, 1);

    private async Task<string> GetAdminTokenAsync()
    {
        // Fast path: return cached token if still valid (read is safe without lock)
        if (_accessToken != null && DateTime.UtcNow < _tokenExpiry.AddMinutes(-1))
        {
            return _accessToken;
        }

        await _tokenLock.WaitAsync();
        try
        {
            // Double-check after acquiring lock (another thread may have refreshed)
            if (_accessToken != null && DateTime.UtcNow < _tokenExpiry.AddMinutes(-1))
            {
                return _accessToken;
            }

            // Use client_credentials grant with the orkyo-backend service account.
            // The service account has realm-management roles (view-users, manage-users)
            // scoped to the orkyo realm — no super-admin credentials required.
            var tokenUrl = $"{_kc.EffectiveInternalBaseUrl}/realms/{_kc.Realm}/protocol/openid-connect/token";
            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = _kc.BackendClientId,
                ["client_secret"] = _kc.BackendClientSecret
            });

            var request = new HttpRequestMessage(HttpMethod.Post, tokenUrl) { Content = content };
            SetInternalProxyHeaders(request);
            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                _logger.LogError(
                    "Failed to get Keycloak admin token: {Status} — {Body} (URL: {Url})",
                    response.StatusCode, body, tokenUrl);
                throw new KeycloakAdminException("Failed to authenticate with Keycloak admin");
            }

            var json = await response.Content.ReadAsStringAsync();
            var tokenResponse = JsonSerializer.Deserialize<TokenResponse>(json);

            if (tokenResponse?.AccessToken == null)
            {
                throw new KeycloakAdminException("Failed to authenticate with Keycloak admin");
            }

            // Write expiry BEFORE token so concurrent readers never see a new token with stale expiry
            _tokenExpiry = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn - 30);
            _accessToken = tokenResponse.AccessToken;

            return _accessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private async Task<string?> GetKeycloakUserIdAsync(string keycloakSub, string token)
    {
        // The 'sub' claim is typically the Keycloak user ID; verify by looking up the user.
        var url = $"{_kc.EffectiveInternalBaseUrl}/admin/realms/{_kc.Realm}/users/{keycloakSub}";
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        SetInternalProxyHeaders(request);

        var response = await _httpClient.SendAsync(request);

        if (response.IsSuccessStatusCode)
        {
            return keycloakSub;
        }

        var body = await response.Content.ReadAsStringAsync();
        _logger.LogError(
            "Keycloak user lookup failed for {Sub}: {Status} — {Body} (URL: {Url})",
            keycloakSub, response.StatusCode, body, url);
        return null;
    }

    private class TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }
    }

    private class KeycloakUser
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("username")]
        public string? Username { get; set; }

        [JsonPropertyName("email")]
        public string? Email { get; set; }
    }

    private class KeycloakUserFull
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("username")]
        public string? Username { get; set; }

        [JsonPropertyName("email")]
        public string? Email { get; set; }

        [JsonPropertyName("firstName")]
        public string? FirstName { get; set; }

        [JsonPropertyName("lastName")]
        public string? LastName { get; set; }

        [JsonPropertyName("emailVerified")]
        public bool EmailVerified { get; set; }
    }

    private class KeycloakCredential
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("userLabel")]
        public string? UserLabel { get; set; }

        [JsonPropertyName("createdDate")]
        public long? CreatedDate { get; set; }
    }

    private class KeycloakRole
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }

    private class FederatedIdentity
    {
        [JsonPropertyName("identityProvider")]
        public string? IdentityProvider { get; set; }

        [JsonPropertyName("userId")]
        public string? UserId { get; set; }

        [JsonPropertyName("userName")]
        public string? UserName { get; set; }
    }
}
