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

    public AuditEndpointsTests(DatabaseFixture fixture)
    {
        _client = fixture.Factory.CreateClient();
    }

    private HttpRequestMessage Auth(HttpMethod method, string url, string token)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return req;
    }

    private static async Task<string> CreateSiteAdminTokenAsync()
        => (await DatabaseTestUtils.CreateLinkedUserAsync("audit-admin", siteAdmin: true)).Token;

    private static async Task<string> CreateRegularUserTokenAsync()
        => (await DatabaseTestUtils.CreateLinkedUserAsync("audit-user")).Token;

    // ── Auth guards ───────────────────────────────────────────────────────────

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
    public async Task GetAuditEvents_PageSizeCappedAt100()
    {
        var token = await CreateSiteAdminTokenAsync();
        var response = await _client.SendAsync(
            Auth(HttpMethod.Get, "/api/admin/audit?pageSize=999", token));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(100, body.GetProperty("pageSize").GetInt32());
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
