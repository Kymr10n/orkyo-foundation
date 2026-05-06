using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Orkyo.Foundation.Tests.Endpoints;

/// <summary>
/// Tests for Site CRUD endpoints.
/// </summary>
[Collection("Database collection")]
public class SiteEndpointsTests
{
    private readonly HttpClient _client;
    private readonly HttpClient _unauthenticatedClient;

    public SiteEndpointsTests(DatabaseFixture databaseFixture)
    {
        _client = databaseFixture.CreateAuthorizedClient();
        _unauthenticatedClient = databaseFixture.Factory.CreateClient();
    }

    private static string UniqueCode() => $"t-{Guid.NewGuid():N}"[..10];

    // ── Auth guard ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetSites_WithoutAuth_Returns401()
    {
        var response = await _unauthenticatedClient.GetAsync("/api/sites");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateSite_WithoutAuth_Returns401()
    {
        var response = await _unauthenticatedClient.PostAsJsonAsync("/api/sites",
            new { code = "x", name = "x" });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Create ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateSite_ReturnsCreatedSite()
    {
        var code = UniqueCode();
        var request = new { code, name = "Test Site" };
        var response = await _client.PostAsJsonAsync("/api/sites", request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var site = await response.Content.ReadFromJsonAsync<SiteInfo>();
        site.Should().NotBeNull();
        site!.Name.Should().Be("Test Site");
        site.Code.Should().Be(code);
    }

    [Fact]
    public async Task CreateSite_WithOptionalFields_IncludesDescriptionAndAddress()
    {
        var code = UniqueCode();
        var request = new { code, name = "Full Site", description = "A description", address = "123 Main St" };
        var response = await _client.PostAsJsonAsync("/api/sites", request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var site = await response.Content.ReadFromJsonAsync<SiteInfo>();
        site!.Description.Should().Be("A description");
        site.Address.Should().Be("123 Main St");
    }

    [Fact]
    public async Task CreateSite_EmptyCode_Returns400()
    {
        var request = new { code = "", name = "No Code Site" };
        var response = await _client.PostAsJsonAsync("/api/sites", request);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateSite_EmptyName_Returns400()
    {
        var request = new { code = UniqueCode(), name = "" };
        var response = await _client.PostAsJsonAsync("/api/sites", request);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── Read ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetSites_ReturnsListIncludingCreatedSite()
    {
        var code = UniqueCode();
        var request = new { code, name = "List Site" };
        var createResp = await _client.PostAsJsonAsync("/api/sites", request);
        createResp.EnsureSuccessStatusCode();
        var created = await createResp.Content.ReadFromJsonAsync<SiteInfo>();
        var response = await _client.GetAsync("/api/sites");
        response.EnsureSuccessStatusCode();
        var sites = await response.Content.ReadFromJsonAsync<List<SiteInfo>>();
        sites.Should().Contain(s => s.Id == created!.Id);
    }

    [Fact]
    public async Task GetSiteById_ReturnsCorrectSite()
    {
        var code = UniqueCode();
        var createResp = await _client.PostAsJsonAsync("/api/sites", new { code, name = "ById Site" });
        var created = await createResp.Content.ReadFromJsonAsync<SiteInfo>();

        var response = await _client.GetAsync($"/api/sites/{created!.Id}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var site = await response.Content.ReadFromJsonAsync<SiteInfo>();
        site!.Id.Should().Be(created.Id);
        site.Name.Should().Be("ById Site");
    }

    [Fact]
    public async Task GetSiteById_NonExistent_Returns404()
    {
        var response = await _client.GetAsync($"/api/sites/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetSites_WithPagination_ReturnsPagedResult()
    {
        // Create a site so there is at least one
        var code = UniqueCode();
        await _client.PostAsJsonAsync("/api/sites", new { code, name = "Page Site" });

        var response = await _client.GetAsync("/api/sites?page=1&pageSize=2");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);
        doc.RootElement.TryGetProperty("items", out _).Should().BeTrue("paged result should have 'items'");
        doc.RootElement.TryGetProperty("totalItems", out _).Should().BeTrue("paged result should have 'totalItems'");
    }

    // ── Update ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateSite_ChangesFields()
    {
        var code = UniqueCode();
        var create = new { code, name = "Update Site" };
        var createResp = await _client.PostAsJsonAsync("/api/sites", create);
        var site = await createResp.Content.ReadFromJsonAsync<SiteInfo>();
        var update = new { code, name = "Updated Name" };
        var updateResp = await _client.PutAsJsonAsync($"/api/sites/{site!.Id}", update);
        updateResp.EnsureSuccessStatusCode();
        var updated = await updateResp.Content.ReadFromJsonAsync<SiteInfo>();
        updated!.Name.Should().Be("Updated Name");
    }

    [Fact]
    public async Task UpdateSite_NonExistent_Returns404()
    {
        var update = new { code = UniqueCode(), name = "Ghost" };
        var response = await _client.PutAsJsonAsync($"/api/sites/{Guid.NewGuid()}", update);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UpdateSite_EmptyName_Returns400()
    {
        var code = UniqueCode();
        var createResp = await _client.PostAsJsonAsync("/api/sites", new { code, name = "Valid" });
        var site = await createResp.Content.ReadFromJsonAsync<SiteInfo>();

        var response = await _client.PutAsJsonAsync($"/api/sites/{site!.Id}", new { code, name = "" });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── Delete ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteSite_RemovesSite()
    {
        var code = UniqueCode();
        var create = new { code, name = "Delete Site" };
        var createResp = await _client.PostAsJsonAsync("/api/sites", create);
        var site = await createResp.Content.ReadFromJsonAsync<SiteInfo>();
        var delResp = await _client.DeleteAsync($"/api/sites/{site!.Id}");
        delResp.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var getResp = await _client.GetAsync($"/api/sites/{site.Id}");
        getResp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteSite_NonExistent_Returns404()
    {
        var response = await _client.DeleteAsync($"/api/sites/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
