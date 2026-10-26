using System.Net;
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
[Trait(TestCategories.Name, TestCategories.Database)]
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
    public async Task GetVersion_NoAuth_Returns200WithVersionAndBuild()
    {
        var response = await _client.GetAsync("/api/version");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("version").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("build").GetString()));
    }

    // ── GET /api/admin/diagnostics ──────────────────────────────

    [Fact]
    public async Task GetDiagnostics_NonSiteAdmin_Returns403()
    {
        var token = (await DatabaseTestUtils.CreateLinkedUserAsync("diag-regular")).Token;
        var request = TestHelpers.AuthRequest(HttpMethod.Get, "/api/admin/diagnostics", token);
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetDiagnostics_SiteAdmin_ReportsEverySection()
    {
        var (_, token) = await DatabaseTestUtils.CreateLinkedUserAsync("diag-admin", siteAdmin: true);
        var request = TestHelpers.AuthRequest(HttpMethod.Get, "/api/admin/diagnostics", token);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("version").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("build").GetString()));
        // Foundation's host sets no deployment mode; a product does.
        Assert.Equal("unknown", body.GetProperty("deploymentMode").GetString());

        var db = body.GetProperty("database");
        Assert.Equal("healthy", db.GetProperty("status").GetString());
        Assert.True(db.GetProperty("migrationsApplied").GetInt32() > 0);
        Assert.True(db.GetProperty("tenantCount").GetInt32() >= 0);

        // SMTP_HOST is "localhost" in the test configuration; MaskHost keeps a host of two parts or fewer as it is.
        var smtp = body.GetProperty("smtp");
        Assert.Equal("configured", smtp.GetProperty("status").GetString());
        Assert.Equal("localhost", smtp.GetProperty("host").GetString());

        var auth = body.GetProperty("auth");
        Assert.Equal("keycloak", auth.GetProperty("provider").GetString());
        Assert.True(auth.TryGetProperty("status", out _));
        Assert.True(auth.TryGetProperty("realm", out _));

        Assert.Contains(body.GetProperty("worker").GetProperty("status").GetString(), new[] { "running", "idle", "unknown" });

        var modules = body.GetProperty("modules");
        Assert.True(modules.GetProperty("observability").ValueKind is JsonValueKind.True or JsonValueKind.False);
        Assert.True(modules.GetProperty("logAggregation").ValueKind is JsonValueKind.True or JsonValueKind.False);
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
                "INSERT INTO worker_job_runs (job_name, started_at, completed_at, last_succeeded_at, result) " +
                "VALUES (@job, NOW(), NOW() + INTERVAL '1 hour', NOW() + INTERVAL '1 hour', 'succeeded')", conn);
            insert.Parameters.AddWithValue("job", job);
            await insert.ExecuteNonQueryAsync();
        }

        try
        {
            var (_, token) = await DatabaseTestUtils.CreateLinkedUserAsync("diag-admin", siteAdmin: true);
            var request = TestHelpers.AuthRequest(HttpMethod.Get, "/api/admin/diagnostics", token);

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
}
