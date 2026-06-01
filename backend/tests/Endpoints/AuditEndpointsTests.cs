using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Orkyo.Foundation.Tests.Endpoints;

/// <summary>
/// Integration tests for GET /api/admin/audit (site-admin only).
/// </summary>
[Collection("Database collection")]
public class AuditEndpointsTests
{
    private readonly HttpClient _client;
    private readonly DatabaseFixture _fixture;
    private readonly string _connString;

    public AuditEndpointsTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.Factory.CreateClient();
        _connString = $"Host=localhost;Port={fixture.DatabasePort};Database=control_plane;Username=postgres;Password=postgres";
    }

    private HttpRequestMessage Auth(HttpMethod method, string url, string token)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return req;
    }

    private async Task<string> CreateSiteAdminTokenAsync()
    {
        var id = Guid.NewGuid();
        var email = $"audit-admin-{id}@test.com";
        var sub = $"kc-audit-{id}";

        await using var conn = new Npgsql.NpgsqlConnection(_connString);
        await conn.OpenAsync();

        await using var cmd = new Npgsql.NpgsqlCommand(
            "INSERT INTO users (id, email, display_name, status) VALUES (@id, @email, 'Audit Admin', 'active')", conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("email", email);
        await cmd.ExecuteNonQueryAsync();

        await using var linkCmd = new Npgsql.NpgsqlCommand(
            "INSERT INTO user_identities (id, user_id, provider, provider_subject, provider_email) VALUES (@id, @uid, 'keycloak', @sub, @email)", conn);
        linkCmd.Parameters.AddWithValue("id", Guid.NewGuid());
        linkCmd.Parameters.AddWithValue("uid", id);
        linkCmd.Parameters.AddWithValue("sub", sub);
        linkCmd.Parameters.AddWithValue("email", email);
        await linkCmd.ExecuteNonQueryAsync();

        var data = new
        {
            UserId = id.ToString(),
            Email = email,
            DisplayName = "Audit Admin",
            TenantId = Guid.Empty.ToString(),
            TenantSlug = "",
            IsTenantAdmin = false,
            Role = "viewer",
            Sub = sub,
            RealmRoles = new[] { "user", "site-admin" },
        };
        return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(data)));
    }

    private async Task<string> CreateRegularUserTokenAsync()
    {
        var id = Guid.NewGuid();
        var email = $"audit-user-{id}@test.com";
        await using var conn = new Npgsql.NpgsqlConnection(_connString);
        await conn.OpenAsync();
        await using var cmd = new Npgsql.NpgsqlCommand(
            "INSERT INTO users (id, email, status) VALUES (@id, @email, 'active')", conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("email", email);
        await cmd.ExecuteNonQueryAsync();
        var data = new
        {
            UserId = id.ToString(),
            Email = email,
            DisplayName = "User",
            TenantId = Guid.Empty.ToString(),
            TenantSlug = "",
            IsTenantAdmin = false,
            Role = "viewer",
            Sub = $"kc-{id}",
            RealmRoles = new[] { "user" },
        };
        return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(data)));
    }

    // ── Auth guards ───────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAuditEvents_Unauthenticated_Returns401()
    {
        var response = await _client.GetAsync("/api/admin/audit");
        Assert.True(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task GetAuditEvents_RegularUser_Returns403()
    {
        var token = await CreateRegularUserTokenAsync();
        var response = await _client.SendAsync(Auth(HttpMethod.Get, "/api/admin/audit", token));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── Happy path ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAuditEvents_SiteAdmin_Returns200WithPagedResponse()
    {
        var token = await CreateSiteAdminTokenAsync();
        var response = await _client.SendAsync(Auth(HttpMethod.Get, "/api/admin/audit", token));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.TryGetProperty("events", out var events));
        Assert.Equal(JsonValueKind.Array, events.ValueKind);
        Assert.True(body.TryGetProperty("page", out _));
        Assert.True(body.TryGetProperty("pageSize", out _));
        Assert.True(body.TryGetProperty("totalCount", out _));
        Assert.True(body.TryGetProperty("totalPages", out _));
    }

    [Fact]
    public async Task GetAuditEvents_WithPaginationParams_Honoured()
    {
        var token = await CreateSiteAdminTokenAsync();
        var response = await _client.SendAsync(
            Auth(HttpMethod.Get, "/api/admin/audit?page=1&pageSize=10", token));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, body.GetProperty("page").GetInt32());
        Assert.Equal(10, body.GetProperty("pageSize").GetInt32());
    }

    [Fact]
    public async Task GetAuditEvents_PageSizeCappedAt200()
    {
        var token = await CreateSiteAdminTokenAsync();
        var response = await _client.SendAsync(
            Auth(HttpMethod.Get, "/api/admin/audit?pageSize=999", token));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(200, body.GetProperty("pageSize").GetInt32());
    }

    [Fact]
    public async Task GetAuditEvents_WithActionFilter_Returns200()
    {
        var token = await CreateSiteAdminTokenAsync();
        var response = await _client.SendAsync(
            Auth(HttpMethod.Get, "/api/admin/audit?action=user.login", token));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetAuditEvents_WithActorIdFilter_Returns200()
    {
        var token = await CreateSiteAdminTokenAsync();
        var actorId = Guid.NewGuid();
        var response = await _client.SendAsync(
            Auth(HttpMethod.Get, $"/api/admin/audit?actorId={actorId}", token));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetAuditEvents_WithDateRangeFilter_Returns200()
    {
        var token = await CreateSiteAdminTokenAsync();
        var from = Uri.EscapeDataString(DateTime.UtcNow.AddDays(-7).ToString("o"));
        var to = Uri.EscapeDataString(DateTime.UtcNow.ToString("o"));
        var response = await _client.SendAsync(
            Auth(HttpMethod.Get, $"/api/admin/audit?from={from}&to={to}", token));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
