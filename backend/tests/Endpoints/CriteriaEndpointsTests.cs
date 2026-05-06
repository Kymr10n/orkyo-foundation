using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Api.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Orkyo.Foundation.Tests.Endpoints;

/// <summary>
/// Tests for Criteria CRUD endpoints.
/// Uses unique names to prevent test conflicts.
/// </summary>
[Collection("Database collection")]
public class CriteriaEndpointsTests
{
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _jsonOptions;

    public CriteriaEndpointsTests(DatabaseFixture databaseFixture)
    {
        _client = databaseFixture.CreateAuthorizedClient();
        // Configure JSON to handle string enums
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };
    }

    #region POST /criteria - Create Criterion

    [Fact]
    public async Task CreateCriterion_WithValidBoolean_ReturnsCreated()
    {
        // Arrange
        var request = new CreateCriterionRequest
        {
            Name = $"test_boolean_{Guid.NewGuid():N}",
            Description = "Test boolean criterion",
            DataType = CriterionDataType.Boolean
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/criteria", request);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var criterion = await response.Content.ReadFromJsonAsync<CriterionInfo>(_jsonOptions);
        Assert.NotNull(criterion);
        Assert.Equal(request.Name, criterion.Name);
        Assert.Equal(request.Description, criterion.Description);
        Assert.Equal(CriterionDataType.Boolean, criterion.DataType);
        Assert.Null(criterion.EnumValues);
        Assert.Null(criterion.Unit);
        Assert.NotEqual(Guid.Empty, criterion.Id);
    }

    [Fact]
    public async Task CreateCriterion_WithValidNumber_ReturnsCreated()
    {
        // Arrange
        var request = new CreateCriterionRequest
        {
            Name = $"test_number_{Guid.NewGuid():N}",
            Description = "Test number criterion",
            DataType = CriterionDataType.Number,
            Unit = "kg"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/criteria", request);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var criterion = await response.Content.ReadFromJsonAsync<CriterionInfo>(_jsonOptions);
        Assert.NotNull(criterion);
        Assert.Equal(request.Name, criterion.Name);
        Assert.Equal(CriterionDataType.Number, criterion.DataType);
        Assert.Equal("kg", criterion.Unit);
    }

    [Fact]
    public async Task CreateCriterion_WithValidString_ReturnsCreated()
    {
        // Arrange
        var request = new CreateCriterionRequest
        {
            Name = $"test_string_{Guid.NewGuid():N}",
            Description = "Test string criterion",
            DataType = CriterionDataType.String
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/criteria", request);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var criterion = await response.Content.ReadFromJsonAsync<CriterionInfo>(_jsonOptions);
        Assert.NotNull(criterion);
        Assert.Equal(CriterionDataType.String, criterion.DataType);
    }

    [Fact]
    public async Task CreateCriterion_WithValidEnum_ReturnsCreated()
    {
        // Arrange
        var request = new CreateCriterionRequest
        {
            Name = $"test_enum_{Guid.NewGuid():N}",
            Description = "Test enum criterion",
            DataType = CriterionDataType.Enum,
            EnumValues = new List<string> { "Small", "Medium", "Large" }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/criteria", request);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var criterion = await response.Content.ReadFromJsonAsync<CriterionInfo>(_jsonOptions);
        Assert.NotNull(criterion);
        Assert.Equal(CriterionDataType.Enum, criterion.DataType);
        Assert.NotNull(criterion.EnumValues);
        Assert.Equal(3, criterion.EnumValues.Count);
        Assert.Contains("Small", criterion.EnumValues);
        Assert.Contains("Medium", criterion.EnumValues);
        Assert.Contains("Large", criterion.EnumValues);
    }

    [Fact]
    public async Task CreateCriterion_WithInvalidName_ReturnsBadRequest()
    {
        // Arrange
        var request = new CreateCriterionRequest
        {
            Name = "invalid name with spaces",
            DataType = CriterionDataType.Boolean
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/criteria", request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateCriterion_WithEmptyName_ReturnsBadRequest()
    {
        // Arrange
        var request = new CreateCriterionRequest
        {
            Name = "",
            DataType = CriterionDataType.Boolean
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/criteria", request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateCriterion_EnumWithoutValues_ReturnsBadRequest()
    {
        // Arrange
        var request = new CreateCriterionRequest
        {
            Name = $"test_enum_{Guid.NewGuid():N}",
            DataType = CriterionDataType.Enum,
            EnumValues = null
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/criteria", request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateCriterion_DuplicateName_ReturnsConflict()
    {
        // Arrange - Create first criterion
        var uniqueName = $"test_duplicate_{Guid.NewGuid():N}";
        var request1 = new CreateCriterionRequest
        {
            Name = uniqueName,
            DataType = CriterionDataType.Boolean
        };
        await _client.PostAsJsonAsync("/api/criteria", request1);

        // Act - Try to create duplicate
        var request2 = new CreateCriterionRequest
        {
            Name = uniqueName,
            DataType = CriterionDataType.Boolean
        };
        var response = await _client.PostAsJsonAsync("/api/criteria", request2);

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task CreateCriterion_DuplicateNameCaseInsensitive_ReturnsConflict()
    {
        // Arrange - Create first criterion
        var uniqueName = $"test_case_{Guid.NewGuid():N}";
        var request1 = new CreateCriterionRequest
        {
            Name = uniqueName.ToLower(),
            DataType = CriterionDataType.Boolean
        };
        await _client.PostAsJsonAsync("/api/criteria", request1);

        // Act - Try to create duplicate with different case
        var request2 = new CreateCriterionRequest
        {
            Name = uniqueName.ToUpper(),
            DataType = CriterionDataType.Boolean
        };
        var response = await _client.PostAsJsonAsync("/api/criteria", request2);

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    #endregion

    #region GET /criteria - List All Criteria

    [Fact]
    public async Task GetAllCriteria_ReturnsOkWithList()
    {
        // Arrange - Create a test criterion
        var request = new CreateCriterionRequest
        {
            Name = $"test_list_{Guid.NewGuid():N}",
            DataType = CriterionDataType.Boolean
        };
        await _client.PostAsJsonAsync("/api/criteria", request);

        // Act
        var response = await _client.GetAsync("/api/criteria");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var criteria = await response.Content.ReadFromJsonAsync<List<CriterionInfo>>(_jsonOptions);
        Assert.NotNull(criteria);
        Assert.NotEmpty(criteria);
    }

    [Fact]
    public async Task GetAllCriteria_ReturnsSortedByName()
    {
        // Arrange - Create multiple criteria
        var name1 = $"aaa_first_{Guid.NewGuid():N}";
        var name2 = $"zzz_last_{Guid.NewGuid():N}";

        await _client.PostAsJsonAsync("/api/criteria", new CreateCriterionRequest
        {
            Name = name2,
            DataType = CriterionDataType.Boolean
        });

        await _client.PostAsJsonAsync("/api/criteria", new CreateCriterionRequest
        {
            Name = name1,
            DataType = CriterionDataType.Boolean
        });

        // Act
        var response = await _client.GetAsync("/api/criteria");

        // Assert
        var criteria = await response.Content.ReadFromJsonAsync<List<CriterionInfo>>(_jsonOptions);
        Assert.NotNull(criteria);

        // Find our test criteria
        var testCriteria = criteria.Where(c => c.Name == name1 || c.Name == name2).ToList();
        Assert.Equal(2, testCriteria.Count);

        var firstIndex = criteria.FindIndex(c => c.Name == name1);
        var secondIndex = criteria.FindIndex(c => c.Name == name2);
        Assert.True(firstIndex < secondIndex, "Criteria should be sorted alphabetically");
    }

    #endregion

    #region GET /criteria/{id} - Get Specific Criterion

    [Fact]
    public async Task GetCriterionById_WithValidId_ReturnsOk()
    {
        // Arrange - Create a criterion
        var createRequest = new CreateCriterionRequest
        {
            Name = $"test_get_{Guid.NewGuid():N}",
            Description = "Test description",
            DataType = CriterionDataType.Number,
            Unit = "m²"
        };
        var createResponse = await _client.PostAsJsonAsync("/api/criteria", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<CriterionInfo>(_jsonOptions);

        // Act
        var response = await _client.GetAsync($"/api/criteria/{created!.Id}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var criterion = await response.Content.ReadFromJsonAsync<CriterionInfo>(_jsonOptions);
        Assert.NotNull(criterion);
        Assert.Equal(created.Id, criterion.Id);
        Assert.Equal(created.Name, criterion.Name);
        Assert.Equal(created.Description, criterion.Description);
        Assert.Equal("m²", criterion.Unit);
    }

    [Fact]
    public async Task GetCriterionById_WithInvalidId_ReturnsNotFound()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();

        // Act
        var response = await _client.GetAsync($"/api/criteria/{nonExistentId}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    #endregion

    #region PUT /criteria/{id} - Update Criterion

    [Fact]
    public async Task UpdateCriterion_WithValidDescription_ReturnsOk()
    {
        // Arrange - Create a criterion
        var createRequest = new CreateCriterionRequest
        {
            Name = $"test_update_{Guid.NewGuid():N}",
            Description = "Original description",
            DataType = CriterionDataType.Boolean
        };
        var createResponse = await _client.PostAsJsonAsync("/api/criteria", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<CriterionInfo>(_jsonOptions);

        // Act
        var updateRequest = new UpdateCriterionRequest
        {
            Description = "Updated description"
        };
        var response = await _client.PutAsJsonAsync($"/api/criteria/{created!.Id}", updateRequest);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<CriterionInfo>(_jsonOptions);
        Assert.NotNull(updated);
        Assert.Equal("Updated description", updated.Description);
        Assert.Equal(created.Name, updated.Name); // Name should remain unchanged
    }

    [Fact]
    public async Task UpdateCriterion_WithValidUnit_ReturnsOk()
    {
        // Arrange - Create a number criterion
        var createRequest = new CreateCriterionRequest
        {
            Name = $"test_update_unit_{Guid.NewGuid():N}",
            DataType = CriterionDataType.Number,
            Unit = "kg"
        };
        var createResponse = await _client.PostAsJsonAsync("/api/criteria", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<CriterionInfo>(_jsonOptions);

        // Act
        var updateRequest = new UpdateCriterionRequest
        {
            Unit = "tons"
        };
        var response = await _client.PutAsJsonAsync($"/api/criteria/{created!.Id}", updateRequest);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<CriterionInfo>(_jsonOptions);
        Assert.NotNull(updated);
        Assert.Equal("tons", updated.Unit);
    }

    [Fact]
    public async Task UpdateCriterion_WithValidEnumValues_ReturnsOk()
    {
        // Arrange - Create an enum criterion
        var createRequest = new CreateCriterionRequest
        {
            Name = $"test_update_enum_{Guid.NewGuid():N}",
            DataType = CriterionDataType.Enum,
            EnumValues = new List<string> { "A", "B" }
        };
        var createResponse = await _client.PostAsJsonAsync("/api/criteria", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<CriterionInfo>(_jsonOptions);

        // Act
        var updateRequest = new UpdateCriterionRequest
        {
            EnumValues = new List<string> { "X", "Y", "Z" }
        };
        var response = await _client.PutAsJsonAsync($"/api/criteria/{created!.Id}", updateRequest);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<CriterionInfo>(_jsonOptions);
        Assert.NotNull(updated);
        Assert.NotNull(updated.EnumValues);
        Assert.Equal(3, updated.EnumValues.Count);
        Assert.Contains("X", updated.EnumValues);
        Assert.Contains("Y", updated.EnumValues);
        Assert.Contains("Z", updated.EnumValues);
    }

    [Fact]
    public async Task UpdateCriterion_EnumValuesOnNonEnumType_ReturnsBadRequest()
    {
        // Arrange - Create a boolean criterion
        var createRequest = new CreateCriterionRequest
        {
            Name = $"test_invalid_enum_{Guid.NewGuid():N}",
            DataType = CriterionDataType.Boolean
        };
        var createResponse = await _client.PostAsJsonAsync("/api/criteria", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<CriterionInfo>(_jsonOptions);

        // Act - Try to set enum values on boolean type
        var updateRequest = new UpdateCriterionRequest
        {
            EnumValues = new List<string> { "true", "false" }
        };
        var response = await _client.PutAsJsonAsync($"/api/criteria/{created!.Id}", updateRequest);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateCriterion_WithNoFields_ReturnsBadRequest()
    {
        // Arrange - Create a criterion
        var createRequest = new CreateCriterionRequest
        {
            Name = $"test_no_fields_{Guid.NewGuid():N}",
            DataType = CriterionDataType.Boolean
        };
        var createResponse = await _client.PostAsJsonAsync("/api/criteria", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<CriterionInfo>(_jsonOptions);

        // Act
        var updateRequest = new UpdateCriterionRequest
        {
            Description = null,
            EnumValues = null,
            Unit = null
        };
        var response = await _client.PutAsJsonAsync($"/api/criteria/{created!.Id}", updateRequest);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateCriterion_WithInvalidId_ReturnsNotFound()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();
        var updateRequest = new UpdateCriterionRequest
        {
            Description = "Updated"
        };

        // Act
        var response = await _client.PutAsJsonAsync($"/api/criteria/{nonExistentId}", updateRequest);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    #endregion

    #region DELETE /criteria/{id} - Delete Criterion

    [Fact]
    public async Task DeleteCriterion_WithValidId_ReturnsOk()
    {
        // Arrange - Create a criterion
        var createRequest = new CreateCriterionRequest
        {
            Name = $"test_delete_{Guid.NewGuid():N}",
            DataType = CriterionDataType.Boolean
        };
        var createResponse = await _client.PostAsJsonAsync("/api/criteria", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<CriterionInfo>(_jsonOptions);

        // Act
        var response = await _client.DeleteAsync($"/api/criteria/{created!.Id}");

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // Verify it's actually deleted
        var getResponse = await _client.GetAsync($"/api/criteria/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task DeleteCriterion_WithInvalidId_ReturnsNotFound()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();

        // Act
        var response = await _client.DeleteAsync($"/api/criteria/{nonExistentId}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    #endregion
}
