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


    private static readonly JsonSerializerOptions RequestJsonOpts = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// A request carrying <paramref name="token"/> as its bearer credential and
    /// <paramref name="body"/>, when given, as camelCase JSON with enums as strings.
    /// </summary>
    public static HttpRequestMessage AuthRequest(HttpMethod method, string url, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
            request.Content = JsonContent.Create(body, options: RequestJsonOpts);
        return request;
    }

    /// <summary>A key that no other test uses: <c>{prefix}_{guid}</c>.</summary>
    public static string UniqueKey(string prefix) => $"{prefix}_{Guid.NewGuid():N}";

    /// <summary>Creates a fractional person resource through the API.</summary>
    public static async Task<ResourceInfo> CreatePersonAsync(HttpClient client, string? name = null, int availabilityPercent = 100)
    {
        var response = await client.PostAsJsonAsync("/api/resources", new CreateResourceRequest
        {
            ResourceTypeKey = ResourceTypeKeys.Person,
            Name = name ?? $"Person-{Guid.NewGuid():N}"[..20],
            AllocationMode = AllocationModes.Fractional,
            BaseAvailabilityPercent = availabilityPercent,
        });
        Assert.Equal(System.Net.HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ResourceInfo>(JsonOpts))!;
    }

    /// <summary>Creates a "Machine" resource type through the API.</summary>
    public static async Task<ResourceTypeInfo> CreateResourceTypeAsync(
        HttpClient client, string? key = null, bool hasGeometry = false, bool scanCodesEnabled = true)
    {
        var response = await client.PostAsJsonAsync("/api/resource-types", new CreateResourceTypeRequest
        {
            Key = key ?? UniqueKey("machine"),
            DisplayName = "Machine",
            DisplayNamePlural = "Machines",
            HasGeometry = hasGeometry,
            ScanCodesEnabled = scanCodesEnabled,
        });
        Assert.Equal(System.Net.HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ResourceTypeInfo>(JsonOpts))!;
    }

    /// <summary>The <see cref="Guid"/> a single-value query returns.</summary>
    public static async Task<Guid> ScalarGuidAsync(Npgsql.NpgsqlConnection conn, Npgsql.NpgsqlTransaction tx, string sql)
    {
        await using var cmd = new Npgsql.NpgsqlCommand(sql, conn, tx);
        return (Guid)(await cmd.ExecuteScalarAsync())!;
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

    private static async Task<Guid> CreatePlaceableAsync(HttpClient client, Guid siteId, string name, string code)
    {
        var response = await client.PostAsJsonAsync("/api/resources", PlaceableRequest(siteId, name, code));
        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<ResourceInfo>();
        return created?.Id ?? throw new Exception($"Failed to create placeable resource '{name}'");
    }

    public static async Task<Guid> CreateUniqueTestSpace(HttpClient client)
    {
        var siteId = DatabaseFixture.SiteId;
        var uniqueCode = $"TEST-{Guid.NewGuid():N}"[..15];
        return await CreatePlaceableAsync(client, siteId, $"Test Space {uniqueCode}", uniqueCode);
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
