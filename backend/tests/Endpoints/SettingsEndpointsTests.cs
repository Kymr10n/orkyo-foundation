using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Services;
using Npgsql;

namespace Orkyo.Foundation.Tests.Endpoints;

/// <summary>
/// Integration tests for the settings endpoints.
/// Tests GET/PUT/DELETE /api/settings against the real DB.
/// GET is member-read (tenant config is read app-wide); PUT/DELETE require Admin.
/// </summary>
[Collection("Database collection")]
public class SettingsEndpointsTests
{
    private readonly HttpClient _client;
    private readonly DatabaseFixture _fixture;
    private const string TenantSlug = TestConstants.TenantSlug;

    public SettingsEndpointsTests(DatabaseFixture databaseFixture)
    {
        _fixture = databaseFixture;
        _client = databaseFixture.Factory.CreateClient();
        _client.DefaultRequestHeaders.Add("X-Tenant-Slug", TenantSlug);
    }

    private string? _cachedAdminToken;

    private async Task<string> GetAdminAuthTokenAsync()
    {
        if (_cachedAdminToken != null) return _cachedAdminToken;

        var email = $"settings_admin_{Guid.NewGuid()}@example.com";
        var userId = await DatabaseTestUtils.CreateTestUserAsync(email, "Settings Admin", TenantSlug, "admin", active: true);
        var tenantId = Guid.Parse("00000000-0000-0000-0000-000000000001");

        var tokenData = new
        {
            UserId = userId.ToString(),
            Email = email,
            DisplayName = "Settings Admin",
            TenantId = tenantId.ToString(),
            TenantSlug = TenantSlug,
            IsTenantAdmin = true,
            Role = "admin"
        };

        var json = JsonSerializer.Serialize(tokenData);
        _cachedAdminToken = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json));
        return _cachedAdminToken;
    }

    /// <summary>Create an authenticated GET/DELETE request with admin token.</summary>
    private async Task<HttpRequestMessage> AuthRequest(HttpMethod method, string url, object? content = null)
    {
        var msg = new HttpRequestMessage(method, url);
        msg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await GetAdminAuthTokenAsync());
        if (content != null)
            msg.Content = JsonContent.Create(content);
        return msg;
    }

    /// <summary>Remove all setting overrides and clear in-memory cache between tests.</summary>
    private async Task CleanupSettingsAsync()
    {
        // Clean tenant-level overrides
        var tenantConnStr = $"Host=localhost;Port={_fixture.DatabasePort};Database={TestConstants.TenantDatabase};Username=postgres;Password=postgres";
        await using var conn = new NpgsqlConnection(tenantConnStr);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("DELETE FROM tenant_settings", conn);
        await cmd.ExecuteNonQueryAsync();

        // Clean site-level overrides in control_plane
        var cpConnStr = $"Host=localhost;Port={_fixture.DatabasePort};Database=control_plane;Username=postgres;Password=postgres";
        await using var cpConn = new NpgsqlConnection(cpConnStr);
        await cpConn.OpenAsync();
        await using var cpCmd = new NpgsqlCommand("DELETE FROM site_settings", cpConn);
        await cpCmd.ExecuteNonQueryAsync();

        // Also clear the static in-memory cache so stale entries don't bleed between tests
        TenantSettingsService.ClearCache();
    }

    // ── GET /api/settings ───────────────────────────────────────────

    [Fact]
    public async Task GetSettings_Unauthenticated_ReturnsUnauthorized()
    {
        // No bearer token → 401
        var response = await _client.GetAsync("/api/settings");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetSettings_Viewer_ReturnsOk()
    {
        // GET /api/settings is member-read: tenant config (scheduling, working hours, …) is read
        // app-wide (e.g. the auto-schedule flow). Only PUT/DELETE require Admin.
        var email = $"settings_viewer_{Guid.NewGuid()}@example.com";
        var userId = await DatabaseTestUtils.CreateTestUserAsync(email, "Settings Viewer", TenantSlug, "viewer", active: true);
        var tenantId = Guid.Parse("00000000-0000-0000-0000-000000000001");

        var tokenData = new
        {
            UserId = userId.ToString(),
            Email = email,
            DisplayName = "Settings Viewer",
            TenantId = tenantId.ToString(),
            TenantSlug = TenantSlug,
            IsTenantAdmin = false,
            Role = "viewer"
        };

        var json = JsonSerializer.Serialize(tokenData);
        var viewerToken = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json));

        var msg = new HttpRequestMessage(HttpMethod.Get, "/api/settings");
        msg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", viewerToken);

        var response = await _client.SendAsync(msg);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetSettings_Editor_ReturnsOk()
    {
        // Members (incl. Editors) can read tenant settings; managing them stays Admin-only.
        var email = $"settings_editor_{Guid.NewGuid()}@example.com";
        var userId = await DatabaseTestUtils.CreateTestUserAsync(email, "Settings Editor", TenantSlug, "editor", active: true);
        var tenantId = Guid.Parse("00000000-0000-0000-0000-000000000001");

        var tokenData = new
        {
            UserId = userId.ToString(),
            Email = email,
            DisplayName = "Settings Editor",
            TenantId = tenantId.ToString(),
            TenantSlug = TenantSlug,
            IsTenantAdmin = false,
            Role = "editor"
        };

        var json = JsonSerializer.Serialize(tokenData);
        var editorToken = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json));

        var msg = new HttpRequestMessage(HttpMethod.Get, "/api/settings");
        msg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", editorToken);

        var response = await _client.SendAsync(msg);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task UpdateSettings_Editor_ReturnsForbidden()
    {
        // Writes remain Admin-only even though reads are member-open.
        var email = $"settings_editor_w_{Guid.NewGuid()}@example.com";
        var userId = await DatabaseTestUtils.CreateTestUserAsync(email, "Settings Editor W", TenantSlug, "editor", active: true);
        var tenantId = Guid.Parse("00000000-0000-0000-0000-000000000001");

        var tokenData = new
        {
            UserId = userId.ToString(),
            Email = email,
            DisplayName = "Settings Editor W",
            TenantId = tenantId.ToString(),
            TenantSlug = TenantSlug,
            IsTenantAdmin = false,
            Role = "editor"
        };

        var json = JsonSerializer.Serialize(tokenData);
        var editorToken = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json));

        var msg = new HttpRequestMessage(HttpMethod.Put, "/api/settings")
        {
            Content = JsonContent.Create(new { settings = new Dictionary<string, string> { ["working_day_start"] = "08:00" } }),
        };
        msg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", editorToken);

        var response = await _client.SendAsync(msg);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetSettings_ReturnsOnlyTenantScopedDescriptors()
    {
        await CleanupSettingsAsync();

        var response = await _client.SendAsync(await AuthRequest(HttpMethod.Get, "/api/settings"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var settings = json.GetProperty("settings");
        // Tenant context only returns 7 tenant-scoped descriptors (not all 19)
        settings.GetArrayLength().Should().Be(7);
    }

    [Fact]
    public async Task GetSettings_EachDescriptorHasRequiredFields()
    {
        await CleanupSettingsAsync();

        var response = await _client.SendAsync(await AuthRequest(HttpMethod.Get, "/api/settings"));
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = json.GetProperty("settings").EnumerateArray().ToList();

        foreach (var item in items)
        {
            item.TryGetProperty("key", out _).Should().BeTrue();
            item.TryGetProperty("category", out _).Should().BeTrue();
            item.TryGetProperty("displayName", out _).Should().BeTrue();
            item.TryGetProperty("description", out _).Should().BeTrue();
            item.TryGetProperty("valueType", out _).Should().BeTrue();
            item.TryGetProperty("defaultValue", out _).Should().BeTrue();
            item.TryGetProperty("scope", out _).Should().BeTrue();
            item.TryGetProperty("currentValue", out _).Should().BeTrue();
        }
    }

    [Fact]
    public async Task GetSettings_DefaultValues_MatchCurrentValues()
    {
        await CleanupSettingsAsync();

        var response = await _client.SendAsync(await AuthRequest(HttpMethod.Get, "/api/settings"));
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = json.GetProperty("settings").EnumerateArray().ToList();

        // When no overrides exist, currentValue == defaultValue for all
        foreach (var item in items)
        {
            var key = item.GetProperty("key").GetString();
            var defaultVal = item.GetProperty("defaultValue").GetString();
            var currentVal = item.GetProperty("currentValue").GetString();

            currentVal.Should().Be(defaultVal,
                $"setting '{key}' currentValue should match default when no override exists");
        }
    }

    // ── PUT /api/settings ───────────────────────────────────────────

    [Fact]
    public async Task UpdateSettings_Unauthenticated_ReturnsUnauthorized()
    {
        var response = await _client.PutAsJsonAsync("/api/settings", new
        {
            settings = new Dictionary<string, string>
            {
                ["search.search_default_page_size"] = "30"
            }
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UpdateSettings_ValidIntSetting_Succeeds()
    {
        await CleanupSettingsAsync();

        var response = await _client.SendAsync(await AuthRequest(HttpMethod.Put, "/api/settings", new
        {
            settings = new Dictionary<string, string>
            {
                ["search.search_default_page_size"] = "30"
            }
        }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = json.GetProperty("settings").EnumerateArray().ToList();
        var expirySetting = items.First(s =>
            s.GetProperty("key").GetString() == "search.search_default_page_size");

        expirySetting.GetProperty("currentValue").GetString().Should().Be("30");
    }

    [Fact]
    public async Task UpdateSettings_ValidStringSetting_Succeeds()
    {
        await CleanupSettingsAsync();

        var response = await _client.SendAsync(await AuthRequest(HttpMethod.Put, "/api/settings", new
        {
            settings = new Dictionary<string, string>
            {
                ["branding.branding_product_name"] = "Acme Corp"
            }
        }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = json.GetProperty("settings").EnumerateArray().ToList();
        var brandSetting = items.First(s =>
            s.GetProperty("key").GetString() == "branding.branding_product_name");

        brandSetting.GetProperty("currentValue").GetString().Should().Be("Acme Corp");
    }

    [Fact]
    public async Task UpdateSettings_MultipleSettings_AllPersisted()
    {
        await CleanupSettingsAsync();

        await _client.SendAsync(await AuthRequest(HttpMethod.Put, "/api/settings", new
        {
            settings = new Dictionary<string, string>
            {
                ["search.search_default_page_size"] = "30",
                ["branding.branding_product_name"] = "Acme Corp",
                ["branding.branding_primary_color"] = "#ff0000"
            }
        }));

        // Verify via GET
        var getResponse = await _client.SendAsync(await AuthRequest(HttpMethod.Get, "/api/settings"));
        var json = await getResponse.Content.ReadFromJsonAsync<JsonElement>();
        var items = json.GetProperty("settings").EnumerateArray().ToList();

        items.First(s => s.GetProperty("key").GetString() == "search.search_default_page_size")
            .GetProperty("currentValue").GetString().Should().Be("30");
        items.First(s => s.GetProperty("key").GetString() == "branding.branding_product_name")
            .GetProperty("currentValue").GetString().Should().Be("Acme Corp");
        items.First(s => s.GetProperty("key").GetString() == "branding.branding_primary_color")
            .GetProperty("currentValue").GetString().Should().Be("#ff0000");
    }

    [Fact]
    public async Task UpdateSettings_EmptyRequest_ReturnsBadRequest()
    {
        var response = await _client.SendAsync(await AuthRequest(HttpMethod.Put, "/api/settings", new
        {
            settings = new Dictionary<string, string>()
        }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateSettings_UnknownKey_ReturnsBadRequest()
    {
        var response = await _client.SendAsync(await AuthRequest(HttpMethod.Put, "/api/settings", new
        {
            settings = new Dictionary<string, string>
            {
                ["unknown.bogus_key"] = "42"
            }
        }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateSettings_IntBelowMinimum_ReturnsBadRequest()
    {
        var response = await _client.SendAsync(await AuthRequest(HttpMethod.Put, "/api/settings", new
        {
            settings = new Dictionary<string, string>
            {
                ["search.search_default_page_size"] = "3"   // min is 5
            }
        }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateSettings_IntAboveMaximum_ReturnsBadRequest()
    {
        var response = await _client.SendAsync(await AuthRequest(HttpMethod.Put, "/api/settings", new
        {
            settings = new Dictionary<string, string>
            {
                ["search.search_default_page_size"] = "200"   // max is 100
            }
        }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateSettings_InvalidIntValue_ReturnsBadRequest()
    {
        var response = await _client.SendAsync(await AuthRequest(HttpMethod.Put, "/api/settings", new
        {
            settings = new Dictionary<string, string>
            {
                ["search.search_default_page_size"] = "abc"
            }
        }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── String format validation ────────────────────────────────────

    [Fact]
    public async Task UpdateSettings_InvalidHexColor_ReturnsBadRequest()
    {
        var response = await _client.SendAsync(await AuthRequest(HttpMethod.Put, "/api/settings", new
        {
            settings = new Dictionary<string, string>
            {
                ["branding.branding_primary_color"] = "not-a-color"
            }
        }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateSettings_ValidHexColor_Succeeds()
    {
        await CleanupSettingsAsync();

        var response = await _client.SendAsync(await AuthRequest(HttpMethod.Put, "/api/settings", new
        {
            settings = new Dictionary<string, string>
            {
                ["branding.branding_primary_color"] = "#abcdef"
            }
        }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task UpdateSettings_SiteScoped_FromTenantContext_ReturnsBadRequest()
    {
        // upload_allowed_mime_types is site-scoped → tenant context rejects with ArgumentException → 400
        var response = await _client.SendAsync(await AuthRequest(HttpMethod.Put, "/api/settings", new
        {
            settings = new Dictionary<string, string>
            {
                ["uploads.upload_allowed_mime_types"] = "image/png"
            }
        }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateSettings_ProductNameWithHtml_ReturnsBadRequest()
    {
        var response = await _client.SendAsync(await AuthRequest(HttpMethod.Put, "/api/settings", new
        {
            settings = new Dictionary<string, string>
            {
                ["branding.branding_product_name"] = "<script>alert('xss')</script>"
            }
        }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateSettings_ValueExceedsMaxLength_ReturnsBadRequest()
    {
        var response = await _client.SendAsync(await AuthRequest(HttpMethod.Put, "/api/settings", new
        {
            settings = new Dictionary<string, string>
            {
                ["branding.branding_product_name"] = new string('A', 501)
            }
        }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── DELETE /api/settings/{key} ──────────────────────────────────

    [Fact]
    public async Task ResetSetting_Unauthenticated_ReturnsUnauthorized()
    {
        var response = await _client.DeleteAsync("/api/settings/search.search_default_page_size");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ResetSetting_UnknownKey_ReturnsNotFound()
    {
        var response = await _client.SendAsync(
            await AuthRequest(HttpMethod.Delete, "/api/settings/unknown.bogus_key"));
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ResetSetting_ExistingOverride_ResetsToDefault()
    {
        await CleanupSettingsAsync();

        // First set an override
        await _client.SendAsync(await AuthRequest(HttpMethod.Put, "/api/settings", new
        {
            settings = new Dictionary<string, string>
            {
                ["search.search_default_page_size"] = "50"
            }
        }));

        // Verify it was set
        var getResponse = await _client.SendAsync(await AuthRequest(HttpMethod.Get, "/api/settings"));
        var json = await getResponse.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("settings").EnumerateArray()
            .First(s => s.GetProperty("key").GetString() == "search.search_default_page_size")
            .GetProperty("currentValue").GetString().Should().Be("50");

        // Delete the override
        var deleteResponse = await _client.SendAsync(
            await AuthRequest(HttpMethod.Delete, "/api/settings/search.search_default_page_size"));
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify it reverted to default
        var afterDelete = await _client.SendAsync(await AuthRequest(HttpMethod.Get, "/api/settings"));
        var afterJson = await afterDelete.Content.ReadFromJsonAsync<JsonElement>();
        afterJson.GetProperty("settings").EnumerateArray()
            .First(s => s.GetProperty("key").GetString() == "search.search_default_page_size")
            .GetProperty("currentValue").GetString().Should().Be("20");
    }

    [Fact]
    public async Task ResetSetting_NoOverride_ReturnsNotFound()
    {
        await CleanupSettingsAsync();

        var response = await _client.SendAsync(
            await AuthRequest(HttpMethod.Delete, "/api/settings/search.search_default_page_size"));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Round-trip ──────────────────────────────────────────────────

    [Fact]
    public async Task Settings_FullRoundTrip_SetUpdateResetVerify()
    {
        await CleanupSettingsAsync();

        // 1. Verify initial defaults
        var r1 = await _client.SendAsync(await AuthRequest(HttpMethod.Get, "/api/settings"));
        var j1 = await r1.Content.ReadFromJsonAsync<JsonElement>();
        j1.GetProperty("settings").EnumerateArray()
            .First(s => s.GetProperty("key").GetString() == "search.search_default_page_size")
            .GetProperty("currentValue").GetString().Should().Be("20");

        // 2. Set override
        await _client.SendAsync(await AuthRequest(HttpMethod.Put, "/api/settings", new
        {
            settings = new Dictionary<string, string>
            {
                ["search.search_default_page_size"] = "50"
            }
        }));

        // 3. Verify override
        var r2 = await _client.SendAsync(await AuthRequest(HttpMethod.Get, "/api/settings"));
        var j2 = await r2.Content.ReadFromJsonAsync<JsonElement>();
        j2.GetProperty("settings").EnumerateArray()
            .First(s => s.GetProperty("key").GetString() == "search.search_default_page_size")
            .GetProperty("currentValue").GetString().Should().Be("50");

        // 4. Update the override to a different value
        await _client.SendAsync(await AuthRequest(HttpMethod.Put, "/api/settings", new
        {
            settings = new Dictionary<string, string>
            {
                ["search.search_default_page_size"] = "75"
            }
        }));

        var r3 = await _client.SendAsync(await AuthRequest(HttpMethod.Get, "/api/settings"));
        var j3 = await r3.Content.ReadFromJsonAsync<JsonElement>();
        j3.GetProperty("settings").EnumerateArray()
            .First(s => s.GetProperty("key").GetString() == "search.search_default_page_size")
            .GetProperty("currentValue").GetString().Should().Be("75");

        // 5. Reset to default
        var deleteResp = await _client.SendAsync(
            await AuthRequest(HttpMethod.Delete, "/api/settings/search.search_default_page_size"));
        deleteResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 6. Verify default restored
        var r4 = await _client.SendAsync(await AuthRequest(HttpMethod.Get, "/api/settings"));
        var j4 = await r4.Content.ReadFromJsonAsync<JsonElement>();
        j4.GetProperty("settings").EnumerateArray()
            .First(s => s.GetProperty("key").GetString() == "search.search_default_page_size")
            .GetProperty("currentValue").GetString().Should().Be("20");
    }
}
