using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Models;
using Xunit;

namespace Orkyo.Foundation.Tests.Endpoints;

[Collection("Database collection")]
public class SpaceCapabilityEndpointsTests
{
    private readonly HttpClient _client;

    public SpaceCapabilityEndpointsTests(DatabaseFixture databaseFixture)
    {
        _client = databaseFixture.CreateAuthorizedClient();
    }

    [Fact]
    public async Task GetSpaceCapabilities_ReturnsEmptyList_WhenNoCapabilities()
    {
        // Arrange
        var siteId = await TestHelpers.GetOrCreateTestSite(_client);
        var (spaceId, _) = await CreateTestSpace(siteId, "Test Space");

        // Act
        var response = await _client.GetAsync($"/api/sites/{siteId}/spaces/{spaceId}/capabilities");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var capabilities = await response.Content.ReadFromJsonAsync<List<JsonElement>>();
        Assert.NotNull(capabilities);
        Assert.Empty(capabilities);
    }

    [Fact]
    public async Task GetSpaceCapabilities_Returns404_WhenSpaceNotFound()
    {
        // Arrange
        var siteId = await TestHelpers.GetOrCreateTestSite(_client);
        var nonExistentSpaceId = Guid.NewGuid();

        // Act
        var response = await _client.GetAsync($"/api/sites/{siteId}/spaces/{nonExistentSpaceId}/capabilities");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AddSpaceCapability_CreatesCapability_WithValidData()
    {
        // Arrange
        var siteId = await TestHelpers.GetOrCreateTestSite(_client);
        var (spaceId, _) = await CreateTestSpace(siteId, "Test Space");
        var (criterionId, _) = await CreateTestCriterion("Test Criterion", "Number", "m²");

        var request = new
        {
            criterionId,
            value = 100.5
        };

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/sites/{siteId}/spaces/{spaceId}/capabilities",
            request);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var capability = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(spaceId.ToString(), capability.GetProperty("spaceId").GetString());
        Assert.Equal(criterionId.ToString(), capability.GetProperty("criterionId").GetString());
        Assert.Equal(100.5, capability.GetProperty("value").GetDouble());
    }

    [Fact]
    public async Task AddSpaceCapability_Returns409_WhenDuplicateCapability()
    {
        // Arrange
        var siteId = await TestHelpers.GetOrCreateTestSite(_client);
        var (spaceId, _) = await CreateTestSpace(siteId, "Test Space");
        var (criterionId, _) = await CreateTestCriterion("Test Criterion", "Number", "m²");

        var request = new
        {
            criterionId,
            value = 100.5
        };

        // Add first capability
        await _client.PostAsJsonAsync(
            $"/api/sites/{siteId}/spaces/{spaceId}/capabilities",
            request);

        // Act - try to add duplicate
        var response = await _client.PostAsJsonAsync(
            $"/api/sites/{siteId}/spaces/{spaceId}/capabilities",
            request);

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task AddSpaceCapability_Returns404_WhenSpaceNotFound()
    {
        // Arrange
        var siteId = await TestHelpers.GetOrCreateTestSite(_client);
        var nonExistentSpaceId = Guid.NewGuid();
        var (criterionId, _) = await CreateTestCriterion("Test Criterion", "Number", "m²");

        var request = new
        {
            criterionId,
            value = 100.5
        };

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/sites/{siteId}/spaces/{nonExistentSpaceId}/capabilities",
            request);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AddSpaceCapability_Returns404_WhenCriterionNotFound()
    {
        // Arrange
        var siteId = await TestHelpers.GetOrCreateTestSite(_client);
        var (spaceId, _) = await CreateTestSpace(siteId, "Test Space");
        var nonExistentCriterionId = Guid.NewGuid();

        var request = new
        {
            criterionId = nonExistentCriterionId,
            value = 100.5
        };

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/sites/{siteId}/spaces/{spaceId}/capabilities",
            request);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateSpaceCapability_UpdatesValue_WithValidData()
    {
        // Arrange
        var siteId = await TestHelpers.GetOrCreateTestSite(_client);
        var (spaceId, _) = await CreateTestSpace(siteId, "Test Space");
        var (criterionId, _) = await CreateTestCriterion("Test Criterion", "Number", "m²");

        var createRequest = new
        {
            criterionId,
            value = 100.5
        };

        var createResponse = await _client.PostAsJsonAsync(
            $"/api/sites/{siteId}/spaces/{spaceId}/capabilities",
            createRequest);
        var capability = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var capabilityId = capability.GetProperty("id").GetGuid();

        var updateRequest = new
        {
            value = 200.75
        };

        // Act
        var response = await _client.PutAsJsonAsync(
            $"/api/sites/{siteId}/spaces/{spaceId}/capabilities/{capabilityId}",
            updateRequest);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(200.75, updated.GetProperty("value").GetDouble());
    }

    [Fact]
    public async Task UpdateSpaceCapability_Returns404_WhenCapabilityNotFound()
    {
        // Arrange
        var siteId = await TestHelpers.GetOrCreateTestSite(_client);
        var (spaceId, _) = await CreateTestSpace(siteId, "Test Space");
        var nonExistentCapabilityId = Guid.NewGuid();

        var request = new
        {
            value = 200.75
        };

        // Act
        var response = await _client.PutAsJsonAsync(
            $"/api/sites/{siteId}/spaces/{spaceId}/capabilities/{nonExistentCapabilityId}",
            request);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteSpaceCapability_RemovesCapability_WhenExists()
    {
        // Arrange
        var siteId = await TestHelpers.GetOrCreateTestSite(_client);
        var (spaceId, _) = await CreateTestSpace(siteId, "Test Space");
        var (criterionId, _) = await CreateTestCriterion("Test Criterion", "Number", "m²");

        var createRequest = new
        {
            criterionId,
            value = 100.5
        };

        var createResponse = await _client.PostAsJsonAsync(
            $"/api/sites/{siteId}/spaces/{spaceId}/capabilities",
            createRequest);
        var capability = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var capabilityId = capability.GetProperty("id").GetGuid();

        // Act
        var response = await _client.DeleteAsync(
            $"/api/sites/{siteId}/spaces/{spaceId}/capabilities/{capabilityId}");

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // Verify it's gone
        var getResponse = await _client.GetAsync($"/api/sites/{siteId}/spaces/{spaceId}/capabilities");
        var capabilities = await getResponse.Content.ReadFromJsonAsync<List<JsonElement>>();
        Assert.NotNull(capabilities);
        Assert.Empty(capabilities);
    }

    [Fact]
    public async Task DeleteSpaceCapability_Returns404_WhenCapabilityNotFound()
    {
        // Arrange
        var siteId = await TestHelpers.GetOrCreateTestSite(_client);
        var (spaceId, _) = await CreateTestSpace(siteId, "Test Space");
        var nonExistentCapabilityId = Guid.NewGuid();

        // Act
        var response = await _client.DeleteAsync(
            $"/api/sites/{siteId}/spaces/{spaceId}/capabilities/{nonExistentCapabilityId}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetSpaceCapabilities_ReturnsCapabilitiesWithCriterionDetails()
    {
        // Arrange
        var siteId = await TestHelpers.GetOrCreateTestSite(_client);
        var (spaceId, _) = await CreateTestSpace(siteId, "Test Space");
        var (criterionId, criterionName) = await CreateTestCriterion("Area", "Number", "m²");

        var createRequest = new
        {
            criterionId,
            value = 100.5
        };

        await _client.PostAsJsonAsync(
            $"/api/sites/{siteId}/spaces/{spaceId}/capabilities",
            createRequest);

        // Act
        var response = await _client.GetAsync($"/api/sites/{siteId}/spaces/{spaceId}/capabilities");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var capabilities = await response.Content.ReadFromJsonAsync<List<JsonElement>>();
        Assert.NotNull(capabilities);
        Assert.Single(capabilities);

        var capability = capabilities[0];
        Assert.Equal(criterionName, capability.GetProperty("criterion").GetProperty("name").GetString());
        Assert.Equal("Number", capability.GetProperty("criterion").GetProperty("dataType").GetString());
        Assert.Equal("m²", capability.GetProperty("criterion").GetProperty("unit").GetString());
    }

    [Fact]
    public async Task AddSpaceCapability_HandlesNumberType()
    {
        // Arrange
        var siteId = await TestHelpers.GetOrCreateTestSite(_client);
        var (spaceId, _) = await CreateTestSpace(siteId, "Test Space");
        var (criterionId, _) = await CreateTestCriterion("Area", "Number", "m²");

        var request = new
        {
            criterionId,
            value = 42.5
        };

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/sites/{siteId}/spaces/{spaceId}/capabilities",
            request);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var capability = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(42.5, capability.GetProperty("value").GetDouble());
    }

    [Fact]
    public async Task AddSpaceCapability_HandlesBooleanType()
    {
        // Arrange
        var siteId = await TestHelpers.GetOrCreateTestSite(_client);
        var (spaceId, _) = await CreateTestSpace(siteId, "Test Space");
        var (criterionId, _) = await CreateTestCriterion("Has WiFi", "Boolean", null);

        var request = new
        {
            criterionId,
            value = true
        };

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/sites/{siteId}/spaces/{spaceId}/capabilities",
            request);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var capability = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(capability.GetProperty("value").GetBoolean());
    }

    [Fact]
    public async Task AddSpaceCapability_HandlesStringType()
    {
        // Arrange
        var siteId = await TestHelpers.GetOrCreateTestSite(_client);
        var (spaceId, _) = await CreateTestSpace(siteId, "Test Space");
        var (criterionId, _) = await CreateTestCriterion("Department", "String", null);

        var request = new
        {
            criterionId,
            value = "Engineering"
        };

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/sites/{siteId}/spaces/{spaceId}/capabilities",
            request);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var capability = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Engineering", capability.GetProperty("value").GetString());
    }

    [Fact]
    public async Task GetSpaceCapabilities_ReturnsMultipleCapabilities()
    {
        // Arrange
        var siteId = await TestHelpers.GetOrCreateTestSite(_client);
        var (spaceId, _) = await CreateTestSpace(siteId, "Test Space");
        var (criterion1Id, _) = await CreateTestCriterion("Area", "Number", "m²");
        var (criterion2Id, _) = await CreateTestCriterion("Has WiFi", "Boolean", null);
        var (criterion3Id, _) = await CreateTestCriterion("Department", "String", null);

        await _client.PostAsJsonAsync(
            $"/api/sites/{siteId}/spaces/{spaceId}/capabilities",
            new { criterionId = criterion1Id, value = 100.5 });
        await _client.PostAsJsonAsync(
            $"/api/sites/{siteId}/spaces/{spaceId}/capabilities",
            new { criterionId = criterion2Id, value = true });
        await _client.PostAsJsonAsync(
            $"/api/sites/{siteId}/spaces/{spaceId}/capabilities",
            new { criterionId = criterion3Id, value = "Engineering" });

        // Act
        var response = await _client.GetAsync($"/api/sites/{siteId}/spaces/{spaceId}/capabilities");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var capabilities = await response.Content.ReadFromJsonAsync<List<JsonElement>>();
        Assert.NotNull(capabilities);
        Assert.Equal(3, capabilities.Count);
    }

    private async Task<(Guid spaceId, string spaceName)> CreateTestSpace(Guid siteId, string name)
    {
        // Make name unique to avoid conflicts in parallel test execution
        var uniqueName = $"{name}_{Guid.NewGuid():N}".Substring(0, Math.Min(100, name.Length + 33));

        var spaceRequest = new
        {
            name = uniqueName,
            isPhysical = false
        };

        var response = await _client.PostAsJsonAsync($"/api/sites/{siteId}/spaces", spaceRequest);
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            throw new Exception($"Failed to create space. Status: {response.StatusCode}, Response: {errorContent}");
        }

        var space = await response.Content.ReadFromJsonAsync<JsonElement>();

        // Try both lowercase and PascalCase property names
        Guid spaceId;
        if (space.TryGetProperty("id", out var idProperty))
        {
            spaceId = idProperty.GetGuid();
        }
        else if (space.TryGetProperty("Id", out idProperty))
        {
            spaceId = idProperty.GetGuid();
        }
        else
        {
            throw new Exception($"Space response missing 'id' property. Available properties: {string.Join(", ", space.EnumerateObject().Select(p => p.Name))}");
        }

        return (spaceId, name);
    }

    private async Task<(Guid criterionId, string criterionName)> CreateTestCriterion(string name, string dataType, string? unit)
    {
        // Replace spaces and special chars with underscores for valid criterion names
        var safeName = name.Replace(" ", "_").Replace("-", "_");
        var criterionRequest = new
        {
            name = $"{safeName}_{Guid.NewGuid():N}",
            dataType,
            unit
        };

        var response = await _client.PostAsJsonAsync("/api/criteria", criterionRequest);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            throw new Exception($"Failed to create criterion: {response.StatusCode} - {errorContent}");
        }

        var criterion = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (criterion.GetProperty("id").GetGuid(), criterionRequest.name);
    }
}
