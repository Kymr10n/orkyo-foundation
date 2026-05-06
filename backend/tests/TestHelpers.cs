using System.Net.Http.Json;
using Api.Models;

namespace Orkyo.Foundation.Tests;

/// <summary>Shared helper methods for test classes to reduce code duplication.</summary>
public static class TestHelpers
{
    public static async Task<Guid> GetOrCreateTestSite(HttpClient client)
    {
        var sitesResponse = await client.GetAsync("/api/sites");
        if (sitesResponse.IsSuccessStatusCode)
        {
            var sites = await sitesResponse.Content.ReadFromJsonAsync<List<SiteInfo>>();
            var testSite = sites?.FirstOrDefault();
            if (testSite != null)
                return testSite.Id;
        }

        return Guid.Parse("d533232d-6ead-4b11-a893-4721364a04c9");
    }

    public static async Task<Guid> GetOrCreateTestSpace(HttpClient client)
    {
        var siteId = await GetOrCreateTestSite(client);

        var spacesResponse = await client.GetAsync($"/api/sites/{siteId}/spaces");
        if (spacesResponse.IsSuccessStatusCode)
        {
            var spaces = await spacesResponse.Content.ReadFromJsonAsync<List<SpaceInfo>>();
            var testSpace = spaces?.FirstOrDefault();
            if (testSpace != null)
                return testSpace.Id;
        }

        var uniqueCode = $"TEST-{Guid.NewGuid():N}"[..15];
        var createSpaceRequest = new CreateSpaceRequest
        {
            Name = "Test Space for Requests",
            Code = uniqueCode,
            IsPhysical = false,
            Geometry = null,
        };

        var createResponse = await client.PostAsJsonAsync($"/api/sites/{siteId}/spaces", createSpaceRequest);
        if (createResponse.IsSuccessStatusCode)
        {
            var createdSpace = await createResponse.Content.ReadFromJsonAsync<SpaceInfo>();
            if (createdSpace != null)
                return createdSpace.Id;
        }

        throw new Exception("Failed to get or create test space");
    }

    public static async Task<Guid> CreateUniqueTestSpace(HttpClient client)
    {
        var siteId = await GetOrCreateTestSite(client);
        var uniqueCode = $"TEST-{Guid.NewGuid():N}"[..15];
        var createSpaceRequest = new CreateSpaceRequest
        {
            Name = $"Test Space {uniqueCode}",
            Code = uniqueCode,
            IsPhysical = false,
            Geometry = null,
        };

        var createResponse = await client.PostAsJsonAsync($"/api/sites/{siteId}/spaces", createSpaceRequest);
        createResponse.EnsureSuccessStatusCode();
        var createdSpace = await createResponse.Content.ReadFromJsonAsync<SpaceInfo>();
        return createdSpace?.Id ?? throw new Exception("Failed to create unique test space");
    }

    public static async Task<Guid> GetOrCreateAnotherTestSpace(HttpClient client)
    {
        var siteId = await GetOrCreateTestSite(client);

        var spacesResponse = await client.GetAsync($"/api/sites/{siteId}/spaces");
        if (spacesResponse.IsSuccessStatusCode)
        {
            var spaces = await spacesResponse.Content.ReadFromJsonAsync<List<SpaceInfo>>();
            if (spaces != null && spaces.Count >= 2)
                return spaces[1].Id;
        }

        var uniqueCode = $"TEST2-{Guid.NewGuid():N}"[..15];
        var createSpaceRequest = new CreateSpaceRequest
        {
            Name = "Second Test Space",
            Code = uniqueCode,
            IsPhysical = false,
            Geometry = null,
        };

        var createResponse = await client.PostAsJsonAsync($"/api/sites/{siteId}/spaces", createSpaceRequest);
        if (createResponse.IsSuccessStatusCode)
        {
            var createdSpace = await createResponse.Content.ReadFromJsonAsync<SpaceInfo>();
            if (createdSpace != null)
                return createdSpace.Id;
        }

        throw new Exception("Failed to get or create second test space");
    }

    public static async Task<List<CriterionInfo>> GetAvailableCriteria(HttpClient client)
    {
        var response = await client.GetAsync("/api/criteria");
        if (!response.IsSuccessStatusCode)
            throw new Exception("Failed to get criteria");

        var criteria = await response.Content.ReadFromJsonAsync<List<CriterionInfo>>();
        return criteria ?? [];
    }
}
