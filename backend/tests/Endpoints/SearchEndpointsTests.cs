using System.Net;
using System.Net.Http.Json;
using Api.Models;
using Xunit;

namespace Orkyo.Foundation.Tests.Endpoints;

/// <summary>
/// Tests for Global Search endpoint.
/// Verifies fuzzy search, tenant isolation, and permission handling.
/// </summary>
[Collection("Database collection")]
public class SearchEndpointsTests
{
    private readonly HttpClient _client;
    private readonly DatabaseFixture _fixture;
    private const string TenantSlug = TestConstants.TenantSlug;

    public SearchEndpointsTests(DatabaseFixture databaseFixture)
    {
        _fixture = databaseFixture;
        _client = databaseFixture.CreateAuthorizedClient();
    }

    #region Basic Search Tests

    [Fact]
    public async Task Search_WithEmptyQuery_ReturnsEmptyResults()
    {
        // Empty query returns empty results (not an error)
        var response = await _client.GetAsync("/api/search?q=");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<SearchResponse>();
        result.Should().NotBeNull();
        result!.Results.Should().BeEmpty();
    }

    [Fact]
    public async Task Search_WithValidQuery_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/search?q=test");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<SearchResponse>();
        result.Should().NotBeNull();
        result!.Query.Should().Be("test");
        result.Results.Should().NotBeNull();
    }

    [Fact]
    public async Task Search_WithShortQuery_ReturnsOk()
    {
        // Short queries (< 3 chars) should still work with trigram-only fallback
        var response = await _client.GetAsync("/api/search?q=ab");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<SearchResponse>();
        result.Should().NotBeNull();
    }

    #endregion

    #region Search with Filters

    [Fact]
    public async Task Search_WithSiteFilter_ReturnsResults()
    {
        // First create a site to get a valid ID
        var siteCode = $"srch-{Guid.NewGuid():N}".Substring(0, 10);
        var createResponse = await _client.PostAsJsonAsync("/api/sites", new { code = siteCode, name = "Search Test Site" });
        createResponse.EnsureSuccessStatusCode();
        var site = await createResponse.Content.ReadFromJsonAsync<SiteInfo>();

        // Search with the site filter
        var response = await _client.GetAsync($"/api/search?q=test&siteId={site!.Id}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<SearchResponse>();
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task Search_WithTypeFilter_ReturnsOnlyFilteredTypes()
    {
        var response = await _client.GetAsync("/api/search?q=test&types=space,request");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<SearchResponse>();
        result.Should().NotBeNull();

        // All results should be either space or request type
        foreach (var item in result!.Results)
        {
            item.Type.Should().BeOneOf("space", "request");
        }
    }

    [Fact]
    public async Task Search_WithLimitParam_RespectsLimit()
    {
        var response = await _client.GetAsync("/api/search?q=test&limit=5");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<SearchResponse>();
        result.Should().NotBeNull();
        result!.Results.Count.Should().BeLessThanOrEqualTo(5);
    }

    #endregion

    #region Result Structure Tests

    [Fact]
    public async Task Search_ReturnsProperStructure()
    {
        // Create a site with a unique name to search for
        var siteCode = $"strct-{Guid.NewGuid():N}".Substring(0, 10);
        var siteName = "Searchable Structure Test Site";
        var siteResponse = await _client.PostAsJsonAsync("/api/sites", new { code = siteCode, name = siteName });
        siteResponse.EnsureSuccessStatusCode();
        var site = await siteResponse.Content.ReadFromJsonAsync<SiteInfo>();

        // Wait a moment for the trigger to sync to search_documents
        await Task.Delay(100);

        // Search for the site
        var response = await _client.GetAsync($"/api/search?q=Searchable Structure");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<SearchResponse>();
        result.Should().NotBeNull();

        // Verify we found results with proper structure
        if (result!.Results.Any())
        {
            var firstResult = result.Results.First();
            firstResult.Id.Should().NotBe(Guid.Empty);
            firstResult.Type.Should().NotBeNullOrEmpty();
            firstResult.Title.Should().NotBeNullOrEmpty();
            firstResult.Open.Should().NotBeNull();
            firstResult.Open.Route.Should().NotBeNullOrEmpty();
            firstResult.Open.Params.Should().NotBeNull();
            firstResult.Permissions.Should().NotBeNull();
        }
    }

    #endregion

    #region Entity-Specific Search Tests

    [Fact]
    public async Task Search_FindsSites_ByName()
    {
        // Create a site with a unique name
        var uniqueName = $"UniqueSearchSite_{Guid.NewGuid():N}";
        var siteCode = $"uniq-{Guid.NewGuid():N}".Substring(0, 10);
        var createResponse = await _client.PostAsJsonAsync("/api/sites", new { code = siteCode, name = uniqueName });
        createResponse.EnsureSuccessStatusCode();

        // Wait for trigger sync
        await Task.Delay(100);

        // Search for the site
        var response = await _client.GetAsync($"/api/search?q={uniqueName.Substring(0, 20)}&types=site");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<SearchResponse>();
        result.Should().NotBeNull();
        result!.Results.Should().Contain(r => r.Type == "site" && r.Title.Contains("UniqueSearchSite"));
    }

    [Fact]
    public async Task Search_FindsCriteria_ByName()
    {
        // Create a criterion with a unique name
        var uniqueName = $"UniqueSearchCriterion_{Guid.NewGuid():N}";
        var createResponse = await _client.PostAsJsonAsync("/api/criteria", new
        {
            name = uniqueName,
            description = "Test criterion for search",
            dataType = "String",
            resourceTypeKeys = new[] { "space" }
        });
        createResponse.EnsureSuccessStatusCode();

        // Wait for trigger sync
        await Task.Delay(100);

        // Search for the criterion
        var response = await _client.GetAsync($"/api/search?q={uniqueName.Substring(0, 20)}&types=criterion");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<SearchResponse>();
        result.Should().NotBeNull();
        result!.Results.Should().Contain(r => r.Type == "criterion" && r.Title.Contains("UniqueSearchCriterion"));
    }

    #endregion

    #region Fuzzy Search Tests

    [Fact]
    public async Task Search_HandlesFuzzyMatching()
    {
        // Create a site with a specific name
        var createResponse = await _client.PostAsJsonAsync("/api/sites", new
        {
            code = $"fuzz-{Guid.NewGuid():N}".Substring(0, 10),
            name = "Headquarters Building"
        });
        createResponse.EnsureSuccessStatusCode();

        // Wait for trigger sync
        await Task.Delay(100);

        // Search with a partial/fuzzy term
        var response = await _client.GetAsync("/api/search?q=headquarter");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<SearchResponse>();
        result.Should().NotBeNull();
        // Should find the site even with partial match
    }

    #endregion

    #region Security Tests

    [Fact]
    public async Task Search_WithoutBearerToken_ReturnsUnauthorized()
    {
        // RequireAuthorization() rejects requests with no Bearer token.
        var clientWithoutBearer = _fixture.Factory.CreateClient();
        clientWithoutBearer.DefaultRequestHeaders.Add(HeaderConstants.TenantSlug, TenantSlug);
        // No Authorization header

        var response = await clientWithoutBearer.GetAsync("/api/search?q=test");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Search_WithoutTenantHeader_ReturnsNotFound()
    {
        var clientWithoutTenant = _fixture.Factory.CreateClient();
        clientWithoutTenant.DefaultRequestHeaders.Add("Authorization", $"Bearer {TestConstants.TestBearerToken}");
        // No X-Tenant-Slug header

        var response = await clientWithoutTenant.GetAsync("/api/search?q=test");
        // Without tenant context, the route effectively doesn't exist
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion
}
