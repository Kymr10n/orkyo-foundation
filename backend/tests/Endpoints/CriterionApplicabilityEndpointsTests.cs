using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Endpoints;
using Api.Models;
using Xunit;

namespace Orkyo.Foundation.Tests.Endpoints;

[Collection("Database collection")]
public class CriterionApplicabilityEndpointsTests
{
    private readonly HttpClient _client;

    public CriterionApplicabilityEndpointsTests(DatabaseFixture databaseFixture)
    {
        _client = databaseFixture.CreateAuthorizedClient();
    }

    #region GET /api/criteria/{id}/applicability

    [Fact]
    public async Task GetCriterionApplicability_WithValidId_ReturnsOk()
    {
        // Arrange - Create a criterion first
        var createRequest = new CreateCriterionRequest
        {
            Name = $"test_applicability_get_{Guid.NewGuid():N}",
            DataType = CriterionDataType.Boolean,
            ResourceTypeKeys = new List<string> { "space" }
        };
        var createResponse = await _client.PostAsJsonAsync("/api/criteria", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<CriterionInfo>();

        // Act
        var response = await _client.GetAsync($"/api/criteria/{created!.Id}/applicability");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var applicability = await response.Content.ReadFromJsonAsync<CriterionApplicabilityInfo>();
        Assert.NotNull(applicability);
        Assert.Equal(created.Id, applicability.CriterionId);
        Assert.True(applicability.ApplicableToRequests); // Default is true
    }

    [Fact]
    public async Task GetCriterionApplicability_WithInvalidId_ReturnsNotFound()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();

        // Act
        var response = await _client.GetAsync($"/api/criteria/{nonExistentId}/applicability");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    #endregion

    #region PUT /api/criteria/{id}/applicability

    [Fact]
    public async Task UpdateCriterionApplicability_WithValidId_ReturnsOk()
    {
        // Arrange - Create a criterion
        var createRequest = new CreateCriterionRequest
        {
            Name = $"test_applicability_put_{Guid.NewGuid():N}",
            DataType = CriterionDataType.Boolean,
            ResourceTypeKeys = new List<string> { "space" }
        };
        var createResponse = await _client.PostAsJsonAsync("/api/criteria", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<CriterionInfo>();

        var updateRequest = new UpdateCriterionApplicabilityRequest
        {
            ApplicableToRequests = false
        };

        // Act
        var response = await _client.PutAsJsonAsync(
            $"/api/criteria/{created!.Id}/applicability", updateRequest);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<CriterionApplicabilityInfo>();
        Assert.NotNull(updated);
        Assert.False(updated.ApplicableToRequests);
    }

    [Fact]
    public async Task UpdateCriterionApplicability_WithResourceTypeKeys_ReturnsOk()
    {
        // Arrange - Create a criterion
        var createRequest = new CreateCriterionRequest
        {
            Name = $"test_applicability_types_{Guid.NewGuid():N}",
            DataType = CriterionDataType.Number,
            ResourceTypeKeys = new List<string> { "space" }
        };
        var createResponse = await _client.PostAsJsonAsync("/api/criteria", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<CriterionInfo>();

        var updateRequest = new UpdateCriterionApplicabilityRequest
        {
            ResourceTypeKeys = new List<string> { "space" }
        };

        // Act
        var response = await _client.PutAsJsonAsync(
            $"/api/criteria/{created!.Id}/applicability", updateRequest);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<CriterionApplicabilityInfo>();
        Assert.NotNull(updated);
        Assert.Contains("space", updated.ResourceTypeKeys);
    }

    [Fact]
    public async Task UpdateCriterionApplicability_WithInvalidId_ReturnsNotFound()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();
        var updateRequest = new UpdateCriterionApplicabilityRequest
        {
            ApplicableToRequests = false
        };

        // Act
        var response = await _client.PutAsJsonAsync(
            $"/api/criteria/{nonExistentId}/applicability", updateRequest);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateCriterionApplicability_Returns409_WhenRemovingTypeWithAssignments()
    {
        // Spec: removing applicability of a type that still has assignments must
        // be blocked. Otherwise the criterion silently becomes non-applicable to
        // existing assignments, leaving the system inconsistent.
        var createRequest = new CreateCriterionRequest
        {
            Name = $"app_inuse_{Guid.NewGuid():N}",
            DataType = CriterionDataType.Boolean,
            ResourceTypeKeys = new List<string> { "person", "space" },
        };
        var createResponse = await _client.PostAsJsonAsync("/api/criteria", createRequest);
        createResponse.EnsureSuccessStatusCode();
        var criterion = (await createResponse.Content.ReadFromJsonAsync<CriterionInfo>())!;

        var person = await CreatePersonAsync($"AppInUse-{Guid.NewGuid().ToString("N")[..12]}");
        var capRequest = new AddResourceCapabilityRequest(
            criterion.Id, JsonSerializer.SerializeToElement(true));
        var capResponse = await _client.PostAsJsonAsync(
            $"/api/resources/{person.Id}/capabilities", capRequest);
        capResponse.EnsureSuccessStatusCode();

        // Try to drop "person" from applicability while a person assignment exists.
        var update = new UpdateCriterionApplicabilityRequest
        {
            ResourceTypeKeys = new List<string> { "space" },
        };
        var response = await _client.PutAsJsonAsync(
            $"/api/criteria/{criterion.Id}/applicability", update);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        // Applicability still contains "person"
        var getResponse = await _client.GetAsync($"/api/criteria/{criterion.Id}/applicability");
        getResponse.EnsureSuccessStatusCode();
        var current = await getResponse.Content.ReadFromJsonAsync<CriterionApplicabilityInfo>();
        Assert.NotNull(current);
        Assert.Contains("person", current.ResourceTypeKeys);
        Assert.Contains("space", current.ResourceTypeKeys);
    }

    [Fact]
    public async Task UpdateCriterionApplicability_AllowsRemovingType_WhenNoAssignments()
    {
        // Sanity check: removing an applicability type with no assignments succeeds.
        var createRequest = new CreateCriterionRequest
        {
            Name = $"app_free_{Guid.NewGuid():N}",
            DataType = CriterionDataType.Boolean,
            ResourceTypeKeys = new List<string> { "person", "space" },
        };
        var createResponse = await _client.PostAsJsonAsync("/api/criteria", createRequest);
        createResponse.EnsureSuccessStatusCode();
        var criterion = (await createResponse.Content.ReadFromJsonAsync<CriterionInfo>())!;

        var update = new UpdateCriterionApplicabilityRequest
        {
            ResourceTypeKeys = new List<string> { "space" },
        };
        var response = await _client.PutAsJsonAsync(
            $"/api/criteria/{criterion.Id}/applicability", update);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var updated = await response.Content.ReadFromJsonAsync<CriterionApplicabilityInfo>();
        Assert.NotNull(updated);
        Assert.DoesNotContain("person", updated.ResourceTypeKeys);
        Assert.Contains("space", updated.ResourceTypeKeys);
    }

    private async Task<ResourceInfo> CreatePersonAsync(string name)
    {
        var request = new CreateResourceRequest
        {
            ResourceTypeKey = "person",
            Name = name,
            AllocationMode = "Fractional",
            BaseAvailabilityPercent = 100,
        };
        var response = await _client.PostAsJsonAsync("/api/resources", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ResourceInfo>())!;
    }

    #endregion
}
