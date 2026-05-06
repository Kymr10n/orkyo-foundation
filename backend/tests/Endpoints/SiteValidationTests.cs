using System.Net;
using System.Net.Http.Json;
using Api.Models;
using Xunit;

namespace Orkyo.Foundation.Tests.Endpoints;

/// <summary>
/// Tests for Site endpoint validation requirements.
/// Ensures API contract validation catches missing required fields.
/// </summary>
[Collection("Database collection")]
public class SiteValidationTests
{
    private readonly HttpClient _client;

    public SiteValidationTests(DatabaseFixture databaseFixture)
    {
        _client = databaseFixture.CreateAuthorizedClient();
    }

    [Fact]
    public async Task UpdateSite_MissingCodeField_ReturnsBadRequest()
    {
        // Arrange - Create a site first
        var code = $"test-{Guid.NewGuid():N}".Substring(0, 10);
        var createRequest = new { code, name = "Validation Test Site" };
        var createResp = await _client.PostAsJsonAsync("/api/sites", createRequest);
        var site = await createResp.Content.ReadFromJsonAsync<SiteInfo>();

        // Act - Try to update WITHOUT code field (simulating the frontend bug)
        var updateRequestWithoutCode = new { name = "Updated Name" };
        var updateResp = await _client.PutAsJsonAsync($"/api/sites/{site!.Id}", updateRequestWithoutCode);

        // Assert - Should reject the request
        Assert.Equal(HttpStatusCode.BadRequest, updateResp.StatusCode);
    }

    [Fact]
    public async Task UpdateSite_MissingNameField_ReturnsBadRequest()
    {
        // Arrange
        var code = $"test-{Guid.NewGuid():N}".Substring(0, 10);
        var createRequest = new { code, name = "Validation Test Site" };
        var createResp = await _client.PostAsJsonAsync("/api/sites", createRequest);
        var site = await createResp.Content.ReadFromJsonAsync<SiteInfo>();

        // Act - Try to update WITHOUT name field
        var updateRequestWithoutName = new { code };
        var updateResp = await _client.PutAsJsonAsync($"/api/sites/{site!.Id}", updateRequestWithoutName);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, updateResp.StatusCode);
    }

    [Fact]
    public async Task CreateSite_MissingCodeField_ReturnsBadRequest()
    {
        // Act - Try to create site without code
        var request = new { name = "No Code Site" };
        var response = await _client.PostAsJsonAsync("/api/sites", request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateSite_MissingNameField_ReturnsBadRequest()
    {
        // Act
        var code = $"test-{Guid.NewGuid():N}".Substring(0, 10);
        var request = new { code };
        var response = await _client.PostAsJsonAsync("/api/sites", request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
