using System.Net;
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
        _connectionString = _fixture.ControlPlaneConnectionString;
    }

    // ── GET /api/admin/settings ─────────────────────────────────

    [Fact]
    public async Task GetSettings_NonSiteAdmin_Returns403()
    {
        var token = (await DatabaseTestUtils.CreateLinkedUserAsync("settings-regular")).Token;
        var request = TestHelpers.AuthRequest(HttpMethod.Get, "/api/admin/settings", token);
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetSettings_SiteAdmin_ReturnsAllSections()
    {
        var (_, token) = await DatabaseTestUtils.CreateLinkedUserAsync("settings-admin", siteAdmin: true);
        var request = TestHelpers.AuthRequest(HttpMethod.Get, "/api/admin/settings", token);

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
    public async Task UpdateSettings_NonSiteAdmin_Returns403()
    {
        var token = (await DatabaseTestUtils.CreateLinkedUserAsync("settings-regular")).Token;
        var request = TestHelpers.AuthRequest(HttpMethod.Put, "/api/admin/settings", token, new { settings = new Dictionary<string, string> { ["DefaultTimezone"] = "Europe/Zurich" } });
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UpdateSettings_ValidChange_ReturnsUpdatedRuntime()
    {
        var (_, token) = await DatabaseTestUtils.CreateLinkedUserAsync("settings-admin", siteAdmin: true);

        var request = TestHelpers.AuthRequest(HttpMethod.Put, "/api/admin/settings", token, new
        {
            settings = new Dictionary<string, string>
            {
                ["DefaultTimezone"] = "Europe/Zurich",
                ["BrandingName"] = "TestOrg",
            }
        });

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
        var (_, token) = await DatabaseTestUtils.CreateLinkedUserAsync("settings-admin", siteAdmin: true);

        var request = TestHelpers.AuthRequest(HttpMethod.Put, "/api/admin/settings", token, new
        {
            settings = new Dictionary<string, string>
            {
                ["NonExistentSetting"] = "value"
            }
        });

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateSettings_EmptySettings_Returns400()
    {
        var (_, token) = await DatabaseTestUtils.CreateLinkedUserAsync("settings-admin", siteAdmin: true);

        var request = TestHelpers.AuthRequest(HttpMethod.Put, "/api/admin/settings", token, new { settings = new Dictionary<string, string>() });

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateSettings_InvalidBoolValue_Returns400()
    {
        var (_, token) = await DatabaseTestUtils.CreateLinkedUserAsync("settings-admin", siteAdmin: true);

        var request = TestHelpers.AuthRequest(HttpMethod.Put, "/api/admin/settings", token, new
        {
            settings = new Dictionary<string, string>
            {
                ["HolidayProviderEnabled"] = "not-a-bool"
            }
        });

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateSettings_RecordsAuditEvent()
    {
        var (userId, token) = await DatabaseTestUtils.CreateLinkedUserAsync("settings-admin", siteAdmin: true);

        var request = TestHelpers.AuthRequest(HttpMethod.Put, "/api/admin/settings", token, new
        {
            settings = new Dictionary<string, string>
            {
                ["BrandingName"] = $"AuditTest-{Guid.NewGuid():N}"
            }
        });

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
        var (_, token) = await DatabaseTestUtils.CreateLinkedUserAsync("settings-admin", siteAdmin: true);

        // Use the DB key format (general.default_timezone) instead of property name
        var request = TestHelpers.AuthRequest(HttpMethod.Put, "/api/admin/settings", token, new
        {
            settings = new Dictionary<string, string>
            {
                ["general.default_timezone"] = "America/New_York"
            }
        });

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("America/New_York", body.GetProperty("runtime").GetProperty("defaultTimezone").GetString());
    }

    [Fact]
    public async Task GetSettings_ReflectsUpdatedValues()
    {
        var (_, token) = await DatabaseTestUtils.CreateLinkedUserAsync("settings-admin", siteAdmin: true);
        var uniqueName = $"GetReflect-{Guid.NewGuid():N}";

        // Update
        var updateReq = TestHelpers.AuthRequest(HttpMethod.Put, "/api/admin/settings", token, new
        {
            settings = new Dictionary<string, string> { ["BrandingName"] = uniqueName }
        });
        var updateResp = await _client.SendAsync(updateReq);
        Assert.Equal(HttpStatusCode.OK, updateResp.StatusCode);

        // Read back
        var getReq = TestHelpers.AuthRequest(HttpMethod.Get, "/api/admin/settings", token);
        var getResp = await _client.SendAsync(getReq);
        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);

        var body = await getResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(uniqueName, body.GetProperty("runtime").GetProperty("brandingName").GetString());
    }
}
