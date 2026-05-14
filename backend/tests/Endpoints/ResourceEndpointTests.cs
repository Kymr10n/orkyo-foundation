using System.Net;
using System.Net.Http.Json;
using Api.Models;
using Xunit;

namespace Orkyo.Foundation.Tests.Endpoints;

[Collection("Database collection")]
public class ResourceEndpointTests
{
    private readonly HttpClient _client;
    private readonly DatabaseFixture _fixture;

    public ResourceEndpointTests(DatabaseFixture databaseFixture)
    {
        _fixture = databaseFixture;
        _client = databaseFixture.CreateAuthorizedClient();
    }

    private async Task<ResourceInfo> CreatePersonAsync(string name = "Test Person")
    {
        var request = new CreateResourceRequest
        {
            ResourceTypeKey = "person",
            Name = name,
            AllocationMode = "Fractional",
            BaseAvailabilityPercent = 100,
        };
        var response = await _client.PostAsJsonAsync("/api/resources", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ResourceInfo>())!;
    }

    [Fact]
    public async Task CreateResource_Person_Returns201()
    {
        var r = await CreatePersonAsync($"Person-{Guid.NewGuid():N}"[..20]);
        Assert.Equal("person", r.ResourceTypeKey);
        Assert.Equal("Fractional", r.AllocationMode);
        Assert.True(r.IsActive);
    }

    [Fact]
    public async Task CreateResource_InvalidAllocationMode_Returns400()
    {
        var request = new CreateResourceRequest
        {
            ResourceTypeKey = "tool",
            Name = "Bad Tool",
            AllocationMode = "NotARealMode",
        };
        var response = await _client.PostAsJsonAsync("/api/resources", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateResource_InvalidAvailabilityPercent_Returns400()
    {
        var request = new CreateResourceRequest
        {
            ResourceTypeKey = "person",
            Name = "Over Person",
            AllocationMode = "Fractional",
            BaseAvailabilityPercent = 150,
        };
        var response = await _client.PostAsJsonAsync("/api/resources", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetResource_ById_ReturnsResource()
    {
        var created = await CreatePersonAsync($"GetById-{Guid.NewGuid():N}"[..20]);
        var response = await _client.GetAsync($"/api/resources/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var r = await response.Content.ReadFromJsonAsync<ResourceInfo>();
        Assert.Equal(created.Id, r!.Id);
    }

    [Fact]
    public async Task GetResources_FilterByType_ReturnsOnlyMatchingType()
    {
        await CreatePersonAsync($"FilterPerson-{Guid.NewGuid():N}"[..20]);

        var response = await _client.GetAsync("/api/resources?resourceTypeKey=person");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await response.Content.ReadFromJsonAsync<List<ResourceInfo>>();
        Assert.NotNull(list);
        Assert.All(list, r => Assert.Equal("person", r.ResourceTypeKey));
    }

    [Fact]
    public async Task DeactivateResource_SetsIsActiveFalse()
    {
        var created = await CreatePersonAsync($"Deactivate-{Guid.NewGuid():N}"[..20]);

        var deleteResponse = await _client.DeleteAsync($"/api/resources/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var getResponse = await _client.GetAsync($"/api/resources/{created.Id}");
        var r = await getResponse.Content.ReadFromJsonAsync<ResourceInfo>();
        Assert.False(r!.IsActive);
    }

    [Fact]
    public async Task CreateResource_Unauthenticated_Returns401()
    {
        var anon = _fixture.Factory.CreateClient();
        anon.DefaultRequestHeaders.Add("X-Tenant-Slug", TestConstants.TenantSlug);

        var request = new CreateResourceRequest
        {
            ResourceTypeKey = "person",
            Name = "Unauthorized",
            AllocationMode = "Fractional",
        };
        var response = await anon.PostAsJsonAsync("/api/resources", request);
        Assert.True(
            response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
            $"Expected 401/403, got {response.StatusCode}");
    }
}
