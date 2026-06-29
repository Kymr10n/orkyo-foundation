using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using Xunit;

namespace Orkyo.Foundation.Tests.Endpoints;

/// <summary>
/// Integration tests for the site-admin feedback triage endpoints
/// (GET/PATCH /api/admin/feedback) against the real control-plane DB.
/// Feedback is created through the public submit endpoint (which now writes to control-plane).
/// </summary>
[Collection("Database collection")]
public class FeedbackAdminEndpointsTests : IAsyncLifetime
{
    private readonly DatabaseFixture _fixture;
    private readonly HttpClient _client;          // token-driven (site-admin / regular / none)
    private readonly HttpClient _authoredClient;  // tenant member, for submitting feedback
    private readonly string _conn;

    public FeedbackAdminEndpointsTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.Factory.CreateClient();
        _authoredClient = fixture.CreateAuthorizedClient();
        _conn = $"Host=localhost;Port={fixture.DatabasePort};Database=control_plane;Username=postgres;Password=postgres";
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await using var conn = new NpgsqlConnection(_conn);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "DELETE FROM audit_events WHERE action = 'feedback.status_changed' " +
            "AND target_id IN (SELECT id::text FROM feedback WHERE title LIKE 'FBADMIN-%'); " +
            "DELETE FROM feedback WHERE title LIKE 'FBADMIN-%'", conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<Guid> SubmitAsync(string type = "bug")
    {
        var title = $"FBADMIN-{Guid.NewGuid():N}"[..28];
        var resp = await _authoredClient.PostAsJsonAsync("/api/feedback",
            new { feedbackType = type, title, description = "details here", pageUrl = "/dashboard" });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("id").GetGuid();
    }

    private async Task<string> CreateUserTokenAsync(string[] realmRoles)
    {
        var userId = Guid.NewGuid();
        var email = $"fbadmin-{userId}@test.com";
        var sub = $"kc-{userId}";
        await using var conn = new NpgsqlConnection(_conn);
        await conn.OpenAsync();
        await using (var cmd = new NpgsqlCommand(
            "INSERT INTO users (id, email, display_name, status) VALUES (@id, @email, 'Admin', 'active')", conn))
        {
            cmd.Parameters.AddWithValue("id", userId);
            cmd.Parameters.AddWithValue("email", email);
            await cmd.ExecuteNonQueryAsync();
        }
        await using (var link = new NpgsqlCommand(
            "INSERT INTO user_identities (id, user_id, provider, provider_subject, provider_email) " +
            "VALUES (@id, @userId, 'keycloak', @sub, @email)", conn))
        {
            link.Parameters.AddWithValue("id", Guid.NewGuid());
            link.Parameters.AddWithValue("userId", userId);
            link.Parameters.AddWithValue("sub", sub);
            link.Parameters.AddWithValue("email", email);
            await link.ExecuteNonQueryAsync();
        }
        var tokenData = new
        {
            UserId = userId.ToString(),
            Email = email,
            DisplayName = "Admin",
            TenantId = Guid.Empty.ToString(),
            TenantSlug = "",
            IsTenantAdmin = false,
            Role = "viewer",
            Sub = sub,
            RealmRoles = realmRoles,
        };
        return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(tokenData)));
    }

    private static HttpRequestMessage Req(HttpMethod method, string url, string token, object? body = null)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) req.Content = JsonContent.Create(body);
        return req;
    }

    private async Task<string> StatusOfAsync(Guid id)
    {
        await using var conn = new NpgsqlConnection(_conn);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT status FROM feedback WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", id);
        return (string)(await cmd.ExecuteScalarAsync())!;
    }

    // ── Authorization ─────────────────────────────────────────────────────────

    [Fact]
    public async Task List_Unauthenticated_Returns401()
    {
        var response = await _client.GetAsync("/api/admin/feedback");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task List_RegularUser_Returns403()
    {
        var token = await CreateUserTokenAsync(["user"]);
        var response = await _client.SendAsync(Req(HttpMethod.Get, "/api/admin/feedback", token));
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task List_SiteAdmin_Returns200WithItems()
    {
        await SubmitAsync();
        var token = await CreateUserTokenAsync(["user", "site-admin"]);
        var response = await _client.SendAsync(Req(HttpMethod.Get, "/api/admin/feedback", token));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("total").GetInt32().Should().BeGreaterThan(0);
        doc.RootElement.GetProperty("items").ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task List_InvalidStatusFilter_Returns400()
    {
        var token = await CreateUserTokenAsync(["user", "site-admin"]);
        var response = await _client.SendAsync(Req(HttpMethod.Get, "/api/admin/feedback?status=archived", token));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── Detail + patch ────────────────────────────────────────────────────────

    [Fact]
    public async Task Detail_SiteAdmin_ReturnsItemWithSubmitterAndTenant()
    {
        var id = await SubmitAsync();
        var token = await CreateUserTokenAsync(["user", "site-admin"]);
        var response = await _client.SendAsync(Req(HttpMethod.Get, $"/api/admin/feedback/{id}", token));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var root = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        root.GetProperty("status").GetString().Should().Be("new");
        root.GetProperty("submitterEmail").GetString().Should().NotBeNullOrEmpty();
        root.GetProperty("tenantName").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Detail_UnknownId_Returns404()
    {
        var token = await CreateUserTokenAsync(["user", "site-admin"]);
        var response = await _client.SendAsync(Req(HttpMethod.Get, $"/api/admin/feedback/{Guid.NewGuid()}", token));
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Patch_StatusChange_UpdatesAndWritesAudit()
    {
        var id = await SubmitAsync();
        var token = await CreateUserTokenAsync(["user", "site-admin"]);

        var response = await _client.SendAsync(Req(HttpMethod.Patch, $"/api/admin/feedback/{id}", token,
            new { status = "reviewed", adminNotes = "Looking into it.", githubIssueUrl = "https://github.com/x/y/issues/1" }));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await StatusOfAsync(id)).Should().Be("reviewed");

        await using var conn = new NpgsqlConnection(_conn);
        await conn.OpenAsync();
        await using (var rc = new NpgsqlCommand("SELECT admin_notes, github_issue_url FROM feedback WHERE id = @id", conn))
        {
            rc.Parameters.AddWithValue("id", id);
            await using var r = await rc.ExecuteReaderAsync();
            (await r.ReadAsync()).Should().BeTrue();
            r.GetString(0).Should().Be("Looking into it.");
            r.GetString(1).Should().Be("https://github.com/x/y/issues/1");
        }
        await using var ac = new NpgsqlCommand(
            "SELECT COUNT(*) FROM audit_events WHERE action = 'feedback.status_changed' AND target_id = @id", conn);
        ac.Parameters.AddWithValue("id", id.ToString());
        Convert.ToInt32(await ac.ExecuteScalarAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Patch_NoFields_Returns400()
    {
        var id = await SubmitAsync();
        var token = await CreateUserTokenAsync(["user", "site-admin"]);
        var response = await _client.SendAsync(Req(HttpMethod.Patch, $"/api/admin/feedback/{id}", token, new { }));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Patch_InvalidStatus_Returns400()
    {
        var id = await SubmitAsync();
        var token = await CreateUserTokenAsync(["user", "site-admin"]);
        var response = await _client.SendAsync(Req(HttpMethod.Patch, $"/api/admin/feedback/{id}", token, new { status = "archived" }));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Patch_UnknownId_Returns404()
    {
        var token = await CreateUserTokenAsync(["user", "site-admin"]);
        var response = await _client.SendAsync(Req(HttpMethod.Patch, $"/api/admin/feedback/{Guid.NewGuid()}", token, new { status = "reviewed" }));
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
