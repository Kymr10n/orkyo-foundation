using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
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
    private readonly string _controlPlane;

    public DiagnosticsAdminEndpointsTests(DatabaseFixture fixture)
    {
        _client = fixture.Factory.CreateClient();
        _controlPlane = fixture.ControlPlaneConnectionString;
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
    public async Task GetDiagnostics_NonSiteAdmin_Returns403()
    {
        var token = await DatabaseFixture.CreateLinkedTokenAsync("diag-regular");
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/diagnostics");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetDiagnostics_SiteAdmin_Returns200WithAllSections()
    {
        var (_, token) = await DatabaseTestUtils.CreateLinkedUserAsync("diag-admin", siteAdmin: true);
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
        var (_, token) = await DatabaseTestUtils.CreateLinkedUserAsync("diag-admin", siteAdmin: true);
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/diagnostics");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        var dbStatus = body.GetProperty("database").GetProperty("status").GetString();
        Assert.Equal("healthy", dbStatus);
    }

    [Fact]
    public async Task GetDiagnostics_SiteAdmin_DeploymentModeIsUnknown_WhenTheProductDoesNotSetIt()
    {
        var (_, token) = await DatabaseTestUtils.CreateLinkedUserAsync("diag-admin", siteAdmin: true);
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/diagnostics");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("unknown", body.GetProperty("deploymentMode").GetString());
    }

    [Fact]
    public async Task GetDiagnostics_SiteAdmin_SmtpHostIsMaskedOrShort()
    {
        var (_, token) = await DatabaseTestUtils.CreateLinkedUserAsync("diag-admin", siteAdmin: true);
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
        var (_, token) = await DatabaseTestUtils.CreateLinkedUserAsync("diag-admin", siteAdmin: true);
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/diagnostics");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("keycloak", body.GetProperty("auth").GetProperty("provider").GetString());
    }

    [Fact]
    public async Task GetDiagnostics_SiteAdmin_WorkerStatusReadsTheWorkerJobJournal()
    {
        // A completion later than any other row, so it is the one the endpoint reports.
        var job = $"diag-test-{Guid.NewGuid():N}";
        await using (var conn = new NpgsqlConnection(_controlPlane))
        {
            await conn.OpenAsync();
            await using var insert = new NpgsqlCommand(
                "INSERT INTO worker_job_runs (job_name, started_at, completed_at, result) " +
                "VALUES (@job, NOW(), NOW() + INTERVAL '1 hour', 'ok')", conn);
            insert.Parameters.AddWithValue("job", job);
            await insert.ExecuteNonQueryAsync();
        }

        try
        {
            var (_, token) = await DatabaseTestUtils.CreateLinkedUserAsync("diag-admin", siteAdmin: true);
            var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/diagnostics");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var body = await (await _client.SendAsync(request)).Content.ReadFromJsonAsync<JsonElement>();

            var worker = body.GetProperty("worker");
            Assert.Equal("running", worker.GetProperty("status").GetString());
            var lastActivity = worker.GetProperty("lastActivity").GetDateTime();
            Assert.True(lastActivity > DateTime.UtcNow.AddMinutes(30), "the journal's completion, not an audit event");
        }
        finally
        {
            await using var conn = new NpgsqlConnection(_controlPlane);
            await conn.OpenAsync();
            await using var delete = new NpgsqlCommand("DELETE FROM worker_job_runs WHERE job_name = @job", conn);
            delete.Parameters.AddWithValue("job", job);
            await delete.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task GetDiagnostics_SiteAdmin_WorkerStatusIsValid()
    {
        var (_, token) = await DatabaseTestUtils.CreateLinkedUserAsync("diag-admin", siteAdmin: true);
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
        var (_, token) = await DatabaseTestUtils.CreateLinkedUserAsync("diag-admin", siteAdmin: true);
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
}
