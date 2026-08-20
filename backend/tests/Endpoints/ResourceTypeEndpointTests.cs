using System.Net;
using System.Net.Http.Json;
using Api.Models;
using Xunit;

namespace Orkyo.Foundation.Tests.Endpoints;

[Collection("Database collection")]
public class ResourceTypeEndpointTests
{
    private readonly HttpClient _client;
    private readonly DatabaseFixture _fixture;

    public ResourceTypeEndpointTests(DatabaseFixture databaseFixture)
    {
        _fixture = databaseFixture;
        _client = databaseFixture.CreateAuthorizedClient();
    }

    [Fact]
    public async Task GetResourceTypes_ReturnsTheFixtureTypes_AllOrdinary()
    {
        var response = await _client.GetAsync("/api/resource-types");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var types = await response.Content.ReadFromJsonAsync<List<ResourceTypeInfo>>();
        Assert.NotNull(types);
        // No type is built in any more: migrations seed nothing, the fixture creates the
        // classic three for the suite, and every row is an ordinary tenant type.
        Assert.Contains(types, t => t.Key == "space");
        Assert.Contains(types, t => t.Key == "person");
        Assert.Contains(types, t => t.Key == "tool");
        Assert.All(types, t => Assert.False(t.IsSystem));
    }

    [Fact]
    public async Task GetResourceTypeById_ReturnsCorrectType()
    {
        var all = await _client.GetFromJsonAsync<List<ResourceTypeInfo>>("/api/resource-types");
        var spaceType = all!.First(t => t.Key == "space");

        var response = await _client.GetAsync($"/api/resource-types/{spaceType.Id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var rt = await response.Content.ReadFromJsonAsync<ResourceTypeInfo>();
        Assert.Equal("space", rt!.Key);
    }

    [Fact]
    public async Task GetResourceTypeById_Returns404_ForNonExistentId()
    {
        var response = await _client.GetAsync($"/api/resource-types/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetResourceTypes_RequiresAuthentication()
    {
        var anon = _fixture.Factory.CreateClient();
        anon.DefaultRequestHeaders.Add(HeaderConstants.TenantSlug, TestConstants.TenantSlug);
        var response = await anon.GetAsync("/api/resource-types");
        Assert.True(
            response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
            $"Expected 401 or 403, got {response.StatusCode}");
    }
}
