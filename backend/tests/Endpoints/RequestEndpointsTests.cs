using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Models;
using Xunit;

namespace Orkyo.Foundation.Tests.Endpoints;

/// <summary>
/// Tests for Request CRUD endpoints.
/// Uses unique identifiers to prevent test conflicts.
/// </summary>
[Collection("Database collection")]
public class RequestEndpointsTests
{
    private readonly HttpClient _client;

    public RequestEndpointsTests(DatabaseFixture databaseFixture)
    {
        _client = databaseFixture.CreateAuthorizedClient();
    }

    #region POST /requests - Create Request

    [Fact]
    public async Task CreateRequest_WithValidData_ReturnsCreatedRequest()
    {
        // Arrange
        var spaceId = await TestHelpers.GetOrCreateTestSpace(_client);
        var request = new CreateRequestRequest
        {
            Name = $"Test Request {Guid.NewGuid():N}".Substring(0, 30),
            Description = "Test request for validation",
            SpaceId = spaceId,
            StartTs = DateTime.UtcNow.AddDays(1),
            EndTs = DateTime.UtcNow.AddDays(5),
            MinimalDurationValue = 4,
            MinimalDurationUnit = DurationUnit.Days
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/requests", request);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<RequestInfo>();
        Assert.NotNull(created);
        Assert.NotEqual(Guid.Empty, created.Id);
        Assert.Equal(request.Name, created.Name);
        Assert.Equal(request.Description, created.Description);
        Assert.Equal(request.SpaceId, created.SpaceId);
        Assert.Equal(4, created.MinimalDurationValue);
        Assert.Equal(DurationUnit.Days, created.MinimalDurationUnit);
        Assert.Equal(RequestStatus.Planned, created.Status);
    }

    [Fact]
    public async Task CreateRequest_WithRequirements_CreatesRequestAndRequirements()
    {
        // Arrange
        var spaceId = await TestHelpers.GetOrCreateTestSpace(_client);
        var criteria = await TestHelpers.GetAvailableCriteria(_client);
        var criterion = criteria.FirstOrDefault(c => c.DataType == CriterionDataType.Number);
        Assert.NotNull(criterion);

        var request = new CreateRequestRequest
        {
            Name = $"Request with Reqs {Guid.NewGuid():N}".Substring(0, 30),
            Description = "Request with criterion requirements",
            SpaceId = spaceId,
            StartTs = DateTime.UtcNow.AddDays(1),
            EndTs = DateTime.UtcNow.AddDays(2),
            MinimalDurationValue = 1,
            MinimalDurationUnit = DurationUnit.Days,
            Requirements = new List<CreateRequestRequirementRequest>
            {
                new()
                {
                    CriterionId = criterion.Id,
                    Value = JsonSerializer.SerializeToElement(100.5)
                }
            }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/requests", request);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<RequestInfo>();
        Assert.NotNull(created);
        Assert.NotNull(created.Requirements);
        Assert.Single(created.Requirements);
        Assert.Equal(criterion.Id, created.Requirements[0].CriterionId);
        Assert.Equal(100.5, created.Requirements[0].Value.GetDouble());
    }

    [Fact]
    public async Task CreateRequest_WithInvalidSpaceId_ReturnsBadRequest()
    {
        // Arrange
        var request = new CreateRequestRequest
        {
            Name = "Invalid Request",
            Description = "Space does not exist",
            SpaceId = Guid.NewGuid(), // Non-existent space
            StartTs = DateTime.UtcNow.AddDays(1),
            EndTs = DateTime.UtcNow.AddDays(2),
            MinimalDurationValue = 1,
            MinimalDurationUnit = DurationUnit.Days
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/requests", request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateRequest_WithEndBeforeStart_ReturnsBadRequest()
    {
        // Arrange
        var spaceId = await TestHelpers.GetOrCreateTestSpace(_client);
        var request = new CreateRequestRequest
        {
            Name = "Invalid Time Range",
            Description = "End before start",
            SpaceId = spaceId,
            StartTs = DateTime.UtcNow.AddDays(5),
            EndTs = DateTime.UtcNow.AddDays(1), // Before start
            MinimalDurationValue = 1,
            MinimalDurationUnit = DurationUnit.Days
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/requests", request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateRequest_WithNegativeDuration_ReturnsBadRequest()
    {
        // Arrange
        var spaceId = await TestHelpers.GetOrCreateTestSpace(_client);
        var request = new CreateRequestRequest
        {
            Name = "Invalid Duration",
            Description = "Negative duration",
            SpaceId = spaceId,
            StartTs = DateTime.UtcNow.AddDays(1),
            EndTs = DateTime.UtcNow.AddDays(2),
            MinimalDurationValue = -5, // Negative
            MinimalDurationUnit = DurationUnit.Days
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/requests", request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    #endregion

    #region GET /requests - List Requests

    [Fact]
    public async Task GetRequests_WithoutIncludeRequirements_ReturnsRequestsWithoutRequirements()
    {
        // Arrange
        var spaceId = await TestHelpers.GetOrCreateTestSpace(_client);
        var createRequest = new CreateRequestRequest
        {
            Name = $"List Test {Guid.NewGuid():N}".Substring(0, 30),
            Description = "For listing test",
            SpaceId = spaceId,
            StartTs = DateTime.UtcNow.AddDays(1),
            EndTs = DateTime.UtcNow.AddDays(2),
            MinimalDurationValue = 1,
            MinimalDurationUnit = DurationUnit.Days
        };
        await _client.PostAsJsonAsync("/api/requests", createRequest);

        // Act
        var response = await _client.GetAsync("/api/requests");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var requests = await response.Content.ReadFromJsonAsync<List<RequestInfo>>();
        Assert.NotNull(requests);
        Assert.NotEmpty(requests);

        // Requirements should be null when not requested
        var testRequest = requests.FirstOrDefault(r => r.Name == createRequest.Name);
        Assert.NotNull(testRequest);
        Assert.Null(testRequest.Requirements);
    }

    [Fact]
    public async Task GetRequests_WithIncludeRequirements_ReturnsRequestsWithRequirements()
    {
        // Arrange
        var spaceId = await TestHelpers.GetOrCreateTestSpace(_client);
        var criteria = await TestHelpers.GetAvailableCriteria(_client);
        var criterion = criteria.FirstOrDefault(c => c.DataType == CriterionDataType.Number);
        Assert.NotNull(criterion);

        var createRequest = new CreateRequestRequest
        {
            Name = $"Include Req Test {Guid.NewGuid():N}".Substring(0, 30),
            Description = "For include requirements test",
            SpaceId = spaceId,
            StartTs = DateTime.UtcNow.AddDays(1),
            EndTs = DateTime.UtcNow.AddDays(2),
            MinimalDurationValue = 1,
            MinimalDurationUnit = DurationUnit.Days,
            Requirements = new List<CreateRequestRequirementRequest>
            {
                new() { CriterionId = criterion.Id, Value = JsonSerializer.SerializeToElement(42.0) }
            }
        };
        await _client.PostAsJsonAsync("/api/requests", createRequest);

        // Act
        var response = await _client.GetAsync("/api/requests?includeRequirements=true");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var requests = await response.Content.ReadFromJsonAsync<List<RequestInfo>>();
        Assert.NotNull(requests);

        var testRequest = requests.FirstOrDefault(r => r.Name == createRequest.Name);
        Assert.NotNull(testRequest);
        Assert.NotNull(testRequest.Requirements);
        Assert.Single(testRequest.Requirements);
        Assert.Equal(criterion.Id, testRequest.Requirements[0].CriterionId);
    }

    #endregion

    #region GET /requests/{id} - Get Single Request

    [Fact]
    public async Task GetRequest_WithValidId_ReturnsRequestWithRequirements()
    {
        // Arrange
        var spaceId = await TestHelpers.GetOrCreateTestSpace(_client);
        var createRequest = new CreateRequestRequest
        {
            Name = $"Get Test {Guid.NewGuid():N}".Substring(0, 30),
            Description = "For get single test",
            SpaceId = spaceId,
            StartTs = DateTime.UtcNow.AddDays(1),
            EndTs = DateTime.UtcNow.AddDays(2),
            MinimalDurationValue = 1,
            MinimalDurationUnit = DurationUnit.Days
        };
        var createResponse = await _client.PostAsJsonAsync("/api/requests", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<RequestInfo>();
        Assert.NotNull(created);

        // Act
        var response = await _client.GetAsync($"/api/requests/{created.Id}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var request = await response.Content.ReadFromJsonAsync<RequestInfo>();
        Assert.NotNull(request);
        Assert.Equal(created.Id, request.Id);
        Assert.Equal(createRequest.Name, request.Name);
        Assert.NotNull(request.Requirements); // Always included in single get
    }

    [Fact]
    public async Task GetRequest_WithInvalidId_ReturnsNotFound()
    {
        // Act
        var response = await _client.GetAsync($"/api/requests/{Guid.NewGuid()}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    #endregion

    #region PUT /requests/{id} - Update Request

    [Fact]
    public async Task UpdateRequest_WithValidData_ReturnsUpdatedRequest()
    {
        // Arrange
        var spaceId = await TestHelpers.GetOrCreateTestSpace(_client);
        var createRequest = new CreateRequestRequest
        {
            Name = $"Update Test {Guid.NewGuid():N}".Substring(0, 30),
            Description = "Original description",
            SpaceId = spaceId,
            StartTs = DateTime.UtcNow.AddDays(1),
            EndTs = DateTime.UtcNow.AddDays(2),
            MinimalDurationValue = 1,
            MinimalDurationUnit = DurationUnit.Days
        };
        var createResponse = await _client.PostAsJsonAsync("/api/requests", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<RequestInfo>();
        Assert.NotNull(created);

        var updateRequest = new UpdateRequestRequest
        {
            Name = "Updated Name",
            Description = "Updated description",
            Status = RequestStatus.InProgress
        };

        // Act
        var response = await _client.PutAsJsonAsync($"/api/requests/{created.Id}", updateRequest);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<RequestInfo>();
        Assert.NotNull(updated);
        Assert.Equal("Updated Name", updated.Name);
        Assert.Equal("Updated description", updated.Description);
        Assert.Equal(RequestStatus.InProgress, updated.Status);

        // Unchanged fields should remain
        Assert.Equal(created.SpaceId, updated.SpaceId);
        Assert.Equal(created.MinimalDurationValue, updated.MinimalDurationValue);
    }

    [Fact]
    public async Task UpdateRequest_PartialUpdate_OnlyUpdatesProvidedFields()
    {
        // Arrange
        var spaceId = await TestHelpers.GetOrCreateTestSpace(_client);
        var createRequest = new CreateRequestRequest
        {
            Name = $"Partial Test {Guid.NewGuid():N}".Substring(0, 30),
            Description = "Original",
            SpaceId = spaceId,
            StartTs = DateTime.UtcNow.AddDays(1),
            EndTs = DateTime.UtcNow.AddDays(2),
            MinimalDurationValue = 1,
            MinimalDurationUnit = DurationUnit.Days
        };
        var createResponse = await _client.PostAsJsonAsync("/api/requests", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<RequestInfo>();
        Assert.NotNull(created);

        var updateRequest = new UpdateRequestRequest
        {
            Status = RequestStatus.Done
            // Only updating status
        };

        // Act
        var response = await _client.PutAsJsonAsync($"/api/requests/{created.Id}", updateRequest);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<RequestInfo>();
        Assert.NotNull(updated);
        Assert.Equal(RequestStatus.Done, updated.Status);
        Assert.Equal(created.Name, updated.Name); // Unchanged
        Assert.Equal(created.Description, updated.Description); // Unchanged
    }

    [Fact]
    public async Task UpdateRequest_WithInvalidId_ReturnsNotFound()
    {
        // Arrange
        var updateRequest = new UpdateRequestRequest
        {
            Name = "Won't be updated"
        };

        // Act
        var response = await _client.PutAsJsonAsync($"/api/requests/{Guid.NewGuid()}", updateRequest);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateRequest_WithEndBeforeStart_ReturnsBadRequest()
    {
        // Arrange
        var spaceId = await TestHelpers.GetOrCreateTestSpace(_client);
        var createRequest = new CreateRequestRequest
        {
            Name = $"Invalid Update {Guid.NewGuid():N}".Substring(0, 30),
            Description = "Test",
            SpaceId = spaceId,
            StartTs = DateTime.UtcNow.AddDays(1),
            EndTs = DateTime.UtcNow.AddDays(5),
            MinimalDurationValue = 1,
            MinimalDurationUnit = DurationUnit.Days,
            SchedulingSettingsApply = false // Disable scheduling so EndTs is not engine-computed
        };
        var createResponse = await _client.PostAsJsonAsync("/api/requests", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<RequestInfo>();
        Assert.NotNull(created);

        var updateRequest = new UpdateRequestRequest
        {
            StartTs = DateTime.UtcNow.AddDays(2),
            EndTs = DateTime.UtcNow.AddHours(1) // Before the start date
        };

        // Act
        var response = await _client.PutAsJsonAsync($"/api/requests/{created.Id}", updateRequest);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateRequest_AddRequirements_AddsRequirementsSuccessfully()
    {
        // Arrange - Create request without requirements
        var spaceId = await TestHelpers.GetOrCreateTestSpace(_client);
        var createRequest = new CreateRequestRequest
        {
            Name = $"Add Reqs Test {Guid.NewGuid():N}".Substring(0, 30),
            Description = "Test adding requirements",
            SpaceId = spaceId,
            StartTs = DateTime.UtcNow.AddDays(1),
            EndTs = DateTime.UtcNow.AddDays(2),
            MinimalDurationValue = 1,
            MinimalDurationUnit = DurationUnit.Days
        };
        var createResponse = await _client.PostAsJsonAsync("/api/requests", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<RequestInfo>();
        Assert.NotNull(created);
        Assert.NotNull(created.Requirements);
        Assert.Empty(created.Requirements);

        // Get available criteria
        var criteria = await TestHelpers.GetAvailableCriteria(_client);
        var numberCriterion = criteria.FirstOrDefault(c => c.DataType == CriterionDataType.Number);
        Assert.NotNull(numberCriterion);

        // Act - Update with name change AND add requirements (testing both at once)
        var updateRequest = new UpdateRequestRequest
        {
            Name = "Updated with Requirements",
            Requirements = new List<CreateRequestRequirementRequest>
            {
                new()
                {
                    CriterionId = numberCriterion.Id,
                    Value = JsonSerializer.SerializeToElement(50.0)
                }
            }
        };
        var response = await _client.PutAsJsonAsync($"/api/requests/{created.Id}", updateRequest);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<RequestInfo>();
        Assert.NotNull(updated);
        Assert.Equal("Updated with Requirements", updated.Name);
        Assert.NotNull(updated.Requirements);
        Assert.Single(updated.Requirements!);
        Assert.Equal(numberCriterion.Id, updated.Requirements[0].CriterionId);
        Assert.Equal(50.0, updated.Requirements[0].Value.GetDouble());
    }

    [Fact]
    public async Task UpdateRequest_ModifyExistingRequirements_ReplacesRequirements()
    {
        // Arrange - Create request with one requirement
        var spaceId = await TestHelpers.GetOrCreateTestSpace(_client);
        var criteria = await TestHelpers.GetAvailableCriteria(_client);
        var numberCriterion = criteria.FirstOrDefault(c => c.DataType == CriterionDataType.Number);
        var textCriterion = criteria.FirstOrDefault(c => c.DataType == CriterionDataType.String);
        Assert.NotNull(numberCriterion);
        Assert.NotNull(textCriterion);

        var createRequest = new CreateRequestRequest
        {
            Name = $"Modify Reqs Test {Guid.NewGuid():N}".Substring(0, 30),
            Description = "Test modifying requirements",
            SpaceId = spaceId,
            StartTs = DateTime.UtcNow.AddDays(1),
            EndTs = DateTime.UtcNow.AddDays(2),
            MinimalDurationValue = 1,
            MinimalDurationUnit = DurationUnit.Days,
            Requirements = new List<CreateRequestRequirementRequest>
            {
                new()
                {
                    CriterionId = numberCriterion.Id,
                    Value = JsonSerializer.SerializeToElement(100.0)
                }
            }
        };
        var createResponse = await _client.PostAsJsonAsync("/api/requests", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<RequestInfo>();
        Assert.NotNull(created);
        Assert.NotNull(created.Requirements);
        Assert.Single(created.Requirements!);

        // Act - Update to replace with different requirements
        var updateRequest = new UpdateRequestRequest
        {
            Requirements = new List<CreateRequestRequirementRequest>
            {
                new()
                {
                    CriterionId = textCriterion.Id,
                    Value = JsonSerializer.SerializeToElement("Updated value")
                }
            }
        };
        var response = await _client.PutAsJsonAsync($"/api/requests/{created.Id}", updateRequest);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<RequestInfo>();
        Assert.NotNull(updated);
        Assert.NotNull(updated.Requirements);
        Assert.Single(updated.Requirements!);
        Assert.Equal(textCriterion.Id, updated.Requirements[0].CriterionId);
        Assert.Equal("Updated value", updated.Requirements[0].Value.GetString());
    }

    [Fact]
    public async Task UpdateRequest_RemoveAllRequirements_ClearsRequirements()
    {
        // Arrange - Create request with requirements
        var spaceId = await TestHelpers.GetOrCreateTestSpace(_client);
        var criteria = await TestHelpers.GetAvailableCriteria(_client);
        var numberCriterion = criteria.FirstOrDefault(c => c.DataType == CriterionDataType.Number);
        Assert.NotNull(numberCriterion);

        var createRequest = new CreateRequestRequest
        {
            Name = $"Remove Reqs Test {Guid.NewGuid():N}".Substring(0, 30),
            Description = "Test removing requirements",
            SpaceId = spaceId,
            StartTs = DateTime.UtcNow.AddDays(1),
            EndTs = DateTime.UtcNow.AddDays(2),
            MinimalDurationValue = 1,
            MinimalDurationUnit = DurationUnit.Days,
            Requirements = new List<CreateRequestRequirementRequest>
            {
                new()
                {
                    CriterionId = numberCriterion.Id,
                    Value = JsonSerializer.SerializeToElement(75.0)
                }
            }
        };
        var createResponse = await _client.PostAsJsonAsync("/api/requests", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<RequestInfo>();
        Assert.NotNull(created);
        Assert.NotNull(created.Requirements);
        Assert.Single(created.Requirements!);

        // Act - Update to remove all requirements
        var updateRequest = new UpdateRequestRequest
        {
            Requirements = new List<CreateRequestRequirementRequest>()
        };
        var response = await _client.PutAsJsonAsync($"/api/requests/{created.Id}", updateRequest);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<RequestInfo>();
        Assert.NotNull(updated);
        Assert.NotNull(updated.Requirements);
        Assert.Empty(updated.Requirements);
    }

    [Fact]
    public async Task UpdateRequest_UpdateNameAndRequirements_UpdatesBothSuccessfully()
    {
        // Arrange - Create request with requirements
        var spaceId = await TestHelpers.GetOrCreateTestSpace(_client);
        var criteria = await TestHelpers.GetAvailableCriteria(_client);
        var numberCriterion = criteria.FirstOrDefault(c => c.DataType == CriterionDataType.Number);
        Assert.NotNull(numberCriterion);

        var createRequest = new CreateRequestRequest
        {
            Name = $"Combined Test {Guid.NewGuid():N}".Substring(0, 30),
            Description = "Original description",
            SpaceId = spaceId,
            StartTs = DateTime.UtcNow.AddDays(1),
            EndTs = DateTime.UtcNow.AddDays(2),
            MinimalDurationValue = 1,
            MinimalDurationUnit = DurationUnit.Days,
            Requirements = new List<CreateRequestRequirementRequest>
            {
                new()
                {
                    CriterionId = numberCriterion.Id,
                    Value = JsonSerializer.SerializeToElement(100.0)
                }
            }
        };
        var createResponse = await _client.PostAsJsonAsync("/api/requests", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<RequestInfo>();
        Assert.NotNull(created);
        Assert.NotNull(created.Requirements);
        Assert.Single(created.Requirements!);

        // Act - Update both name and requirements
        var updateRequest = new UpdateRequestRequest
        {
            Name = "Updated Name",
            Description = "Updated description",
            Requirements = new List<CreateRequestRequirementRequest>
            {
                new()
                {
                    CriterionId = numberCriterion.Id,
                    Value = JsonSerializer.SerializeToElement(200.0)
                }
            }
        };
        var response = await _client.PutAsJsonAsync($"/api/requests/{created.Id}", updateRequest);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<RequestInfo>();
        Assert.NotNull(updated);
        Assert.Equal("Updated Name", updated.Name);
        Assert.Equal("Updated description", updated.Description);
        Assert.NotNull(updated.Requirements);
        Assert.Single(updated.Requirements!);
        Assert.Equal(200.0, updated.Requirements[0].Value.GetDouble());
    }

    [Fact]
    public async Task UpdateRequest_WithoutRequirementsField_PreservesExistingRequirements()
    {
        // Arrange - Create request with requirements
        var spaceId = await TestHelpers.GetOrCreateTestSpace(_client);
        var criteria = await TestHelpers.GetAvailableCriteria(_client);
        var numberCriterion = criteria.FirstOrDefault(c => c.DataType == CriterionDataType.Number);
        Assert.NotNull(numberCriterion);

        var createRequest = new CreateRequestRequest
        {
            Name = $"Preserve Reqs Test {Guid.NewGuid():N}".Substring(0, 30),
            Description = "Original",
            SpaceId = spaceId,
            StartTs = DateTime.UtcNow.AddDays(1),
            EndTs = DateTime.UtcNow.AddDays(2),
            MinimalDurationValue = 1,
            MinimalDurationUnit = DurationUnit.Days,
            Requirements = new List<CreateRequestRequirementRequest>
            {
                new()
                {
                    CriterionId = numberCriterion.Id,
                    Value = JsonSerializer.SerializeToElement(100.0)
                }
            }
        };
        var createResponse = await _client.PostAsJsonAsync("/api/requests", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<RequestInfo>();
        Assert.NotNull(created);
        Assert.NotNull(created.Requirements);
        Assert.Single(created.Requirements!);

        // Act - Update name only, don't include requirements field
        var updateRequest = new UpdateRequestRequest
        {
            Name = "Updated Name Only"
            // Not including Requirements field should preserve existing ones
        };
        var response = await _client.PutAsJsonAsync($"/api/requests/{created.Id}", updateRequest);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<RequestInfo>();
        Assert.NotNull(updated);
        Assert.Equal("Updated Name Only", updated.Name);
        Assert.NotNull(updated.Requirements);
        Assert.Single(updated.Requirements!); // Requirements should still be there
        Assert.Equal(numberCriterion.Id, updated.Requirements[0].CriterionId);
        Assert.Equal(100.0, updated.Requirements[0].Value.GetDouble());
    }

    #endregion

    #region DELETE /requests/{id} - Delete Request

    [Fact]
    public async Task DeleteRequest_WithValidId_ReturnsNoContent()
    {
        // Arrange
        var spaceId = await TestHelpers.GetOrCreateTestSpace(_client);
        var createRequest = new CreateRequestRequest
        {
            Name = $"Delete Test {Guid.NewGuid():N}".Substring(0, 30),
            Description = "Will be deleted",
            SpaceId = spaceId,
            StartTs = DateTime.UtcNow.AddDays(1),
            EndTs = DateTime.UtcNow.AddDays(2),
            MinimalDurationValue = 1,
            MinimalDurationUnit = DurationUnit.Days
        };
        var createResponse = await _client.PostAsJsonAsync("/api/requests", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<RequestInfo>();
        Assert.NotNull(created);

        // Act
        var response = await _client.DeleteAsync($"/api/requests/{created.Id}");

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // Verify deletion
        var getResponse = await _client.GetAsync($"/api/requests/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task DeleteRequest_WithInvalidId_ReturnsNotFound()
    {
        // Act
        var response = await _client.DeleteAsync($"/api/requests/{Guid.NewGuid()}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteRequest_AlsoDeletesRequirements()
    {
        // Arrange
        var spaceId = await TestHelpers.GetOrCreateTestSpace(_client);
        var criteria = await TestHelpers.GetAvailableCriteria(_client);
        var criterion = criteria.FirstOrDefault(c => c.DataType == CriterionDataType.Number);
        Assert.NotNull(criterion);

        var createRequest = new CreateRequestRequest
        {
            Name = $"Delete Cascade {Guid.NewGuid():N}".Substring(0, 30),
            Description = "With requirements to cascade",
            SpaceId = spaceId,
            StartTs = DateTime.UtcNow.AddDays(1),
            EndTs = DateTime.UtcNow.AddDays(2),
            MinimalDurationValue = 1,
            MinimalDurationUnit = DurationUnit.Days,
            Requirements = new List<CreateRequestRequirementRequest>
            {
                new() { CriterionId = criterion.Id, Value = JsonSerializer.SerializeToElement(50.0) }
            }
        };
        var createResponse = await _client.PostAsJsonAsync("/api/requests", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<RequestInfo>();
        Assert.NotNull(created);

        // Act
        var response = await _client.DeleteAsync($"/api/requests/{created.Id}");

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // Verify both request and requirements are gone
        var getResponse = await _client.GetAsync($"/api/requests/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    #endregion

    #region POST /requests/{id}/requirements - Add Requirement

    [Fact]
    public async Task AddRequirement_WithValidData_ReturnsCreatedRequirement()
    {
        // Arrange
        var spaceId = await TestHelpers.GetOrCreateTestSpace(_client);
        var criteria = await TestHelpers.GetAvailableCriteria(_client);
        var criterion = criteria.FirstOrDefault(c => c.DataType == CriterionDataType.Boolean);
        Assert.NotNull(criterion);

        var createRequest = new CreateRequestRequest
        {
            Name = $"Add Req Test {Guid.NewGuid():N}".Substring(0, 30),
            Description = "Test",
            SpaceId = spaceId,
            StartTs = DateTime.UtcNow.AddDays(1),
            EndTs = DateTime.UtcNow.AddDays(2),
            MinimalDurationValue = 1,
            MinimalDurationUnit = DurationUnit.Days
        };
        var createResponse = await _client.PostAsJsonAsync("/api/requests", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<RequestInfo>();
        Assert.NotNull(created);

        var requirement = new CreateRequestRequirementRequest
        {
            CriterionId = criterion.Id,
            Value = JsonSerializer.SerializeToElement(true)
        };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/requests/{created.Id}/requirements", requirement);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var addedReq = await response.Content.ReadFromJsonAsync<RequestRequirementInfo>();
        Assert.NotNull(addedReq);
        Assert.NotEqual(Guid.Empty, addedReq.Id);
        Assert.Equal(criterion.Id, addedReq.CriterionId);
        Assert.True(addedReq.Value.GetBoolean());
    }

    [Fact]
    public async Task AddRequirement_WithInvalidRequestId_ReturnsNotFound()
    {
        // Arrange
        var criteria = await TestHelpers.GetAvailableCriteria(_client);
        var criterion = criteria.FirstOrDefault();
        Assert.NotNull(criterion);

        var requirement = new CreateRequestRequirementRequest
        {
            CriterionId = criterion.Id,
            Value = JsonSerializer.SerializeToElement("test")
        };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/requests/{Guid.NewGuid()}/requirements", requirement);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AddRequirement_WithInvalidCriterionId_ReturnsBadRequest()
    {
        // Arrange
        var spaceId = await TestHelpers.GetOrCreateTestSpace(_client);
        var createRequest = new CreateRequestRequest
        {
            Name = $"Invalid Criterion {Guid.NewGuid():N}".Substring(0, 30),
            Description = "Test",
            SpaceId = spaceId,
            StartTs = DateTime.UtcNow.AddDays(1),
            EndTs = DateTime.UtcNow.AddDays(2),
            MinimalDurationValue = 1,
            MinimalDurationUnit = DurationUnit.Days
        };
        var createResponse = await _client.PostAsJsonAsync("/api/requests", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<RequestInfo>();
        Assert.NotNull(created);

        var requirement = new CreateRequestRequirementRequest
        {
            CriterionId = Guid.NewGuid(), // Non-existent criterion
            Value = JsonSerializer.SerializeToElement("test")
        };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/requests/{created.Id}/requirements", requirement);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AddRequirement_UpdatesExistingIfDuplicate()
    {
        // Arrange
        var spaceId = await TestHelpers.GetOrCreateTestSpace(_client);
        var criteria = await TestHelpers.GetAvailableCriteria(_client);
        var criterion = criteria.FirstOrDefault(c => c.DataType == CriterionDataType.Number);
        Assert.NotNull(criterion);

        var createRequest = new CreateRequestRequest
        {
            Name = $"Duplicate Req {Guid.NewGuid():N}".Substring(0, 30),
            Description = "Test",
            SpaceId = spaceId,
            StartTs = DateTime.UtcNow.AddDays(1),
            EndTs = DateTime.UtcNow.AddDays(2),
            MinimalDurationValue = 1,
            MinimalDurationUnit = DurationUnit.Days,
            Requirements = new List<CreateRequestRequirementRequest>
            {
                new() { CriterionId = criterion.Id, Value = JsonSerializer.SerializeToElement(10.0) }
            }
        };
        var createResponse = await _client.PostAsJsonAsync("/api/requests", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<RequestInfo>();
        Assert.NotNull(created);

        var updatedRequirement = new CreateRequestRequirementRequest
        {
            CriterionId = criterion.Id,
            Value = JsonSerializer.SerializeToElement(20.0) // Different value
        };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/requests/{created.Id}/requirements", updatedRequirement);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<RequestRequirementInfo>();
        Assert.NotNull(result);
        Assert.Equal(20.0, result.Value.GetDouble());

        // Verify only one requirement exists
        var getResponse = await _client.GetAsync($"/api/requests/{created.Id}");
        var request = await getResponse.Content.ReadFromJsonAsync<RequestInfo>();
        Assert.NotNull(request);
        Assert.NotNull(request.Requirements);
        Assert.Single(request.Requirements);
        Assert.Equal(20.0, request.Requirements[0].Value.GetDouble());
    }

    #endregion

    #region DELETE /requests/{requestId}/requirements/{requirementId} - Delete Requirement

    [Fact]
    public async Task DeleteRequirement_WithValidIds_ReturnsNoContent()
    {
        // Arrange
        var spaceId = await TestHelpers.GetOrCreateTestSpace(_client);
        var criteria = await TestHelpers.GetAvailableCriteria(_client);
        var criterion = criteria.FirstOrDefault(c => c.DataType == CriterionDataType.Number);
        Assert.NotNull(criterion);

        var createRequest = new CreateRequestRequest
        {
            Name = $"Del Req Test {Guid.NewGuid():N}".Substring(0, 30),
            Description = "Test",
            SpaceId = spaceId,
            StartTs = DateTime.UtcNow.AddDays(1),
            EndTs = DateTime.UtcNow.AddDays(2),
            MinimalDurationValue = 1,
            MinimalDurationUnit = DurationUnit.Days,
            Requirements = new List<CreateRequestRequirementRequest>
            {
                new() { CriterionId = criterion.Id, Value = JsonSerializer.SerializeToElement(42.0) }
            }
        };
        var createResponse = await _client.PostAsJsonAsync("/api/requests", createRequest);
        createResponse.EnsureSuccessStatusCode(); // Add this to get better error message
        var created = await createResponse.Content.ReadFromJsonAsync<RequestInfo>();
        Assert.NotNull(created);
        Assert.NotNull(created.Requirements);
        Assert.Single(created.Requirements);
        var requirementId = created.Requirements[0].Id;

        // Act
        var response = await _client.DeleteAsync($"/api/requests/{created.Id}/requirements/{requirementId}");

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // Verify deletion
        var getResponse = await _client.GetAsync($"/api/requests/{created.Id}");
        var updated = await getResponse.Content.ReadFromJsonAsync<RequestInfo>();
        Assert.NotNull(updated);
        Assert.NotNull(updated.Requirements);
        Assert.Empty(updated.Requirements);
    }

    [Fact]
    public async Task DeleteRequirement_WithInvalidRequestId_ReturnsNotFound()
    {
        // Act
        var response = await _client.DeleteAsync($"/api/requests/{Guid.NewGuid()}/requirements/{Guid.NewGuid()}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteRequirement_WithInvalidRequirementId_ReturnsNotFound()
    {
        // Arrange
        var spaceId = await TestHelpers.GetOrCreateTestSpace(_client);
        var createRequest = new CreateRequestRequest
        {
            Name = $"Invalid Req Del {Guid.NewGuid():N}".Substring(0, 30),
            Description = "Test",
            SpaceId = spaceId,
            StartTs = DateTime.UtcNow.AddDays(1),
            EndTs = DateTime.UtcNow.AddDays(2),
            MinimalDurationValue = 1,
            MinimalDurationUnit = DurationUnit.Days
        };
        var createResponse = await _client.PostAsJsonAsync("/api/requests", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<RequestInfo>();
        Assert.NotNull(created);

        // Act
        var response = await _client.DeleteAsync($"/api/requests/{created.Id}/requirements/{Guid.NewGuid()}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    #endregion

    #region PATCH /requests/{id}/schedule - Schedule/Unschedule Request

    [Fact]
    public async Task ScheduleRequest_WithValidData_SchedulesRequest()
    {
        // Arrange - Create unscheduled request
        var spaceId = await TestHelpers.GetOrCreateTestSpace(_client);
        var createRequest = new CreateRequestRequest
        {
            Name = $"Unscheduled {Guid.NewGuid():N}".Substring(0, 30),
            Description = "Initially unscheduled",
            StartTs = null,
            EndTs = null,
            MinimalDurationValue = 4,
            MinimalDurationUnit = DurationUnit.Hours
        };
        var createResponse = await _client.PostAsJsonAsync("/api/requests", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<RequestInfo>();
        Assert.NotNull(created);
        Assert.Null(created.StartTs);
        Assert.Null(created.EndTs);
        Assert.Null(created.SpaceId);

        // Act - Schedule the request
        var scheduleData = new ScheduleRequestRequest
        {
            SpaceId = spaceId,
            StartTs = DateTime.UtcNow.AddDays(1),
            EndTs = DateTime.UtcNow.AddDays(1).AddHours(4)
        };
        var response = await _client.PatchAsJsonAsync($"/api/requests/{created.Id}/schedule", scheduleData);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var scheduled = await response.Content.ReadFromJsonAsync<RequestInfo>();
        Assert.NotNull(scheduled);
        Assert.Equal(spaceId, scheduled.SpaceId);
        Assert.NotNull(scheduled.StartTs);
        Assert.NotNull(scheduled.EndTs);
        Assert.True(scheduled.EndTs > scheduled.StartTs);
    }

    [Fact]
    public async Task UnscheduleRequest_WithValidData_UnschemulRequest()
    {
        // Arrange - Create scheduled request
        var spaceId = await TestHelpers.GetOrCreateTestSpace(_client);
        var createRequest = new CreateRequestRequest
        {
            Name = $"Scheduled {Guid.NewGuid():N}".Substring(0, 30),
            Description = "Initially scheduled",
            SpaceId = spaceId,
            StartTs = DateTime.UtcNow.AddDays(1),
            EndTs = DateTime.UtcNow.AddDays(1).AddHours(4),
            MinimalDurationValue = 4,
            MinimalDurationUnit = DurationUnit.Hours
        };
        var createResponse = await _client.PostAsJsonAsync("/api/requests", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<RequestInfo>();
        Assert.NotNull(created);
        Assert.NotNull(created.StartTs);
        Assert.NotNull(created.EndTs);
        Assert.Equal(spaceId, created.SpaceId);

        // Act - Unschedule the request
        var unscheduleData = new ScheduleRequestRequest
        {
            SpaceId = null,
            StartTs = null,
            EndTs = null
        };
        var response = await _client.PatchAsJsonAsync($"/api/requests/{created.Id}/schedule", unscheduleData);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var unscheduled = await response.Content.ReadFromJsonAsync<RequestInfo>();
        Assert.NotNull(unscheduled);
        Assert.Null(unscheduled.SpaceId);
        Assert.Null(unscheduled.StartTs);
        Assert.Null(unscheduled.EndTs);
    }

    [Fact]
    public async Task ScheduleRequest_WithInvalidSpaceId_ReturnsBadRequest()
    {
        // Arrange
        var createRequest = new CreateRequestRequest
        {
            Name = $"Test {Guid.NewGuid():N}".Substring(0, 30),
            StartTs = null,
            EndTs = null,
            MinimalDurationValue = 4,
            MinimalDurationUnit = DurationUnit.Hours
        };
        var createResponse = await _client.PostAsJsonAsync("/api/requests", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<RequestInfo>();
        Assert.NotNull(created);

        // Act - Try to schedule with non-existent space
        var scheduleData = new ScheduleRequestRequest
        {
            SpaceId = Guid.NewGuid(),
            StartTs = DateTime.UtcNow.AddDays(1),
            EndTs = DateTime.UtcNow.AddDays(1).AddHours(4)
        };
        var response = await _client.PatchAsJsonAsync($"/api/requests/{created.Id}/schedule", scheduleData);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ScheduleRequest_WithInvalidTimeRange_ReturnsBadRequest()
    {
        // Arrange
        var spaceId = await TestHelpers.GetOrCreateTestSpace(_client);
        var createRequest = new CreateRequestRequest
        {
            Name = $"Test {Guid.NewGuid():N}".Substring(0, 30),
            StartTs = null,
            EndTs = null,
            MinimalDurationValue = 4,
            MinimalDurationUnit = DurationUnit.Hours
        };
        var createResponse = await _client.PostAsJsonAsync("/api/requests", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<RequestInfo>();
        Assert.NotNull(created);

        // Act - Try to schedule with end before start
        var scheduleData = new ScheduleRequestRequest
        {
            SpaceId = spaceId,
            StartTs = DateTime.UtcNow.AddDays(1),
            EndTs = DateTime.UtcNow // End before start
        };
        var response = await _client.PatchAsJsonAsync($"/api/requests/{created.Id}/schedule", scheduleData);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ScheduleRequest_WithPartialData_ReturnsBadRequest()
    {
        // Arrange
        var spaceId = await TestHelpers.GetOrCreateTestSpace(_client);
        var createRequest = new CreateRequestRequest
        {
            Name = $"Test {Guid.NewGuid():N}".Substring(0, 30),
            StartTs = null,
            EndTs = null,
            MinimalDurationValue = 4,
            MinimalDurationUnit = DurationUnit.Hours
        };
        var createResponse = await _client.PostAsJsonAsync("/api/requests", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<RequestInfo>();
        Assert.NotNull(created);

        // Act - Try to schedule with only spaceId (missing times)
        var scheduleData = new ScheduleRequestRequest
        {
            SpaceId = spaceId,
            StartTs = null,
            EndTs = null
        };
        var response = await _client.PatchAsJsonAsync($"/api/requests/{created.Id}/schedule", scheduleData);

        // Assert - Should fail because all must be provided or all null
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ScheduleRequest_NonExistentRequest_ReturnsNotFound()
    {
        // Arrange
        var spaceId = await TestHelpers.GetOrCreateTestSpace(_client);
        var scheduleData = new ScheduleRequestRequest
        {
            SpaceId = spaceId,
            StartTs = DateTime.UtcNow.AddDays(1),
            EndTs = DateTime.UtcNow.AddDays(1).AddHours(4)
        };

        // Act
        var response = await _client.PatchAsJsonAsync($"/api/requests/{Guid.NewGuid()}/schedule", scheduleData);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ScheduleRequest_SummaryRequest_ReturnsBadRequest()
    {
        // Arrange - Create unscheduled summary request
        var createRequest = new CreateRequestRequest
        {
            Name = $"Summary {Guid.NewGuid():N}".Substring(0, 30),
            PlanningMode = PlanningMode.Summary,
            StartTs = null,
            EndTs = null,
            MinimalDurationValue = 4,
            MinimalDurationUnit = DurationUnit.Hours
        };
        var createResponse = await _client.PostAsJsonAsync("/api/requests", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<RequestInfo>();
        Assert.NotNull(created);

        var spaceId = await TestHelpers.GetOrCreateTestSpace(_client);

        // Act - Try to schedule the summary request
        var scheduleData = new ScheduleRequestRequest
        {
            SpaceId = spaceId,
            StartTs = DateTime.UtcNow.AddDays(1),
            EndTs = DateTime.UtcNow.AddDays(1).AddHours(4)
        };
        var response = await _client.PatchAsJsonAsync($"/api/requests/{created.Id}/schedule", scheduleData);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RescheduleRequest_ChangesSpaceAndTime()
    {
        // Arrange - Create scheduled request
        var spaceId1 = await TestHelpers.GetOrCreateTestSpace(_client);
        var spaceId2 = await TestHelpers.GetOrCreateAnotherTestSpace(_client);

        var createRequest = new CreateRequestRequest
        {
            Name = $"Reschedulable {Guid.NewGuid():N}".Substring(0, 30),
            SpaceId = spaceId1,
            StartTs = DateTime.UtcNow.AddDays(1),
            EndTs = DateTime.UtcNow.AddDays(1).AddHours(4),
            MinimalDurationValue = 4,
            MinimalDurationUnit = DurationUnit.Hours
        };
        var createResponse = await _client.PostAsJsonAsync("/api/requests", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<RequestInfo>();
        Assert.NotNull(created);

        // Act - Reschedule to different space and time
        var rescheduleData = new ScheduleRequestRequest
        {
            SpaceId = spaceId2,
            StartTs = DateTime.UtcNow.AddDays(2),
            EndTs = DateTime.UtcNow.AddDays(2).AddHours(4)
        };
        var response = await _client.PatchAsJsonAsync($"/api/requests/{created.Id}/schedule", rescheduleData);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var rescheduled = await response.Content.ReadFromJsonAsync<RequestInfo>();
        Assert.NotNull(rescheduled);
        Assert.Equal(spaceId2, rescheduled.SpaceId);
        Assert.NotEqual(created.StartTs, rescheduled.StartTs);
    }

    [Fact]
    public async Task ResizeRequest_SameSpace_PreservesExplicitEndTs()
    {
        // Arrange — schedule a request on a space
        var spaceId = await TestHelpers.GetOrCreateTestSpace(_client);
        var start = DateTime.UtcNow.AddDays(1);
        var originalEnd = start.AddHours(4);

        var createRequest = new CreateRequestRequest
        {
            Name = $"Resize {Guid.NewGuid():N}"[..30],
            SpaceId = spaceId,
            StartTs = start,
            EndTs = originalEnd,
            MinimalDurationValue = 4,
            MinimalDurationUnit = DurationUnit.Hours
        };
        var createResponse = await _client.PostAsJsonAsync("/api/requests", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<RequestInfo>();
        Assert.NotNull(created);

        // Act — resize on the SAME space (explicit EndTs must be honoured)
        var resizedEnd = start.AddHours(6);
        var resizeData = new ScheduleRequestRequest
        {
            SpaceId = spaceId,
            StartTs = start,
            EndTs = resizedEnd,
        };
        var response = await _client.PatchAsJsonAsync($"/api/requests/{created.Id}/schedule", resizeData);

        // Assert — the returned EndTs must match the explicit value we sent,
        // NOT the scheduling-engine-recalculated value from MinimalDuration.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var resized = await response.Content.ReadFromJsonAsync<RequestInfo>();
        Assert.NotNull(resized);
        Assert.Equal(spaceId, resized.SpaceId);

        // Allow 1-second tolerance for DB round-trip precision
        var endTsDiff = Math.Abs((resized.EndTs!.Value - resizedEnd).TotalSeconds);
        Assert.True(endTsDiff < 1, $"EndTs drifted by {endTsDiff:F1}s — scheduling engine likely overwrote the explicit resize value");
    }

    #endregion

    #region Edge Cases and Data Type Tests

    [Fact]
    public async Task CreateRequest_WithDifferentMinimalDurationUnits_WorksCorrectly()
    {
        // Test all duration units
        var spaceId = await TestHelpers.GetOrCreateTestSpace(_client);
        var units = new[] { DurationUnit.Minutes, DurationUnit.Hours, DurationUnit.Days,
                           DurationUnit.Weeks, DurationUnit.Months, DurationUnit.Years };

        foreach (var unit in units)
        {
            var request = new CreateRequestRequest
            {
                Name = $"Duration {unit} {Guid.NewGuid():N}".Substring(0, 30),
                Description = $"Test {unit}",
                SpaceId = spaceId,
                StartTs = DateTime.UtcNow.AddDays(1),
                EndTs = DateTime.UtcNow.AddDays(10),
                MinimalDurationValue = 5,
                MinimalDurationUnit = unit
            };

            var response = await _client.PostAsJsonAsync("/api/requests", request);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);

            var created = await response.Content.ReadFromJsonAsync<RequestInfo>();
            Assert.NotNull(created);
            Assert.Equal(unit, created.MinimalDurationUnit);
            Assert.Equal(5, created.MinimalDurationValue);
        }
    }

    [Fact]
    public async Task Requirement_WithBooleanCriterion_WorksCorrectly()
    {
        var spaceId = await TestHelpers.GetOrCreateTestSpace(_client);
        var criteria = await TestHelpers.GetAvailableCriteria(_client);
        var criterion = criteria.FirstOrDefault(c => c.DataType == CriterionDataType.Boolean);
        Assert.NotNull(criterion);

        var request = new CreateRequestRequest
        {
            Name = $"Bool Req {Guid.NewGuid():N}".Substring(0, 30),
            Description = "Test",
            SpaceId = spaceId,
            StartTs = DateTime.UtcNow.AddDays(1),
            EndTs = DateTime.UtcNow.AddDays(2),
            MinimalDurationValue = 1,
            MinimalDurationUnit = DurationUnit.Days,
            Requirements = new List<CreateRequestRequirementRequest>
            {
                new() { CriterionId = criterion.Id, Value = JsonSerializer.SerializeToElement(true) }
            }
        };

        var response = await _client.PostAsJsonAsync("/api/requests", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<RequestInfo>();
        Assert.NotNull(created);
        Assert.NotNull(created.Requirements);
        Assert.Single(created.Requirements);
        Assert.True(created.Requirements[0].Value.GetBoolean());
    }

    [Fact]
    public async Task Requirement_WithEnumCriterion_WorksCorrectly()
    {
        var spaceId = await TestHelpers.GetOrCreateTestSpace(_client);
        var criteria = await TestHelpers.GetAvailableCriteria(_client);
        var criterion = criteria.FirstOrDefault(c => c.DataType == CriterionDataType.Enum);
        Assert.NotNull(criterion);
        Assert.NotNull(criterion.EnumValues);
        Assert.NotEmpty(criterion.EnumValues);

        var request = new CreateRequestRequest
        {
            Name = $"Enum Req {Guid.NewGuid():N}".Substring(0, 30),
            Description = "Test",
            SpaceId = spaceId,
            StartTs = DateTime.UtcNow.AddDays(1),
            EndTs = DateTime.UtcNow.AddDays(2),
            MinimalDurationValue = 1,
            MinimalDurationUnit = DurationUnit.Days,
            Requirements = new List<CreateRequestRequirementRequest>
            {
                new() { CriterionId = criterion.Id, Value = JsonSerializer.SerializeToElement(criterion.EnumValues[0]) }
            }
        };

        var response = await _client.PostAsJsonAsync("/api/requests", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<RequestInfo>();
        Assert.NotNull(created);
        Assert.NotNull(created.Requirements);
        Assert.Single(created.Requirements);
        Assert.Equal(criterion.EnumValues[0], created.Requirements[0].Value.GetString());
    }

    #endregion
}
