using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;

namespace Orkyo.Foundation.Tests.Endpoints;

/// <summary>
/// Integration tests for GET /api/admin/audit (site-admin only).
/// </summary>
[Trait(TestCategories.Name, TestCategories.Database)]
[Collection("Database collection")]
public class AuditEndpointsTests
{
    private readonly HttpClient _client;
    private readonly string _controlPlane;

    public AuditEndpointsTests(DatabaseFixture fixture)
    {
        _client = fixture.Factory.CreateClient();
        _controlPlane = fixture.ControlPlaneConnectionString;
    }

    private async Task<Guid> SeedEventAsync(Guid actorId, string action, DateTime createdAtUtc)
    {
        await using var conn = new NpgsqlConnection(_controlPlane);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO audit_events (actor_user_id, action, created_at) VALUES (@actor, @action, @at) RETURNING id", conn);
        cmd.Parameters.AddWithValue("actor", actorId);
        cmd.Parameters.AddWithValue("action", action);
        cmd.Parameters.AddWithValue("at", createdAtUtc);
        return (Guid)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task<List<JsonElement>> QueryEventsAsync(string query, string token)
    {
        var response = await _client.SendAsync(
            TestHelpers.AuthRequest(HttpMethod.Get, $"/api/admin/audit?{query}", token));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("events").EnumerateArray().ToList();
    }

    // ── Auth guards ───────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAuditEvents_RegularUser_Returns403()
    {
        var token = (await DatabaseTestUtils.CreateLinkedUserAsync("audit-user")).Token;
        var response = await _client.SendAsync(TestHelpers.AuthRequest(HttpMethod.Get, "/api/admin/audit", token));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── Happy path ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAuditEvents_SiteAdmin_Returns200WithPagedResponse()
    {
        var token = (await DatabaseTestUtils.CreateLinkedUserAsync("audit-admin", siteAdmin: true)).Token;
        var response = await _client.SendAsync(TestHelpers.AuthRequest(HttpMethod.Get, "/api/admin/audit", token));

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
        var token = (await DatabaseTestUtils.CreateLinkedUserAsync("audit-admin", siteAdmin: true)).Token;
        var response = await _client.SendAsync(
            TestHelpers.AuthRequest(HttpMethod.Get, "/api/admin/audit?page=1&pageSize=10", token));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, body.GetProperty("page").GetInt32());
        Assert.Equal(10, body.GetProperty("pageSize").GetInt32());
    }

    [Fact]
    public async Task GetAuditEvents_PageSizeCappedAt100()
    {
        var token = (await DatabaseTestUtils.CreateLinkedUserAsync("audit-admin", siteAdmin: true)).Token;
        var response = await _client.SendAsync(
            TestHelpers.AuthRequest(HttpMethod.Get, "/api/admin/audit?pageSize=999", token));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(100, body.GetProperty("pageSize").GetInt32());
    }

    // ── Filters ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAuditEvents_WithActionFilter_ReturnsOnlyThatAction()
    {
        var (adminId, token) = await DatabaseTestUtils.CreateLinkedUserAsync("audit-admin", siteAdmin: true);
        var action = $"test.filter.{Guid.NewGuid():N}";
        var id = await SeedEventAsync(adminId, action, DateTime.UtcNow);

        var events = await QueryEventsAsync($"action={action}", token);

        Assert.Contains(events, e => e.GetProperty("id").GetGuid() == id);
        Assert.All(events, e => Assert.Equal(action, e.GetProperty("action").GetString()));
    }

    [Fact]
    public async Task GetAuditEvents_WithActorIdFilter_ReturnsOnlyThatActorsEvents()
    {
        var (adminId, token) = await DatabaseTestUtils.CreateLinkedUserAsync("audit-admin", siteAdmin: true);
        var id = await SeedEventAsync(adminId, "test.actor", DateTime.UtcNow);

        var events = await QueryEventsAsync($"actorId={adminId}", token);

        Assert.Contains(events, e => e.GetProperty("id").GetGuid() == id);
        Assert.All(events, e => Assert.Equal(adminId, e.GetProperty("actorUserId").GetGuid()));
    }

    [Fact]
    public async Task GetAuditEvents_WithDateRangeFilter_ReturnsOnlyEventsInTheRange()
    {
        var (adminId, token) = await DatabaseTestUtils.CreateLinkedUserAsync("audit-admin", siteAdmin: true);
        var from = new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = from.AddDays(1);
        var id = await SeedEventAsync(adminId, "test.range", from.AddHours(12));

        var events = await QueryEventsAsync($"from={from:O}&to={to:O}", token);

        Assert.Contains(events, e => e.GetProperty("id").GetGuid() == id);
        Assert.All(events, e => Assert.InRange(e.GetProperty("createdAt").GetDateTime().ToUniversalTime(), from, to));
    }
}
