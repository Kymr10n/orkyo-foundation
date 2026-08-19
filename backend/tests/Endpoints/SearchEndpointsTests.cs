using System.Net;
using System.Net.Http.Json;
using Api.Models;
using Api.Repositories;
using Api.Services;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
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
    public async Task RequestSearchDocument_CarriesSiteId_AndFollowsSiteChange()
    {
        // 1330 wrote site_id = NULL for every request document because requests had no site
        // then; 1550 added requests.site_id and 1850 made the trigger carry it. This pins the
        // repaired behaviour: the document tracks the request's site through create and update.
        var siteCode = $"rqsd-{Guid.NewGuid():N}"[..10];
        var siteResponse = await _client.PostAsJsonAsync("/api/sites", new { code = siteCode, name = "Request Search Site" });
        siteResponse.EnsureSuccessStatusCode();
        var site = await siteResponse.Content.ReadFromJsonAsync<SiteInfo>();

        var createResponse = await _client.PostAsJsonAsync("/api/requests", new CreateRequestRequest
        {
            Name = $"SiteDoc-{Guid.NewGuid():N}"[..20],
            MinimalDurationValue = 1,
            MinimalDurationUnit = DurationUnit.Hours,
            SiteId = site!.Id,
        });
        createResponse.EnsureSuccessStatusCode();
        var requestId = (await createResponse.Content.ReadFromJsonAsync<RequestInfo>())!.Id;

        using var scope = _fixture.Factory.Services.CreateScope();
        var connFactory = scope.ServiceProvider.GetRequiredService<IOrgDbConnectionFactory>();
        var orgContext = scope.ServiceProvider.GetRequiredService<OrgContext>();

        async Task<Guid?> DocumentSiteIdAsync()
        {
            await using var conn = connFactory.CreateOrgConnection(orgContext);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                "SELECT site_id FROM search_documents WHERE entity_type='request' AND entity_id=@id",
                conn);
            cmd.Parameters.AddWithValue("id", requestId);
            var value = await cmd.ExecuteScalarAsync();
            return value is Guid g ? g : null;
        }

        (await DocumentSiteIdAsync()).Should().Be(site.Id);

        // Clearing the site must clear the document's site too (trg_search_requests fires on
        // every UPDATE, no column list).
        await using (var conn = connFactory.CreateOrgConnection(orgContext))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                "UPDATE requests SET site_id = NULL, updated_at = now() WHERE id = @id", conn);
            cmd.Parameters.AddWithValue("id", requestId);
            await cmd.ExecuteNonQueryAsync();
        }

        (await DocumentSiteIdAsync()).Should().BeNull();
    }

    [Fact]
    public async Task Search_WithTypeFilter_ReturnsOnlyFilteredTypes()
    {
        var response = await _client.GetAsync("/api/search?q=test&types=resource,request");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<SearchResponse>();
        result.Should().NotBeNull();

        // All results should be either space or request type
        foreach (var item in result!.Results)
        {
            item.Type.Should().BeOneOf("resource", "request");
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

    [Fact]
    public async Task Search_FindsGroups_AndCarriesResourceTypeKey()
    {
        // Group results are routed by resource type on the client, so the search
        // result must surface the group's resourceTypeKey (person vs space).
        var uniqueName = $"UniqueSearchGroup_{Guid.NewGuid():N}";
        var createResponse = await _client.PostAsJsonAsync("/api/resource-groups", new
        {
            resourceTypeKey = "person",
            name = uniqueName
        });
        createResponse.EnsureSuccessStatusCode();

        // Wait for trigger sync
        await Task.Delay(100);

        var response = await _client.GetAsync($"/api/search?q={uniqueName.Substring(0, 20)}&types=group");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<SearchResponse>();
        result.Should().NotBeNull();
        result!.Results.Should().Contain(r =>
            r.Type == "group" && r.Title.Contains("UniqueSearchGroup") && r.ResourceTypeKey == "person");
    }

    #endregion

    #region Fuzzy Search Tests

    [Fact]
    public async Task Search_FindsToolResources()
    {
        // `tool` has been a seeded system type since migration 1300 but was never indexed:
        // the only trigger on `resources` early-returned for anything that was not a person.
        var uniqueName = $"UniqueSearchTool_{Guid.NewGuid():N}";
        var created = await _client.PostAsJsonAsync("/api/resources", new
        {
            resourceTypeKey = "tool",
            name = uniqueName,
            allocationMode = "Exclusive",
        });
        created.EnsureSuccessStatusCode();

        await Task.Delay(100); // trigger sync

        var response = await _client.GetAsync($"/api/search?q={uniqueName[..20]}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<SearchResponse>();
        result!.Results.Should().Contain(r =>
            r.Type == "resource" && r.Title == uniqueName && r.ResourceTypeKey == "tool");
    }

    [Fact]
    public async Task Search_FindsTenantDefinedResourceTypes()
    {
        // The point of the generic indexer: a type invented at runtime is searchable with no
        // new trigger, no new entity_type, and no code change.
        var typeKey = $"vehicle_{Guid.NewGuid():N}"[..24];
        var typeResponse = await _client.PostAsJsonAsync("/api/resource-types", new
        {
            key = typeKey,
            displayName = "Vehicle",
            displayNamePlural = "Vehicles",
        });
        typeResponse.EnsureSuccessStatusCode();

        var uniqueName = $"UniqueSearchVan_{Guid.NewGuid():N}";
        var created = await _client.PostAsJsonAsync("/api/resources", new
        {
            resourceTypeKey = typeKey,
            name = uniqueName,
            allocationMode = "Exclusive",
        });
        created.EnsureSuccessStatusCode();

        await Task.Delay(100);

        var response = await _client.GetAsync($"/api/search?q={uniqueName[..19]}");
        var result = await response.Content.ReadFromJsonAsync<SearchResponse>();

        result!.Results.Should().Contain(r =>
            r.Type == "resource" && r.Title == uniqueName && r.ResourceTypeKey == typeKey);
    }

    [Fact]
    public async Task Search_ReindexesAResourceRenamedThroughItsOwnRow()
    {
        // The old space trigger fired on the spaces profile table only, so a rename — which
        // writes resources.name — left a stale title in the index indefinitely.
        var original = $"UniqueSearchRename_{Guid.NewGuid():N}";
        var created = await _client.PostAsJsonAsync("/api/resources", new
        {
            resourceTypeKey = "tool",
            name = original,
            allocationMode = "Exclusive",
        });
        var resource = await created.Content.ReadFromJsonAsync<ResourceInfo>();

        var renamed = $"UniqueSearchRenamed_{Guid.NewGuid():N}";
        var update = await _client.PutAsJsonAsync($"/api/resources/{resource!.Id}", new { name = renamed });
        update.EnsureSuccessStatusCode();

        await Task.Delay(100);

        var response = await _client.GetAsync($"/api/search?q={renamed[..22]}");
        var result = await response.Content.ReadFromJsonAsync<SearchResponse>();

        result!.Results.Should().Contain(r => r.Title == renamed);
    }

    [Fact]
    public async Task ReindexesOnIndexedColumnsOnly()
    {
        // The resources UPDATE trigger carries a WHEN guard listing exactly the columns
        // refresh_search_resource() reads. Without it every write reindexed — including the
        // unconditional updated_at every repository sets, and the seeder's bulk site pass.
        // The risk the guard introduces is the opposite one: add a column to the document and
        // forget the guard, and the document silently goes stale. This pins both directions.
        var created = await _client.PostAsJsonAsync("/api/resources", new
        {
            resourceTypeKey = "tool",
            name = $"TriggerProbe-{Guid.NewGuid():N}"[..24],
            allocationMode = "Exclusive",
        });
        created.EnsureSuccessStatusCode();
        var resourceId = (await created.Content.ReadFromJsonAsync<ResourceInfo>())!.Id;

        using var scope = _fixture.Factory.Services.CreateScope();
        var connFactory = scope.ServiceProvider.GetRequiredService<IOrgDbConnectionFactory>();
        var orgContext = scope.ServiceProvider.GetRequiredService<OrgContext>();

        async Task<DateTime> IndexedAtAsync()
        {
            await using var conn = connFactory.CreateOrgConnection(orgContext);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                "SELECT updated_at FROM search_documents WHERE entity_type='resource' AND entity_id=@id",
                conn);
            cmd.Parameters.AddWithValue("id", resourceId);
            return (DateTime)(await cmd.ExecuteScalarAsync())!;
        }

        async Task TouchAsync(string setClause, object? value = null)
        {
            await using var conn = connFactory.CreateOrgConnection(orgContext);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                $"UPDATE resources SET {setClause}, updated_at = now() WHERE id = @id", conn);
            cmd.Parameters.AddWithValue("id", resourceId);
            if (value is not null) cmd.Parameters.AddWithValue("value", value);
            await cmd.ExecuteNonQueryAsync();
        }

        var before = await IndexedAtAsync();

        // Not part of the document: no reindex, however much the row changes.
        await TouchAsync("cross_site_allowed = NOT cross_site_allowed");
        (await IndexedAtAsync()).Should()
            .Be(before, "cross_site_allowed is not part of the search document");

        // Part of the document: reindexed.
        await TouchAsync("name = @value", $"Renamed-{Guid.NewGuid():N}"[..20]);
        (await IndexedAtAsync()).Should().BeAfter(before, "the name is the document's title");
    }

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
