using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Api.Configuration;
using Orkyo.Shared;
using Orkyo.Shared.Keycloak;

namespace Api.Integrations.Keycloak;

// Interface is in IKeycloakAdminService.cs
// Models are in KeycloakModels.cs

public class KeycloakAdminService : IKeycloakAdminService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<KeycloakAdminService> _logger;
    private readonly KeycloakOptions _kc;
    private readonly TimeProvider _time;
    private readonly KeycloakAdminTokenCache _tokenCache;

    public KeycloakAdminService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<KeycloakAdminService> logger,
        KeycloakOptions keycloakOptions,
        TimeProvider time,
        KeycloakAdminTokenCache tokenCache)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
        _kc = keycloakOptions;
        _time = time;
        _tokenCache = tokenCache;
    }

    public async Task ChangePasswordAsync(string keycloakSub, string currentPassword, string newPassword, CancellationToken ct = default)
    {
        // Verify current password first — "incorrect password" is a user error (400), not an upstream failure.
        await VerifyCurrentPasswordAsync(keycloakSub, currentPassword, ct);

        var (token, userId) = await ResolveUserAsync(keycloakSub, ct);

        await SetUserPasswordAsync(userId, token, newPassword, "Failed to update password", ct);

        _logger.LogInformation("Password changed for user {Sub}", keycloakSub);
    }

    /// <summary>
    /// Set (or reset) a Keycloak user's password via the admin reset-password endpoint.
    /// Shared by <see cref="ChangePasswordAsync"/> and <see cref="CreateUserAsync"/> so the
    /// non-temporary password-credential shape lives in one place.
    /// </summary>
    private Task SetUserPasswordAsync(
        string userId, string token, string password, string failureMessage, CancellationToken ct) =>
        SendAdminAsync(HttpMethod.Put, $"users/{userId}/reset-password", token, failureMessage, ct,
            new { type = "password", value = password, temporary = false });

    public async Task<List<KeycloakSession>> GetUserSessionsAsync(string keycloakSub, CancellationToken ct = default)
    {
        var (token, userId) = await ResolveUserAsync(keycloakSub, ct);
        return await GetAdminJsonAsync<List<KeycloakSession>>($"users/{userId}/sessions", token, "Failed to retrieve sessions", ct)
            ?? new List<KeycloakSession>();
    }

    public async Task RevokeSessionAsync(string sessionId, CancellationToken ct = default)
    {
        var token = await GetAdminTokenAsync(ct);
        await SendAdminAsync(HttpMethod.Delete, $"sessions/{Uri.EscapeDataString(sessionId)}", token, "Failed to revoke session", ct);
        _logger.LogInformation("Session {SessionId} revoked", sessionId);
    }

    public async Task LogoutAllSessionsAsync(string keycloakSub, CancellationToken ct = default)
    {
        var (token, userId) = await ResolveUserAsync(keycloakSub, ct);
        await SendAdminAsync(HttpMethod.Post, $"users/{userId}/logout", token, "Failed to logout from all sessions", ct);
        _logger.LogInformation("All sessions logged out for user {Sub}", keycloakSub);
    }

    public async Task<FederationStatus> GetUserFederationStatusAsync(string keycloakSub, CancellationToken ct = default)
    {
        // Best-effort check — any failure yields "not federated" rather than throwing,
        // because this call drives UI state (show/hide "change password") and a Keycloak
        // outage shouldn't make that UI disappear.
        try
        {
            var (token, userId) = await ResolveUserAsync(keycloakSub, ct);

            using var request = CreateAdminRequest(HttpMethod.Get, AdminUrl($"users/{userId}/federated-identity"), token);
            using var response = await _httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                return new FederationStatus(false, null);
            }

            var identities = await ReadJsonAsync<List<FederatedIdentity>>(response, ct);

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
        string email, string password, string? firstName = null, string? lastName = null, bool emailVerified = false, CancellationToken ct = default)
    {
        var token = await GetAdminTokenAsync(ct);

        if (await UserExistsAsync(email, ct))
        {
            throw new KeycloakAdminException("An account with this email already exists", 409);
        }

        // Keycloak 26+ ignores inline `credentials` on user creation in some configurations.
        // Create the user without credentials first, then set the password explicitly via
        // the reset-password endpoint — the same mechanism used by ChangePasswordAsync.
        var userPayload = new
        {
            username = email,
            email,
            firstName = firstName ?? "",
            lastName = lastName ?? "",
            enabled = true,
            emailVerified,
            requiredActions = emailVerified ? Array.Empty<string>() : new[] { KeycloakRequiredActions.VerifyEmail }
        };

        using var request = CreateAdminRequest(HttpMethod.Post, AdminUrl("users"), token, userPayload);
        using var response = await _httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning("Failed to create user: {Status} - {Body}", response.StatusCode, body);
            if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                throw new KeycloakAdminException("An account with this email already exists", 409);
            }
            throw new KeycloakAdminException("Failed to create account");
        }

        // Extract the new user's ID from the Location header, then explicitly set the
        // password via the dedicated reset-password endpoint.
        var userId = response.Headers.Location?.ToString().Split('/').LastOrDefault();
        if (string.IsNullOrEmpty(userId))
            throw new KeycloakAdminException("Failed to retrieve user ID after creation");

        // The user exists in Keycloak from here on. A cancelled request must not leave it without
        // a password, so the remaining steps run to completion regardless of the caller's token.
        await SetUserPasswordAsync(userId, token, password, "Failed to set password for new user", CancellationToken.None);

        if (!emailVerified)
        {
            await SendVerificationEmailAsync(userId, token, CancellationToken.None);
        }

        _logger.LogInformation("User created: {Email}", email);
    }

    public async Task<bool> UserExistsAsync(string email, CancellationToken ct = default)
    {
        var token = await GetAdminTokenAsync(ct);
        var users = await GetAdminJsonAsync<List<KeycloakUser>>(
            $"users?email={Uri.EscapeDataString(email)}&exact=true", token, "Failed to check if user exists", ct);
        return users is { Count: > 0 };
    }

    public Task DisableUserAsync(string keycloakId, CancellationToken ct = default) => SetUserEnabledAsync(keycloakId, enabled: false, ct);

    public Task EnableUserAsync(string keycloakId, CancellationToken ct = default) => SetUserEnabledAsync(keycloakId, enabled: true, ct);

    public async Task DeleteUserAsync(string keycloakId, CancellationToken ct = default)
    {
        var token = await GetAdminTokenAsync(ct);
        await SendAdminAsync(HttpMethod.Delete, $"users/{Uri.EscapeDataString(keycloakId)}", token, "Failed to delete user from Keycloak", ct);
        _logger.LogInformation("Deleted user {KeycloakId} from Keycloak", keycloakId);
    }

    public async Task<MfaStatus> GetMfaStatusAsync(string keycloakSub, CancellationToken ct = default)
    {
        var (token, userId) = await ResolveUserAsync(keycloakSub, ct);

        var credentials = await GetAdminJsonAsync<List<KeycloakCredential>>(
            $"users/{userId}/credentials", token, "Failed to retrieve MFA status", ct) ?? new();

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

    public async Task DeleteUserCredentialAsync(string keycloakSub, string credentialId, CancellationToken ct = default)
    {
        var (token, userId) = await ResolveUserAsync(keycloakSub, ct);

        // Verify the credential belongs to this user before deleting. Fails closed: when the
        // list cannot be read, nothing is deleted.
        var credentials = await GetAdminJsonAsync<List<KeycloakCredential>>(
            $"users/{userId}/credentials", token, "Failed to verify credential ownership", ct) ?? new();
        if (!credentials.Any(c => c.Id == credentialId))
        {
            throw new KeycloakAdminException("Credential not found for this user", 404);
        }

        await SendAdminAsync(HttpMethod.Delete, $"users/{userId}/credentials/{Uri.EscapeDataString(credentialId)}", token, "Failed to remove credential", ct);

        _logger.LogInformation("Deleted credential {CredentialId} for user {Sub}", credentialId, keycloakSub);
    }

    public async Task<UserProfile> GetUserProfileAsync(string keycloakSub, CancellationToken ct = default)
    {
        var (token, userId) = await ResolveUserAsync(keycloakSub, ct);

        var userData = await GetAdminJsonAsync<KeycloakUserFull>($"users/{userId}", token, "Failed to retrieve profile", ct)
            ?? throw new KeycloakAdminException("Failed to parse user profile");

        return new UserProfile
        {
            Email = userData.Email ?? string.Empty,
            FirstName = userData.FirstName ?? string.Empty,
            LastName = userData.LastName ?? string.Empty,
            EmailVerified = userData.EmailVerified,
        };
    }

    public async Task UpdateUserProfileAsync(string keycloakSub, string firstName, string lastName, CancellationToken ct = default)
    {
        var (token, userId) = await ResolveUserAsync(keycloakSub, ct);
        await SendAdminAsync(HttpMethod.Put, $"users/{userId}", token, "Failed to update profile", ct, new { firstName, lastName });
        _logger.LogInformation("Profile updated for user {Sub}", keycloakSub);
    }

    public async Task UpdateEmailAsync(string keycloakSub, string newEmail, CancellationToken ct = default)
    {
        var (token, userId) = await ResolveUserAsync(keycloakSub, ct);

        await UpdateEmailByUserIdAsync(token, userId, newEmail, ct);
        _logger.LogInformation("Email updated for user {Sub}", keycloakSub);
    }

    public async Task UpdateEmailForAccountAsync(string? keycloakSub, string currentEmail, string newEmail, CancellationToken ct = default)
    {
        var token = await GetAdminTokenAsync(ct);
        if (!string.IsNullOrWhiteSpace(keycloakSub))
        {
            var userId = await GetKeycloakUserIdAsync(keycloakSub, token, ct);
            if (!string.IsNullOrWhiteSpace(userId))
            {
                await UpdateEmailByUserIdAsync(token, userId, newEmail, ct);
                _logger.LogInformation("Email updated for user {Sub}", keycloakSub);
                return;
            }

            _logger.LogWarning(
                "Could not resolve Keycloak subject {Sub} while confirming email change; falling back to current email lookup",
                keycloakSub);
        }

        var fallbackUserId = await GetKeycloakUserIdByEmailAsync(currentEmail, token, ct)
            ?? throw new KeycloakAdminException("User not found in Keycloak", 404);

        await UpdateEmailByUserIdAsync(token, fallbackUserId, newEmail, ct);
        _logger.LogInformation("Email updated for account with previous email {CurrentEmail}", currentEmail);
    }

    private Task UpdateEmailByUserIdAsync(string token, string userId, string newEmail, CancellationToken ct) =>
        SendAdminAsync(HttpMethod.Put, $"users/{userId}", token, "Failed to update email", ct,
            new { email = newEmail, emailVerified = true });

    public async Task EnableMfaAsync(string keycloakSub, CancellationToken ct = default)
    {
        var (token, userId) = await ResolveUserAsync(keycloakSub, ct);

        // Keycloak's user PUT replaces any field present in the body, so writing just
        // [CONFIGURE_TOTP] would drop pending actions like VERIFY_EMAIL or
        // UPDATE_PASSWORD. Read the current list and append instead.
        var userData = await GetAdminJsonAsync<KeycloakUserFull>(
            $"users/{userId}", token, "Failed to read user before enabling MFA", ct)
            ?? throw new KeycloakAdminException("Failed to parse user while enabling MFA");

        var actions = userData.RequiredActions ?? new List<string>();
        if (!actions.Contains(KeycloakRequiredActions.ConfigureTotp))
        {
            actions.Add(KeycloakRequiredActions.ConfigureTotp);
        }

        await SendAdminAsync(HttpMethod.Put, $"users/{userId}", token, "Failed to enable MFA", ct,
            new { requiredActions = actions });

        _logger.LogInformation("CONFIGURE_TOTP required action added for user {Sub}", keycloakSub);
    }

    public async Task<bool> HasRealmRoleAsync(string keycloakId, string roleName, CancellationToken ct = default)
    {
        var token = await GetAdminTokenAsync(ct);
        var roles = await GetAdminJsonAsync<List<KeycloakRole>>(
            $"users/{Uri.EscapeDataString(keycloakId)}/role-mappings/realm", token, "Failed to check realm roles", ct) ?? new();
        return roles.Any(r => r.Name == roleName);
    }

    public Task AssignRealmRoleAsync(string keycloakId, string roleName, CancellationToken ct = default)
        => ModifyRealmRoleAsync(keycloakId, roleName, assign: true, ct);

    public Task RevokeRealmRoleAsync(string keycloakId, string roleName, CancellationToken ct = default)
        => ModifyRealmRoleAsync(keycloakId, roleName, assign: false, ct);

    public async Task<int> CountRealmRoleMembersAsync(string roleName, CancellationToken ct = default)
    {
        var token = await GetAdminTokenAsync(ct);
        var users = await GetAdminJsonAsync<JsonElement[]>(
            $"roles/{Uri.EscapeDataString(roleName)}/users", token, $"Failed to count members of role '{roleName}'", ct);
        return users?.Length ?? 0;
    }

    public async Task<IReadOnlySet<string>> GetRealmRoleMemberIdsAsync(string roleName, CancellationToken ct = default)
    {
        const int pageSize = 500;
        var token = await GetAdminTokenAsync(ct);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        // Keycloak pages this endpoint (100 by default), so read until a short page.
        for (var first = 0; ; first += pageSize)
        {
            var page = await GetAdminJsonAsync<JsonElement[]>(
                $"roles/{Uri.EscapeDataString(roleName)}/users?first={first}&max={pageSize}", token,
                $"Failed to list members of role '{roleName}'", ct) ?? [];
            foreach (var user in page)
            {
                if (user.TryGetProperty("id", out var id) && id.GetString() is { } value)
                    ids.Add(value);
            }
            if (page.Length < pageSize) return ids;
        }
    }

    private async Task ModifyRealmRoleAsync(string keycloakId, string roleName, bool assign, CancellationToken ct)
    {
        var token = await GetAdminTokenAsync(ct);

        // Look up the role to get its ID (required by Keycloak API)
        KeycloakRole? role;
        using (var roleRequest = CreateAdminRequest(HttpMethod.Get, AdminUrl($"roles/{Uri.EscapeDataString(roleName)}"), token))
        using (var roleResponse = await _httpClient.SendAsync(roleRequest, ct))
        {
            if (!roleResponse.IsSuccessStatusCode)
            {
                _logger.LogWarning("Realm role {Role} not found", roleName);
                throw new KeycloakAdminException($"Realm role '{roleName}' not found", 404);
            }

            role = await ReadJsonAsync<KeycloakRole>(roleResponse, ct);
        }

        if (role?.Id == null || role.Name == null)
        {
            throw new KeycloakAdminException($"Failed to parse realm role '{roleName}'");
        }

        var method = assign ? HttpMethod.Post : HttpMethod.Delete;
        await SendAdminAsync(method, $"users/{Uri.EscapeDataString(keycloakId)}/role-mappings/realm", token,
            $"Failed to {(assign ? "assign" : "revoke")} role", ct,
            new[] { new { id = role.Id, name = role.Name } });

        _logger.LogInformation("{Action} realm role {Role} for user {KeycloakId}",
            assign ? "Assigned" : "Revoked", roleName, keycloakId);
    }

    private async Task SetUserEnabledAsync(string keycloakId, bool enabled, CancellationToken ct)
    {
        var token = await GetAdminTokenAsync(ct);
        await SendAdminAsync(HttpMethod.Put, $"users/{Uri.EscapeDataString(keycloakId)}", token,
            $"Failed to {(enabled ? "enable" : "disable")} user in Keycloak", ct, new { enabled });
        _logger.LogInformation("Set enabled={Enabled} for user {KeycloakId}", enabled, keycloakId);
    }

    private async Task SendVerificationEmailAsync(string userId, string token, CancellationToken ct)
    {
        // Best-effort — user creation succeeds even if the email dispatch fails
        try
        {
            var clientId = _kc.BackendClientId;
            var frontendUrl = _configuration.GetRequired(ConfigKeys.AppBaseUrl);
            var redirectUri = Uri.EscapeDataString(frontendUrl);

            await SendAdminAsync(HttpMethod.Put,
                $"users/{userId}/send-verify-email?client_id={clientId}&redirect_uri={redirectUri}",
                token, "Failed to send verification email", ct);
            _logger.LogInformation("Verification email sent to user {UserId}", userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending verification email to {UserId}", userId);
        }
    }

    public async Task VerifyCurrentPasswordAsync(string keycloakSub, string password, CancellationToken ct = default)
    {
        var (adminToken, userId) = await ResolveUserAsync(keycloakSub, ct);

        // Get user details to get username
        var userData = await GetAdminJsonAsync<KeycloakUser>($"users/{userId}", adminToken, "Failed to get user details", ct);
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

        using var verifyRequest = new HttpRequestMessage(HttpMethod.Post, tokenUrl)
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

        using var response = await _httpClient.SendAsync(verifyRequest, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new KeycloakAdminException("Current password is incorrect", 400);
        }
    }

    // ── helpers ─────────────────────────────────────────────────────

    /// <summary>The realm's admin REST URL for <paramref name="path"/> (no leading slash).</summary>
    private string AdminUrl(string path) => $"{_kc.EffectiveInternalBaseUrl}/admin/realms/{_kc.Realm}/{path}";

    /// <summary>
    /// Sends an admin request that must succeed and whose response body is not needed.
    /// Throws <see cref="KeycloakAdminException"/> with <paramref name="failureMessage"/> otherwise.
    /// </summary>
    private async Task SendAdminAsync(
        HttpMethod method, string path, string token, string failureMessage, CancellationToken ct, object? body = null)
    {
        using var request = CreateAdminRequest(method, AdminUrl(path), token, body);
        using var response = await _httpClient.SendAsync(request, ct);
        await EnsureSuccessAsync(response, failureMessage, ct);
    }

    /// <summary>
    /// GETs an admin resource that must exist and deserializes its JSON body.
    /// Throws <see cref="KeycloakAdminException"/> with <paramref name="failureMessage"/> on a non-success status.
    /// </summary>
    private async Task<T?> GetAdminJsonAsync<T>(string path, string token, string failureMessage, CancellationToken ct)
    {
        using var request = CreateAdminRequest(HttpMethod.Get, AdminUrl(path), token);
        using var response = await _httpClient.SendAsync(request, ct);
        await EnsureSuccessAsync(response, failureMessage, ct);
        return await ReadJsonAsync<T>(response, ct);
    }

    private static async Task<T?> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        var json = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<T>(json);
    }

    /// <summary>
    /// Throws <see cref="KeycloakAdminException"/> with <paramref name="publicErrorMessage"/>
    /// if the response isn't successful, logging the upstream body first.
    /// </summary>
    private async Task EnsureSuccessAsync(HttpResponseMessage response, string publicErrorMessage, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;

        var body = await response.Content.ReadAsStringAsync(ct);
        _logger.LogWarning("Keycloak admin request failed: {Status} - {Body}", response.StatusCode, body);
        throw new KeycloakAdminException(publicErrorMessage, 502);
    }

    /// <summary>
    /// Resolve admin token + Keycloak user ID for a given subject.
    /// Throws <see cref="KeycloakAdminException"/> if either step fails.
    /// </summary>
    private async Task<(string token, string userId)> ResolveUserAsync(string keycloakSub, CancellationToken ct)
    {
        var token = await GetAdminTokenAsync(ct);
        var userId = await GetKeycloakUserIdAsync(keycloakSub, token, ct)
            ?? throw new KeycloakAdminException("User not found in Keycloak", 404);
        return (token, userId);
    }

    /// <summary>
    /// Create an HttpRequestMessage pre-configured with the admin Bearer token, and an optional
    /// JSON <paramref name="body"/>. When using an internal URL (e.g. http://keycloak:8080), sets
    /// X-Forwarded headers so Keycloak's hostname validation accepts the request.
    /// </summary>
    private HttpRequestMessage CreateAdminRequest(HttpMethod method, string url, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
            request.Content = new StringContent(
                JsonSerializer.Serialize(body),
                Encoding.UTF8,
                "application/json");
        SetInternalProxyHeaders(request);
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

    private async Task<string> GetAdminTokenAsync(CancellationToken ct)
    {
        var cache = _tokenCache;

        // Fast path: return cached token if still valid (read is safe without lock)
        if (cache.AccessToken != null && _time.GetUtcNow().UtcDateTime < cache.Expiry.AddMinutes(-1))
        {
            return cache.AccessToken;
        }

        await cache.Lock.WaitAsync(ct);
        try
        {
            // Double-check after acquiring lock (another thread may have refreshed)
            if (cache.AccessToken != null && _time.GetUtcNow().UtcDateTime < cache.Expiry.AddMinutes(-1))
            {
                return cache.AccessToken;
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

            using var request = new HttpRequestMessage(HttpMethod.Post, tokenUrl) { Content = content };
            SetInternalProxyHeaders(request);
            using var response = await _httpClient.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                _logger.LogError(
                    "Failed to get Keycloak admin token: {Status} — {Body} (URL: {Url})",
                    response.StatusCode, body, tokenUrl);
                throw new KeycloakAdminException("Failed to authenticate with Keycloak admin");
            }

            var tokenResponse = await ReadJsonAsync<TokenResponse>(response, ct);

            if (tokenResponse?.AccessToken == null)
            {
                throw new KeycloakAdminException("Failed to authenticate with Keycloak admin");
            }

            // Write expiry BEFORE token so concurrent readers never see a new token with stale expiry
            cache.Expiry = _time.GetUtcNow().UtcDateTime.AddSeconds(tokenResponse.ExpiresIn - 30);
            cache.AccessToken = tokenResponse.AccessToken;

            return cache.AccessToken;
        }
        finally
        {
            cache.Lock.Release();
        }
    }

    private async Task<string?> GetKeycloakUserIdAsync(string keycloakSub, string token, CancellationToken ct)
    {
        // The 'sub' claim is typically the Keycloak user ID; verify by looking up the user.
        // The returned ID is escaped, so every path built from it is a single segment.
        var userId = Uri.EscapeDataString(keycloakSub);
        var url = AdminUrl($"users/{userId}");
        using var request = CreateAdminRequest(HttpMethod.Get, url, token);
        using var response = await _httpClient.SendAsync(request, ct);

        if (response.IsSuccessStatusCode)
        {
            return userId;
        }

        var body = await response.Content.ReadAsStringAsync(ct);
        _logger.LogError(
            "Keycloak user lookup failed for {Sub}: {Status} — {Body} (URL: {Url})",
            keycloakSub, response.StatusCode, body, url);
        return null;
    }

    public async Task<bool> SendExecuteActionsEmailAsync(
        string email, IReadOnlyCollection<string> actions, CancellationToken ct = default)
    {
        var token = await GetAdminTokenAsync(ct);

        var userId = await GetKeycloakUserIdByEmailAsync(email, token, ct);
        if (userId is null)
        {
            _logger.LogWarning("No Keycloak user for {Email}; cannot send required-actions email", email);
            return false;
        }

        // client_id and redirect_uri are what give the mail a way back. Without them
        // Keycloak has no client to return to: it ends the flow on its own info page
        // with no link, or leaves the person in the account console — which is not
        // where someone who just set their first password expects to arrive.
        // Same pair, same client, as SendVerificationEmailAsync above.
        var clientId = _kc.BackendClientId;
        var redirectUri = Uri.EscapeDataString(_configuration.GetRequired(ConfigKeys.AppBaseUrl));

        await SendAdminAsync(HttpMethod.Put,
            $"users/{userId}/execute-actions-email?client_id={clientId}&redirect_uri={redirectUri}",
            token, "Failed to send required-actions email", ct, actions);

        _logger.LogInformation("Sent required-actions email ({Actions}) to {Email}",
            string.Join(",", actions), email);
        return true;
    }

    private async Task<string?> GetKeycloakUserIdByEmailAsync(string email, string token, CancellationToken ct)
    {
        var users = await GetAdminJsonAsync<List<KeycloakUser>>(
            $"users?email={Uri.EscapeDataString(email)}&exact=true", token, "Failed to find user by email", ct);
        return users?.FirstOrDefault()?.Id;
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

        [JsonPropertyName("requiredActions")]
        public List<string>? RequiredActions { get; set; }
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
