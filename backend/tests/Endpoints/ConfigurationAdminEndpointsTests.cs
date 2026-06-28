using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Services;
using Npgsql;

namespace Orkyo.Foundation.Tests.Endpoints;

/// <summary>
/// Integration tests for the site-admin configuration endpoints (GET/PUT/DELETE
/// /api/admin/configuration) — the control-plane surface for the descriptor-based settings, gated by
/// RequireSiteAdmin and sharing handlers with the tenant /api/settings surface.
///
/// These tests focus on the AUTHORIZATION this endpoint group adds (site-admin required for all
/// methods). The site-vs-tenant scope selection is the responsibility of TenantSettingsService
/// (IsSiteContext) and is covered by TenantSettingsScopePolicyTests / SettingsEndpointsTests; the
/// shared test factory exercises endpoints in its default tenant context, so a tenant-scoped key is
/// used for the write round-trip.
/// </summary>
[Collection("Database collection")]
public class ConfigurationAdminEndpointsTests
{
    private readonly HttpClient _client;
    private readonly DatabaseFixture _fixture;
    private readonly string _controlPlaneConn;

    // A tenant-scoped key (int, range 5–100), valid in the factory's default tenant context.
    private const string Key = "search.search_default_page_size";

    public ConfigurationAdminEndpointsTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.Factory.CreateClient();
        _controlPlaneConn = $"Host=localhost;Port={_fixture.DatabasePort};Database=control_plane;Username=postgres;Password=postgres";
    }

    private async Task<string> CreateSiteAdminTokenAsync()
        => await CreateUserTokenAsync("config-admin", new[] { "user", "site-admin" });

    private async Task<string> CreateRegularUserTokenAsync()
        => await CreateUserTokenAsync("config-regular", new[] { "user" });

    private async Task<string> CreateUserTokenAsync(string prefix, string[] realmRoles)
    {
        var userId = Guid.NewGuid();
        var email = $"{prefix}-{userId}@test.com";
        var keycloakSub = $"kc-{userId}";

        await using var conn = new NpgsqlConnection(_controlPlaneConn);
        await conn.OpenAsync();
        await using var userCmd = new NpgsqlCommand(
            "INSERT INTO users (id, email, display_name, status) VALUES (@id, @email, 'Config User', 'active')", conn);
        userCmd.Parameters.AddWithValue("id", userId);
        userCmd.Parameters.AddWithValue("email", email);
        await userCmd.ExecuteNonQueryAsync();
        await using var linkCmd = new NpgsqlCommand(
            "INSERT INTO user_identities (id, user_id, provider, provider_subject, provider_email) VALUES (@id, @userId, 'keycloak', @sub, @email)", conn);
        linkCmd.Parameters.AddWithValue("id", Guid.NewGuid());
        linkCmd.Parameters.AddWithValue("userId", userId);
        linkCmd.Parameters.AddWithValue("sub", keycloakSub);
        linkCmd.Parameters.AddWithValue("email", email);
        await linkCmd.ExecuteNonQueryAsync();

        var tokenData = new
        {
            UserId = userId.ToString(),
            Email = email,
            DisplayName = "Config User",
            TenantId = Guid.Empty.ToString(),
            TenantSlug = "",
            IsTenantAdmin = false,
            Role = "viewer",
            Sub = keycloakSub,
            RealmRoles = realmRoles,
        };
        return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(tokenData)));
    }

    private async Task ResetOverridesAsync()
    {
        var tenantConn = $"Host=localhost;Port={_fixture.DatabasePort};Database={TestConstants.TenantDatabase};Username=postgres;Password=postgres";
        await using var conn = new NpgsqlConnection(tenantConn);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("DELETE FROM tenant_settings", conn);
        await cmd.ExecuteNonQueryAsync();
        TenantSettingsService.ClearCache();
    }

    private HttpRequestMessage Authed(HttpMethod method, string url, string token, object? body = null)
    {
        var msg = new HttpRequestMessage(method, url);
        msg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body != null) msg.Content = JsonContent.Create(body);
        return msg;
    }

    private static JsonElement FindSetting(JsonElement body, string key) =>
        body.GetProperty("settings").EnumerateArray().First(s => s.GetProperty("key").GetString() == key);

    // ── GET — RequireSiteAdmin ───────────────────────────────────────

    [Fact]
    public async Task GetConfiguration_NoAuth_Returns401()
    {
        var response = await _client.GetAsync("/api/admin/configuration");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetConfiguration_NonSiteAdmin_Returns403()
    {
        var token = await CreateRegularUserTokenAsync();
        var response = await _client.SendAsync(Authed(HttpMethod.Get, "/api/admin/configuration", token));
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetConfiguration_SiteAdmin_ReturnsSettings()
    {
        var token = await CreateSiteAdminTokenAsync();
        var response = await _client.SendAsync(Authed(HttpMethod.Get, "/api/admin/configuration", token));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("settings").GetArrayLength().Should().BeGreaterThan(0);
    }

    // ── PUT — RequireSiteAdmin ───────────────────────────────────────

    [Fact]
    public async Task UpdateConfiguration_NonSiteAdmin_Returns403()
    {
        var token = await CreateRegularUserTokenAsync();
        var body = new { settings = new Dictionary<string, string> { [Key] = "30" } };
        var response = await _client.SendAsync(Authed(HttpMethod.Put, "/api/admin/configuration", token, body));
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task UpdateConfiguration_SiteAdmin_PersistsAndReflects()
    {
        await ResetOverridesAsync();
        var token = await CreateSiteAdminTokenAsync();

        var put = await _client.SendAsync(Authed(HttpMethod.Put, "/api/admin/configuration", token,
            new { settings = new Dictionary<string, string> { [Key] = "30" } }));
        put.StatusCode.Should().Be(HttpStatusCode.OK);
        FindSetting(await put.Content.ReadFromJsonAsync<JsonElement>(), Key)
            .GetProperty("currentValue").GetString().Should().Be("30");

        var get = await _client.SendAsync(Authed(HttpMethod.Get, "/api/admin/configuration", token));
        FindSetting(await get.Content.ReadFromJsonAsync<JsonElement>(), Key)
            .GetProperty("currentValue").GetString().Should().Be("30");
    }

    // ── DELETE — RequireSiteAdmin ────────────────────────────────────

    [Fact]
    public async Task ResetConfiguration_NonSiteAdmin_Returns403()
    {
        var token = await CreateRegularUserTokenAsync();
        var response = await _client.SendAsync(Authed(HttpMethod.Delete, $"/api/admin/configuration/{Key}", token));
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ResetConfiguration_SiteAdmin_RemovesOverride()
    {
        await ResetOverridesAsync();
        var token = await CreateSiteAdminTokenAsync();

        (await _client.SendAsync(Authed(HttpMethod.Put, "/api/admin/configuration", token,
            new { settings = new Dictionary<string, string> { [Key] = "30" } }))).EnsureSuccessStatusCode();

        var reset = await _client.SendAsync(Authed(HttpMethod.Delete, $"/api/admin/configuration/{Key}", token));
        reset.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
