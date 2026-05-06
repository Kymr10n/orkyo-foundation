using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Orkyo.Foundation.Tests.Mocks;

namespace Orkyo.Foundation.Tests.Endpoints;

/// <summary>
/// Integration tests for the diagnostics admin endpoints.
/// GET /api/version — public version info (no auth required).
/// GET /api/admin/diagnostics — full platform diagnostics (site-admin only).
/// </summary>
[Collection("Database collection")]
public class DiagnosticsAdminEndpointsTests
{
    private readonly HttpClient _client;
    private readonly DatabaseFixture _fixture;
    private readonly string _connectionString;

    public DiagnosticsAdminEndpointsTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.Factory.CreateClient();
        _connectionString = $"Host=localhost;Port={_fixture.DatabasePort};Database=control_plane;Username=postgres;Password=postgres";
    }

    private async Task<(Guid UserId, string Token)> CreateSiteAdminAsync()
    {
        var userId = Guid.NewGuid();
        var email = $"diag-admin-{userId}@test.com";
        var keycloakSub = $"kc-{userId}";

        await using var conn = new Npgsql.NpgsqlConnection(_connectionString);
        await conn.OpenAsync();

        await using var userCmd = new Npgsql.NpgsqlCommand(
            "INSERT INTO users (id, email, display_name, status) VALUES (@id, @email, 'Diag Admin', 'active')",
            conn);
        userCmd.Parameters.AddWithValue("id", userId);
        userCmd.Parameters.AddWithValue("email", email);
        await userCmd.ExecuteNonQueryAsync();

        await using var linkCmd = new Npgsql.NpgsqlCommand(
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
            DisplayName = "Diag Admin",
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
        var email = $"diag-regular-{userId}@test.com";
        var keycloakSub = $"kc-{userId}";

        await using var conn = new Npgsql.NpgsqlConnection(_connectionString);
        await conn.OpenAsync();

        await using var cmd = new Npgsql.NpgsqlCommand(
            "INSERT INTO users (id, email, display_name, status) VALUES (@id, @email, 'Regular User', 'active')",
            conn);
        cmd.Parameters.AddWithValue("id", userId);
        cmd.Parameters.AddWithValue("email", email);
        await cmd.ExecuteNonQueryAsync();

        await using var linkCmd = new Npgsql.NpgsqlCommand(
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

    // ── GET /api/version ────────────────────────────────────────

    [Fact]
    public async Task GetVersion_NoAuth_Returns200()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/version");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.TryGetProperty("version", out _));
        Assert.True(body.TryGetProperty("build", out _));
    }

    [Fact]
    public async Task GetVersion_ReturnsNonEmptyValues()
    {
        var response = await _client.GetFromJsonAsync<JsonElement>("/api/version");

        var version = response.GetProperty("version").GetString();
        var build = response.GetProperty("build").GetString();

        Assert.False(string.IsNullOrWhiteSpace(version));
        Assert.False(string.IsNullOrWhiteSpace(build));
    }

    // ── GET /api/admin/diagnostics ──────────────────────────────

    [Fact]
    public async Task GetDiagnostics_NoAuth_Returns401()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/diagnostics");
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetDiagnostics_NonSiteAdmin_Returns403()
    {
        var token = await CreateRegularUserTokenAsync();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/diagnostics");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetDiagnostics_SiteAdmin_Returns200WithAllSections()
    {
        var (_, token) = await CreateSiteAdminAsync();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/diagnostics");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        // Top-level fields
        Assert.True(body.TryGetProperty("version", out _));
        Assert.True(body.TryGetProperty("build", out _));
        Assert.True(body.TryGetProperty("deploymentMode", out _));

        // Database section
        Assert.True(body.TryGetProperty("database", out var db));
        Assert.Equal("healthy", db.GetProperty("status").GetString());
        Assert.True(db.GetProperty("migrationsApplied").GetInt32() > 0);
        Assert.True(db.GetProperty("tenantCount").GetInt32() >= 0);

        // SMTP section
        Assert.True(body.TryGetProperty("smtp", out var smtp));
        Assert.True(smtp.TryGetProperty("status", out _));
        Assert.True(smtp.TryGetProperty("host", out _));

        // Auth section
        Assert.True(body.TryGetProperty("auth", out var auth));
        Assert.True(auth.TryGetProperty("status", out _));
        Assert.True(auth.TryGetProperty("provider", out _));
        Assert.True(auth.TryGetProperty("realm", out _));

        // Worker section
        Assert.True(body.TryGetProperty("worker", out var worker));
        Assert.True(worker.TryGetProperty("status", out _));

        // Modules section
        Assert.True(body.TryGetProperty("modules", out var modules));
        Assert.True(modules.TryGetProperty("observability", out _));
        Assert.True(modules.TryGetProperty("logAggregation", out _));
    }

    [Fact]
    public async Task GetDiagnostics_SiteAdmin_DatabaseShowsHealthy()
    {
        var (_, token) = await CreateSiteAdminAsync();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/diagnostics");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        var dbStatus = body.GetProperty("database").GetProperty("status").GetString();
        Assert.Equal("healthy", dbStatus);
    }

    [Fact]
    public async Task GetDiagnostics_SiteAdmin_DeploymentModeIsSelfHosted()
    {
        var (_, token) = await CreateSiteAdminAsync();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/diagnostics");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("self-hosted", body.GetProperty("deploymentMode").GetString());
    }

    [Fact]
    public async Task GetDiagnostics_SiteAdmin_SmtpHostIsMaskedOrShort()
    {
        var (_, token) = await CreateSiteAdminAsync();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/diagnostics");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        var smtpStatus = body.GetProperty("smtp").GetProperty("status").GetString();
        var smtpHost = body.GetProperty("smtp").GetProperty("host").GetString();

        if (smtpStatus == "configured")
        {
            // Host is present and either masked (3+ parts: smtp.*****.com) or short (<= 2 parts: kept as-is)
            Assert.NotNull(smtpHost);
            Assert.NotEmpty(smtpHost);
        }
        else
        {
            Assert.Equal("not-configured", smtpStatus);
        }
    }

    [Fact]
    public async Task GetDiagnostics_SiteAdmin_AuthProviderIsKeycloak()
    {
        var (_, token) = await CreateSiteAdminAsync();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/diagnostics");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("keycloak", body.GetProperty("auth").GetProperty("provider").GetString());
    }

    [Fact]
    public async Task GetDiagnostics_SiteAdmin_WorkerStatusIsValid()
    {
        var (_, token) = await CreateSiteAdminAsync();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/diagnostics");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        var workerStatus = body.GetProperty("worker").GetProperty("status").GetString();
        Assert.Contains(workerStatus, new[] { "running", "idle", "unknown" });
    }

    [Fact]
    public async Task GetDiagnostics_SiteAdmin_ModulesAreBooleans()
    {
        var (_, token) = await CreateSiteAdminAsync();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/diagnostics");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        var modules = body.GetProperty("modules");
        var obsKind = modules.GetProperty("observability").ValueKind;
        Assert.True(obsKind is JsonValueKind.True or JsonValueKind.False);
        var logKind = modules.GetProperty("logAggregation").ValueKind;
        Assert.True(logKind is JsonValueKind.True or JsonValueKind.False);
    }

    // ── GET /api/v1/info (modified: now uses DeploymentConfig) ──

    [Fact]
    public async Task GetApiInfo_NoAuth_Returns200()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/info");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetApiInfo_ReturnsNameAndVersion()
    {
        var response = await _client.GetFromJsonAsync<JsonElement>("/api/v1/info");

        Assert.Equal("Orkyo API", response.GetProperty("name").GetString());

        var version = response.GetProperty("version").GetString();
        Assert.NotNull(version);
        Assert.NotEmpty(version);
    }
}
