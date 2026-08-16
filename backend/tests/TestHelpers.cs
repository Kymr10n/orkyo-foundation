using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Api.Constants;
using Api.Models;

namespace Orkyo.Foundation.Tests;

/// <summary>Shared helper methods for test classes to reduce code duplication.</summary>
public static class TestHelpers
{
    /// <summary>
    /// JSON options that match the backend's serialization settings:
    /// enums are serialized as camelCase strings, not integers.
    /// Use with PostAsJsonAsync / ReadFromJsonAsync when the body contains enum properties.
    /// </summary>
    public static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };


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

    /// <summary>
    /// Seeds through the generic resource surface, supplying the placement defaults the retired
    /// site-scoped space route used to hardcode: exclusive allocation, a home site, and no
    /// travelling off it.
    /// </summary>
    private static CreateResourceRequest PlaceableRequest(Guid siteId, string name, string code) => new()
    {
        ResourceTypeKey = ResourceTypeKeys.Space,
        Name = name,
        Code = code,
        AllocationMode = AllocationModes.Exclusive,
        HomeSiteId = siteId,
        CrossSiteAllowed = false,
        IsPhysical = false,
        Geometry = null,
    };

    private static async Task<List<ResourceInfo>> GetPlaceableAsync(HttpClient client, Guid siteId)
    {
        var response = await client.GetAsync(
            $"/api/resources?hasGeometry=true&isActive=true&siteId={siteId}");
        if (!response.IsSuccessStatusCode) return [];
        var envelope = await response.Content.ReadFromJsonAsync<JsonElement>();
        return envelope.GetProperty("data").Deserialize<List<ResourceInfo>>(JsonOpts) ?? [];
    }

    private static async Task<Guid> CreatePlaceableAsync(HttpClient client, Guid siteId, string name, string code)
    {
        var response = await client.PostAsJsonAsync("/api/resources", PlaceableRequest(siteId, name, code));
        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<ResourceInfo>();
        return created?.Id ?? throw new Exception($"Failed to create placeable resource '{name}'");
    }

    public static async Task<Guid> GetOrCreateTestSpace(HttpClient client)
    {
        var siteId = await GetOrCreateTestSite(client);

        var existing = await GetPlaceableAsync(client, siteId);
        if (existing.Count > 0) return existing[0].Id;

        var uniqueCode = $"TEST-{Guid.NewGuid():N}"[..15];
        return await CreatePlaceableAsync(client, siteId, "Test Space for Requests", uniqueCode);
    }

    public static async Task<Guid> CreateUniqueTestSpace(HttpClient client)
    {
        var siteId = await GetOrCreateTestSite(client);
        var uniqueCode = $"TEST-{Guid.NewGuid():N}"[..15];
        return await CreatePlaceableAsync(client, siteId, $"Test Space {uniqueCode}", uniqueCode);
    }

    public static async Task<Guid> GetOrCreateAnotherTestSpace(HttpClient client)
    {
        var siteId = await GetOrCreateTestSite(client);

        var existing = await GetPlaceableAsync(client, siteId);
        if (existing.Count >= 2) return existing[1].Id;

        var uniqueCode = $"TEST2-{Guid.NewGuid():N}"[..15];
        return await CreatePlaceableAsync(client, siteId, "Second Test Space", uniqueCode);
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
