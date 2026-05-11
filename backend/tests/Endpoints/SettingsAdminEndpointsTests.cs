using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using Orkyo.Foundation.Tests.Mocks;

namespace Orkyo.Foundation.Tests.Endpoints;

/// <summary>
/// Integration tests for the admin settings endpoints.
/// GET /api/admin/settings — returns runtime + deployment + system info.
/// PUT /api/admin/settings — updates runtime settings with audit trail.
/// </summary>
[Collection("Database collection")]
public class SettingsAdminEndpointsTests
{
    private readonly HttpClient _client;
    private readonly DatabaseFixture _fixture;
    private readonly string _connectionString;

    public SettingsAdminEndpointsTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.Factory.CreateClient();
        _connectionString = $"Host=localhost;Port={_fixture.DatabasePort};Database=control_plane;Username=postgres;Password=postgres";
    }

    private async Task<(Guid UserId, string Token)> CreateSiteAdminAsync()
    {
        var userId = Guid.NewGuid();
        var email = $"settings-admin-{userId}@test.com";
        var keycloakSub = $"kc-{userId}";

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();

        await using var userCmd = new NpgsqlCommand(
            "INSERT INTO users (id, email, display_name, status) VALUES (@id, @email, 'Settings Admin', 'active')",
            conn);
        userCmd.Parameters.AddWithValue("id", userId);
        userCmd.Parameters.AddWithValue("email", email);
        await userCmd.ExecuteNonQueryAsync();

        await using var linkCmd = new NpgsqlCommand(
            "INSERT INTO user_identities (id, user_id, provider, provider_subject, provider_email) VALUES (@id, @userId, 'keycloak', @sub, @email)",
            conn);
        linkCmd.Parameters.AddWithValue("id", Guid.NewGuid());
        linkCmd.Parameters.AddWithValue("userId", userId);
        linkCmd.Parameters.AddWithValue("sub", keycloakSub);
        linkCmd.Parameters.AddWithValue("email", email);
        await linkCmd.ExecuteNonQueryAsync();

        var tokenData = new
        {
            UserId = userId.ToString(),
            Email = email,
            DisplayName = "Settings Admin",
            TenantId = Guid.Empty.ToString(),
            TenantSlug = "",
            IsTenantAdmin = false,
            Role = "viewer",
            Sub = keycloakSub,
            RealmRoles = new[] { "user", "site-admin" },
        };
        var json = JsonSerializer.Serialize(tokenData);
        var token = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json));

        return (userId, token);
    }

    private async Task<string> CreateRegularUserTokenAsync()
    {
        var userId = Guid.NewGuid();
        var email = $"settings-regular-{userId}@test.com";
        var keycloakSub = $"kc-{userId}";

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "INSERT INTO users (id, email, display_name, status) VALUES (@id, @email, 'Regular User', 'active')",
            conn);
        cmd.Parameters.AddWithValue("id", userId);
        cmd.Parameters.AddWithValue("email", email);
        await cmd.ExecuteNonQueryAsync();

        await using var linkCmd = new NpgsqlCommand(
            "INSERT INTO user_identities (id, user_id, provider, provider_subject, provider_email) VALUES (@id, @userId, 'keycloak', @sub, @email)",
            conn);
        linkCmd.Parameters.AddWithValue("id", Guid.NewGuid());
        linkCmd.Parameters.AddWithValue("userId", userId);
        linkCmd.Parameters.AddWithValue("sub", keycloakSub);
        linkCmd.Parameters.AddWithValue("email", email);
        await linkCmd.ExecuteNonQueryAsync();

        var tokenData = new
        {
            UserId = userId.ToString(),
            Email = email,
            DisplayName = "Regular User",
            TenantId = Guid.Empty.ToString(),
            TenantSlug = "",
            IsTenantAdmin = false,
            Role = "viewer",
            Sub = keycloakSub,
            RealmRoles = new[] { "user" },
        };
        var json = JsonSerializer.Serialize(tokenData);
        return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json));
    }

    // ── GET /api/admin/settings ─────────────────────────────────

    [Fact]
    public async Task GetSettings_NoAuth_Returns401()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/settings");
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetSettings_NonSiteAdmin_Returns403()
    {
        var token = await CreateRegularUserTokenAsync();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/settings");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetSettings_SiteAdmin_ReturnsAllSections()
    {
        var (_, token) = await CreateSiteAdminAsync();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/settings");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        // Runtime section
        Assert.True(body.TryGetProperty("runtime", out var runtime));
        Assert.True(runtime.TryGetProperty("defaultTimezone", out _));
        Assert.True(runtime.TryGetProperty("workingHoursStart", out _));
        Assert.True(runtime.TryGetProperty("workingHoursEnd", out _));
        Assert.True(runtime.TryGetProperty("holidayProviderEnabled", out _));
        Assert.True(runtime.TryGetProperty("brandingName", out _));
        Assert.True(runtime.TryGetProperty("brandingLogoUrl", out _));

        // Deployment section (read-only, redacted)
        Assert.True(body.TryGetProperty("deployment", out var deployment));
        Assert.True(deployment.TryGetProperty("publicUrl", out _));
        Assert.True(deployment.TryGetProperty("authPublicUrl", out _));
        Assert.True(deployment.TryGetProperty("smtpHost", out _));
        Assert.True(deployment.TryGetProperty("logLevel", out _));

        // System info
        Assert.True(body.TryGetProperty("systemInfo", out var sysInfo));
        Assert.True(sysInfo.TryGetProperty("version", out _));
        Assert.True(sysInfo.TryGetProperty("databaseStatus", out var dbStatus));
        Assert.Equal("healthy", dbStatus.GetString());
        Assert.True(sysInfo.TryGetProperty("authProvider", out var authProvider));
        Assert.Equal("keycloak", authProvider.GetString());
    }

    // ── PUT /api/admin/settings ─────────────────────────────────

    [Fact]
    public async Task UpdateSettings_NoAuth_Returns401()
    {
        var request = new HttpRequestMessage(HttpMethod.Put, "/api/admin/settings")
        {
            Content = JsonContent.Create(new { settings = new { DefaultTimezone = "Europe/Zurich" } })
        };
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UpdateSettings_NonSiteAdmin_Returns403()
    {
        var token = await CreateRegularUserTokenAsync();
        var request = new HttpRequestMessage(HttpMethod.Put, "/api/admin/settings")
        {
            Content = JsonContent.Create(new { settings = new Dictionary<string, string> { ["DefaultTimezone"] = "Europe/Zurich" } })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UpdateSettings_ValidChange_ReturnsUpdatedRuntime()
    {
        var (_, token) = await CreateSiteAdminAsync();

        var request = new HttpRequestMessage(HttpMethod.Put, "/api/admin/settings")
        {
            Content = JsonContent.Create(new
            {
                settings = new Dictionary<string, string>
                {
                    ["DefaultTimezone"] = "Europe/Zurich",
                    ["BrandingName"] = "TestOrg",
                }
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.TryGetProperty("runtime", out var runtime));
        Assert.Equal("Europe/Zurich", runtime.GetProperty("defaultTimezone").GetString());
        Assert.Equal("TestOrg", runtime.GetProperty("brandingName").GetString());

        Assert.True(body.TryGetProperty("updatedKeys", out var keys));
        Assert.Equal(2, keys.GetArrayLength());
    }

    [Fact]
    public async Task UpdateSettings_UnknownKey_Returns400()
    {
        var (_, token) = await CreateSiteAdminAsync();

        var request = new HttpRequestMessage(HttpMethod.Put, "/api/admin/settings")
        {
            Content = JsonContent.Create(new
            {
                settings = new Dictionary<string, string>
                {
                    ["NonExistentSetting"] = "value"
                }
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateSettings_EmptySettings_Returns400()
    {
        var (_, token) = await CreateSiteAdminAsync();

        var request = new HttpRequestMessage(HttpMethod.Put, "/api/admin/settings")
        {
            Content = JsonContent.Create(new { settings = new Dictionary<string, string>() })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateSettings_InvalidBoolValue_Returns400()
    {
        var (_, token) = await CreateSiteAdminAsync();

        var request = new HttpRequestMessage(HttpMethod.Put, "/api/admin/settings")
        {
            Content = JsonContent.Create(new
            {
                settings = new Dictionary<string, string>
                {
                    ["HolidayProviderEnabled"] = "not-a-bool"
                }
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateSettings_RecordsAuditEvent()
    {
        var (userId, token) = await CreateSiteAdminAsync();

        var request = new HttpRequestMessage(HttpMethod.Put, "/api/admin/settings")
        {
            Content = JsonContent.Create(new
            {
                settings = new Dictionary<string, string>
                {
                    ["BrandingName"] = $"AuditTest-{Guid.NewGuid():N}"
                }
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Verify audit event was recorded
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM audit_events WHERE actor_user_id = @userId AND action = 'settings.updated' AND target_type = 'site_setting'",
            conn);
        cmd.Parameters.AddWithValue("userId", userId);
        var count = (long)(await cmd.ExecuteScalarAsync())!;
        Assert.True(count >= 1, "Expected at least one settings.updated audit event");
    }

    [Fact]
    public async Task UpdateSettings_AcceptsDbKeyFormat()
    {
        var (_, token) = await CreateSiteAdminAsync();

        // Use the DB key format (general.default_timezone) instead of property name
        var request = new HttpRequestMessage(HttpMethod.Put, "/api/admin/settings")
        {
            Content = JsonContent.Create(new
            {
                settings = new Dictionary<string, string>
                {
                    ["general.default_timezone"] = "America/New_York"
                }
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("America/New_York", body.GetProperty("runtime").GetProperty("defaultTimezone").GetString());
    }

    [Fact]
    public async Task GetSettings_ReflectsUpdatedValues()
    {
        var (_, token) = await CreateSiteAdminAsync();
        var uniqueName = $"GetReflect-{Guid.NewGuid():N}";

        // Update
        var updateReq = new HttpRequestMessage(HttpMethod.Put, "/api/admin/settings")
        {
            Content = JsonContent.Create(new
            {
                settings = new Dictionary<string, string> { ["BrandingName"] = uniqueName }
            })
        };
        updateReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var updateResp = await _client.SendAsync(updateReq);
        Assert.Equal(HttpStatusCode.OK, updateResp.StatusCode);

        // Read back
        var getReq = new HttpRequestMessage(HttpMethod.Get, "/api/admin/settings");
        getReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var getResp = await _client.SendAsync(getReq);
        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);

        var body = await getResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(uniqueName, body.GetProperty("runtime").GetProperty("brandingName").GetString());
    }
}
