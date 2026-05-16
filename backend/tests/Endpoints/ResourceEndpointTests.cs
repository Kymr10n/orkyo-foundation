using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Endpoints;
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
        var envelope = await response.Content.ReadFromJsonAsync<JsonElement>();
        var list = envelope.GetProperty("data").Deserialize<List<ResourceInfo>>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
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

    // ── Capabilities ──────────────────────────────────────────────────────────

    private async Task<CriterionInfo> GetSeedCriterionAsync(string name)
    {
        var response = await _client.GetAsync("/api/criteria");
        response.EnsureSuccessStatusCode();
        var criteria = await response.Content.ReadFromJsonAsync<List<CriterionInfo>>();
        return criteria!.First(c => c.Name == name);
    }

    [Fact]
    public async Task GetResourceCapabilities_ReturnsEmptyList_WhenNoCapabilities()
    {
        // Arrange
        var resource = await CreatePersonAsync("CapTest-" + Guid.NewGuid().ToString("N")[..20]);

        // Act
        var response = await _client.GetAsync($"/api/resources/{resource.Id}/capabilities");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var capabilities = await response.Content.ReadFromJsonAsync<List<ResourceCapabilityInfo>>();
        Assert.NotNull(capabilities);
        Assert.Empty(capabilities);
    }

    [Fact]
    public async Task GetResourceCapabilities_Returns404_WhenResourceNotFound()
    {
        // Arrange
        var nonExistentResourceId = Guid.NewGuid();

        // Act
        var response = await _client.GetAsync($"/api/resources/{nonExistentResourceId}/capabilities");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AddResourceCapability_CreatesCapability_WithValidData()
    {
        // Arrange
        var resource = await CreatePersonAsync("CapTest-" + Guid.NewGuid().ToString("N")[..20]);
        var criterion = await GetSeedCriterionAsync("seed_number");

        var request = new AddResourceCapabilityRequest(criterion.Id, JsonSerializer.SerializeToElement(100.5));

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/resources/{resource.Id}/capabilities",
            request);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var capability = await response.Content.ReadFromJsonAsync<ResourceCapabilityInfo>();
        Assert.NotNull(capability);
        Assert.Equal(resource.Id, capability.ResourceId);
        Assert.Equal(criterion.Id, capability.CriterionId);
        Assert.Equal(100.5, capability.Value.GetDouble());
    }

    [Fact]
    public async Task AddResourceCapability_Returns404_WhenResourceNotFound()
    {
        // Arrange
        var nonExistentResourceId = Guid.NewGuid();
        var criterion = await GetSeedCriterionAsync("seed_boolean");

        var request = new AddResourceCapabilityRequest(criterion.Id, JsonSerializer.SerializeToElement(true));

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/resources/{nonExistentResourceId}/capabilities",
            request);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteResourceCapability_RemovesCapability_WhenExists()
    {
        // Arrange
        var resource = await CreatePersonAsync("CapTest-" + Guid.NewGuid().ToString("N")[..20]);
        var criterion = await GetSeedCriterionAsync("seed_string");

        // Create capability first
        var createRequest = new AddResourceCapabilityRequest(criterion.Id, JsonSerializer.SerializeToElement("test-value"));
        var createResponse = await _client.PostAsJsonAsync(
            $"/api/resources/{resource.Id}/capabilities",
            createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<ResourceCapabilityInfo>();
        Assert.NotNull(created);

        // Act
        var response = await _client.DeleteAsync(
            $"/api/resources/{resource.Id}/capabilities/{created.Id}");

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // Verify it's gone
        var getResponse = await _client.GetAsync($"/api/resources/{resource.Id}/capabilities");
        var capabilities = await getResponse.Content.ReadFromJsonAsync<List<ResourceCapabilityInfo>>();
        Assert.NotNull(capabilities);
        Assert.Empty(capabilities);
    }

    [Fact]
    public async Task DeleteResourceCapability_Returns404_WhenCapabilityNotFound()
    {
        // Arrange
        var resource = await CreatePersonAsync("CapTest-" + Guid.NewGuid().ToString("N")[..20]);
        var nonExistentCapabilityId = Guid.NewGuid();

        // Act
        var response = await _client.DeleteAsync(
            $"/api/resources/{resource.Id}/capabilities/{nonExistentCapabilityId}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AddResourceCapability_ThenGetReturnsIt()
    {
        // Arrange
        var resource = await CreatePersonAsync("CapTest-" + Guid.NewGuid().ToString("N")[..20]);
        var criterion = await GetSeedCriterionAsync("seed_number");

        var request = new AddResourceCapabilityRequest(criterion.Id, JsonSerializer.SerializeToElement(42.5));
        await _client.PostAsJsonAsync(
            $"/api/resources/{resource.Id}/capabilities",
            request);

        // Act
        var response = await _client.GetAsync($"/api/resources/{resource.Id}/capabilities");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var capabilities = await response.Content.ReadFromJsonAsync<List<ResourceCapabilityInfo>>();
        Assert.NotNull(capabilities);
        Assert.Single(capabilities);
        var capability = capabilities[0];
        Assert.Equal(criterion.Id, capability.CriterionId);
        Assert.Equal(42.5, capability.Value.GetDouble());
    }

    [Fact]
    public async Task GetResourceCapabilities_ReturnsCapabilitiesWithCriterionDetails()
    {
        // Arrange
        var resource = await CreatePersonAsync("CapTest-" + Guid.NewGuid().ToString("N")[..20]);
        var criterion = await GetSeedCriterionAsync("seed_number");

        var request = new AddResourceCapabilityRequest(criterion.Id, JsonSerializer.SerializeToElement(100.5));
        await _client.PostAsJsonAsync(
            $"/api/resources/{resource.Id}/capabilities",
            request);

        // Act
        var response = await _client.GetAsync($"/api/resources/{resource.Id}/capabilities");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var capabilities = await response.Content.ReadFromJsonAsync<List<ResourceCapabilityInfo>>();
        Assert.NotNull(capabilities);
        Assert.Single(capabilities);

        var capability = capabilities[0];
        Assert.NotNull(capability.Criterion);
        Assert.Equal(criterion.Name, capability.Criterion.Name);
        Assert.Equal(CriterionDataType.Number, capability.Criterion.DataType);
    }
}
