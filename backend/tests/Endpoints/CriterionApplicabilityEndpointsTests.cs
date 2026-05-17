using System.Net;
using System.Net.Http.Json;
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

    #endregion
}
