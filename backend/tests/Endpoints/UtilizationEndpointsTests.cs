using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Models;
using Xunit;

namespace Orkyo.Foundation.Tests.Endpoints;

/// <summary>
/// Integration tests for the utilization endpoints.
/// GET /api/resources/{id}/utilization
/// GET /api/resource-groups/{id}/utilization
/// GET /api/utilization
/// </summary>
[Collection("Database collection")]
public class UtilizationEndpointsTests
{
    private readonly HttpClient _client;
    private readonly HttpClient _anonClient;

    public UtilizationEndpointsTests(DatabaseFixture databaseFixture)
    {
        _client = databaseFixture.CreateAuthorizedClient();
        // Unauthenticated client using the same test server (no credentials)
        _anonClient = databaseFixture.Factory.CreateClient();
    }

    private static string DateParam(DateTime dt) =>
        Uri.EscapeDataString(dt.ToString("o"));

    private static (string from, string to) DateRange() =>
        (DateParam(DateTime.UtcNow.AddDays(-7)), DateParam(DateTime.UtcNow));

    // ── GET /api/resources/{id}/utilization ──────────────────────────────────

    [Fact]
    public async Task GetResourceUtilization_WithValidResource_Returns200()
    {
        // Create a resource to get utilization for
        var createReq = new CreateResourceRequest
        {
            ResourceTypeKey = "person",
            Name = $"UtilPerson-{Guid.NewGuid():N}"[..20],
            AllocationMode = "Fractional",
        };
        var createResp = await _client.PostAsJsonAsync("/api/resources", createReq);
        createResp.EnsureSuccessStatusCode();
        var resource = await createResp.Content.ReadFromJsonAsync<ResourceInfo>();

        var (from, to) = DateRange();
        var response = await _client.GetAsync(
            $"/api/resources/{resource!.Id}/utilization?from={from}&to={to}&granularity=day");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.ValueKind == JsonValueKind.Object || body.ValueKind == JsonValueKind.Array);
    }

    [Fact]
    public async Task GetResourceUtilization_NonExistentResource_Returns404()
    {
        var (from, to) = DateRange();
        var response = await _client.GetAsync(
            $"/api/resources/{Guid.NewGuid()}/utilization?from={from}&to={to}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── GET /api/resource-groups/{id}/utilization ────────────────────────────

    [Fact]
    public async Task GetGroupUtilization_WithValidGroup_Returns200()
    {
        var createReq = new CreateResourceGroupRequest
        {
            ResourceTypeKey = "person",
            Name = $"UtilGroup-{Guid.NewGuid():N}"[..20],
            DefaultAvailabilityPercent = 100,
        };
        var createResp = await _client.PostAsJsonAsync("/api/resource-groups", createReq);
        createResp.EnsureSuccessStatusCode();
        var group = await createResp.Content.ReadFromJsonAsync<ResourceGroupInfo>();

        var (from, to) = DateRange();
        var response = await _client.GetAsync(
            $"/api/resource-groups/{group!.Id}/utilization?from={from}&to={to}&granularity=day");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetGroupUtilization_NonExistentGroup_ReturnsNotFoundOrEmpty()
    {
        // Endpoint returns 404 or empty data for unknown groups — either is acceptable
        var (from, to) = DateRange();
        var response = await _client.GetAsync(
            $"/api/resource-groups/{Guid.NewGuid()}/utilization?from={from}&to={to}");

        Assert.True(
            response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.OK,
            $"Expected NotFound or OK, got {response.StatusCode}");
    }

    // ── GET /api/utilization ─────────────────────────────────────────────────

    [Fact]
    public async Task GetTenantUtilization_WithDateRange_Returns200()
    {
        var (from, to) = DateRange();
        var response = await _client.GetAsync(
            $"/api/utilization/?from={from}&to={to}&granularity=day");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetTenantUtilization_Unauthenticated_Returns401()
    {
        var (from, to) = DateRange();
        var response = await _anonClient.GetAsync(
            $"/api/utilization/?from={from}&to={to}");

        Assert.True(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task GetTenantUtilization_WithResourceTypeFilter_Returns200()
    {
        var (from, to) = DateRange();
        var response = await _client.GetAsync(
            $"/api/utilization/?from={from}&to={to}&resourceTypeKey=person&granularity=week");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── GET /api/utilization/by-resource ─────────────────────────────────────

    [Fact]
    public async Task GetUtilizationByResource_Returns200WithPerResourceArray()
    {
        // Ensure at least one person resource exists so the array is non-trivial.
        var createReq = new CreateResourceRequest
        {
            ResourceTypeKey = "person",
            Name = $"BulkPerson-{Guid.NewGuid():N}"[..20],
            AllocationMode = "Fractional",
        };
        (await _client.PostAsJsonAsync("/api/resources", createReq)).EnsureSuccessStatusCode();

        var (from, to) = DateRange();
        var response = await _client.GetAsync(
            $"/api/utilization/by-resource?from={from}&to={to}&resourceTypeKey=person&granularity=day");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Array, body.ValueKind);
        Assert.True(body.GetArrayLength() > 0);
        // Each entry exposes a resourceId and its own buckets array.
        var first = body[0];
        Assert.True(first.TryGetProperty("resourceId", out _));
        Assert.Equal(JsonValueKind.Array, first.GetProperty("buckets").ValueKind);
    }

    [Fact]
    public async Task GetUtilizationByResource_WithSiteId_ReturnsOnlySiteRelevantPeople()
    {
        // A site + a person homed there; the bulk grid query filtered to that site includes them,
        // and filtered to an unrelated site excludes them.
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var siteResp = await _client.PostAsJsonAsync("/api/sites",
            new { code = $"S{suffix}", name = $"Site {suffix}", description = (string?)null, address = (string?)null });
        siteResp.EnsureSuccessStatusCode();
        var siteId = (await siteResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var createResp = await _client.PostAsJsonAsync("/api/resources", new CreateResourceRequest
        {
            ResourceTypeKey = "person",
            Name = $"SitePerson-{suffix}",
            AllocationMode = "Fractional",
            HomeSiteId = siteId,
        });
        createResp.EnsureSuccessStatusCode();
        var personId = (await createResp.Content.ReadFromJsonAsync<ResourceInfo>())!.Id;

        var (from, to) = DateRange();

        var atSite = await _client.GetFromJsonAsync<JsonElement>(
            $"/api/utilization/by-resource?from={from}&to={to}&resourceTypeKey=person&granularity=day&siteId={siteId}");
        Assert.Contains(atSite.EnumerateArray(), e => e.GetProperty("resourceId").GetGuid() == personId);

        var otherSite = await _client.GetFromJsonAsync<JsonElement>(
            $"/api/utilization/by-resource?from={from}&to={to}&resourceTypeKey=person&granularity=day&siteId={Guid.NewGuid()}");
        Assert.DoesNotContain(otherSite.EnumerateArray(), e => e.GetProperty("resourceId").GetGuid() == personId);
    }
}
