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
        _client.DefaultRequestHeaders.Add(HeaderConstants.TenantSlug, TenantSlug);
    }

    private Task<string>? _token;
    private Task<string> Token => _token ??= DatabaseFixture.CreateMemberTokenAsync(RoleConstants.Admin);

    /// <summary>Remove all setting overrides and clear in-memory cache between tests.</summary>
    private async Task CleanupSettingsAsync()
    {
        // Clean tenant-level overrides
        var tenantConnStr = _fixture.TenantConnectionString;
        await using var conn = new NpgsqlConnection(tenantConnStr);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("DELETE FROM tenant_settings", conn);
        await cmd.ExecuteNonQueryAsync();

        // Clean site-level overrides in control_plane
        var cpConnStr = _fixture.ControlPlaneConnectionString;
        await using var cpConn = new NpgsqlConnection(cpConnStr);
        await cpConn.OpenAsync();
        await using var cpCmd = new NpgsqlCommand("DELETE FROM site_settings", cpConn);
        await cpCmd.ExecuteNonQueryAsync();

        // Also clear the host's in-memory cache so stale entries don't bleed between tests
        _fixture.Factory.ResetCaches();
    }

    // ── GET /api/settings ───────────────────────────────────────────

    [Fact]
    public async Task GetSettings_ReturnsOnlyTenantScopedDescriptors()
    {
        await CleanupSettingsAsync();

        var response = await _client.SendAsync(TestHelpers.AuthRequest(HttpMethod.Get, "/api/settings", await Token));

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

        var response = await _client.SendAsync(TestHelpers.AuthRequest(HttpMethod.Get, "/api/settings", await Token));
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

        var response = await _client.SendAsync(TestHelpers.AuthRequest(HttpMethod.Get, "/api/settings", await Token));
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
    public async Task UpdateSettings_ValidIntSetting_Succeeds()
    {
        await CleanupSettingsAsync();

        var response = await _client.SendAsync(TestHelpers.AuthRequest(HttpMethod.Put, "/api/settings", await Token, new
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

        var response = await _client.SendAsync(TestHelpers.AuthRequest(HttpMethod.Put, "/api/settings", await Token, new
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

        await _client.SendAsync(TestHelpers.AuthRequest(HttpMethod.Put, "/api/settings", await Token, new
        {
            settings = new Dictionary<string, string>
            {
                ["search.search_default_page_size"] = "30",
                ["branding.branding_product_name"] = "Acme Corp",
                ["branding.branding_primary_color"] = "#ff0000"
            }
        }));

        // Verify via GET
        var getResponse = await _client.SendAsync(TestHelpers.AuthRequest(HttpMethod.Get, "/api/settings", await Token));
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
        var response = await _client.SendAsync(TestHelpers.AuthRequest(HttpMethod.Put, "/api/settings", await Token, new
        {
            settings = new Dictionary<string, string>()
        }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateSettings_UnknownKey_ReturnsBadRequest()
    {
        var response = await _client.SendAsync(TestHelpers.AuthRequest(HttpMethod.Put, "/api/settings", await Token, new
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
        var response = await _client.SendAsync(TestHelpers.AuthRequest(HttpMethod.Put, "/api/settings", await Token, new
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
        var response = await _client.SendAsync(TestHelpers.AuthRequest(HttpMethod.Put, "/api/settings", await Token, new
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
        var response = await _client.SendAsync(TestHelpers.AuthRequest(HttpMethod.Put, "/api/settings", await Token, new
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
        var response = await _client.SendAsync(TestHelpers.AuthRequest(HttpMethod.Put, "/api/settings", await Token, new
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

        var response = await _client.SendAsync(TestHelpers.AuthRequest(HttpMethod.Put, "/api/settings", await Token, new
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
        var response = await _client.SendAsync(TestHelpers.AuthRequest(HttpMethod.Put, "/api/settings", await Token, new
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
        var response = await _client.SendAsync(TestHelpers.AuthRequest(HttpMethod.Put, "/api/settings", await Token, new
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
        var response = await _client.SendAsync(TestHelpers.AuthRequest(HttpMethod.Put, "/api/settings", await Token, new
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
    public async Task ResetSetting_UnknownKey_ReturnsNotFound()
    {
        var response = await _client.SendAsync(
            TestHelpers.AuthRequest(HttpMethod.Delete, "/api/settings/unknown.bogus_key", await Token));
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ResetSetting_ExistingOverride_ResetsToDefault()
    {
        await CleanupSettingsAsync();

        // First set an override
        await _client.SendAsync(TestHelpers.AuthRequest(HttpMethod.Put, "/api/settings", await Token, new
        {
            settings = new Dictionary<string, string>
            {
                ["search.search_default_page_size"] = "50"
            }
        }));

        // Verify it was set
        var getResponse = await _client.SendAsync(TestHelpers.AuthRequest(HttpMethod.Get, "/api/settings", await Token));
        var json = await getResponse.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("settings").EnumerateArray()
            .First(s => s.GetProperty("key").GetString() == "search.search_default_page_size")
            .GetProperty("currentValue").GetString().Should().Be("50");

        // Delete the override
        var deleteResponse = await _client.SendAsync(
            TestHelpers.AuthRequest(HttpMethod.Delete, "/api/settings/search.search_default_page_size", await Token));
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify it reverted to default
        var afterDelete = await _client.SendAsync(TestHelpers.AuthRequest(HttpMethod.Get, "/api/settings", await Token));
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
            TestHelpers.AuthRequest(HttpMethod.Delete, "/api/settings/search.search_default_page_size", await Token));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Round-trip ──────────────────────────────────────────────────

    [Fact]
    public async Task Settings_FullRoundTrip_SetUpdateResetVerify()
    {
        await CleanupSettingsAsync();

        // 1. Verify initial defaults
        var r1 = await _client.SendAsync(TestHelpers.AuthRequest(HttpMethod.Get, "/api/settings", await Token));
        var j1 = await r1.Content.ReadFromJsonAsync<JsonElement>();
        j1.GetProperty("settings").EnumerateArray()
            .First(s => s.GetProperty("key").GetString() == "search.search_default_page_size")
            .GetProperty("currentValue").GetString().Should().Be("20");

        // 2. Set override
        await _client.SendAsync(TestHelpers.AuthRequest(HttpMethod.Put, "/api/settings", await Token, new
        {
            settings = new Dictionary<string, string>
            {
                ["search.search_default_page_size"] = "50"
            }
        }));

        // 3. Verify override
        var r2 = await _client.SendAsync(TestHelpers.AuthRequest(HttpMethod.Get, "/api/settings", await Token));
        var j2 = await r2.Content.ReadFromJsonAsync<JsonElement>();
        j2.GetProperty("settings").EnumerateArray()
            .First(s => s.GetProperty("key").GetString() == "search.search_default_page_size")
            .GetProperty("currentValue").GetString().Should().Be("50");

        // 4. Update the override to a different value
        await _client.SendAsync(TestHelpers.AuthRequest(HttpMethod.Put, "/api/settings", await Token, new
        {
            settings = new Dictionary<string, string>
            {
                ["search.search_default_page_size"] = "75"
            }
        }));

        var r3 = await _client.SendAsync(TestHelpers.AuthRequest(HttpMethod.Get, "/api/settings", await Token));
        var j3 = await r3.Content.ReadFromJsonAsync<JsonElement>();
        j3.GetProperty("settings").EnumerateArray()
            .First(s => s.GetProperty("key").GetString() == "search.search_default_page_size")
            .GetProperty("currentValue").GetString().Should().Be("75");

        // 5. Reset to default
        var deleteResp = await _client.SendAsync(
            TestHelpers.AuthRequest(HttpMethod.Delete, "/api/settings/search.search_default_page_size", await Token));
        deleteResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 6. Verify default restored
        var r4 = await _client.SendAsync(TestHelpers.AuthRequest(HttpMethod.Get, "/api/settings", await Token));
        var j4 = await r4.Content.ReadFromJsonAsync<JsonElement>();
        j4.GetProperty("settings").EnumerateArray()
            .First(s => s.GetProperty("key").GetString() == "search.search_default_page_size")
            .GetProperty("currentValue").GetString().Should().Be("20");
    }
}
