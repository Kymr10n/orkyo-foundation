using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Api.Endpoints;
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
            DataType = CriterionDataType.Boolean,
            ResourceTypeKeys = new List<string> { "space" }
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
            Unit = "kg",
            ResourceTypeKeys = new List<string> { "space" }
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
            DataType = CriterionDataType.String,
            ResourceTypeKeys = new List<string> { "space" }
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
            EnumValues = new List<string> { "Small", "Medium", "Large" },
            ResourceTypeKeys = new List<string> { "space" }
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
            DataType = CriterionDataType.Boolean,
            ResourceTypeKeys = new List<string> { "space" }
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
            DataType = CriterionDataType.Boolean,
            ResourceTypeKeys = new List<string> { "space" }
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
            EnumValues = null,
            ResourceTypeKeys = new List<string> { "space" }
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
            DataType = CriterionDataType.Boolean,
            ResourceTypeKeys = new List<string> { "space" }
        };
        await _client.PostAsJsonAsync("/api/criteria", request1);

        // Act - Try to create duplicate
        var request2 = new CreateCriterionRequest
        {
            Name = uniqueName,
            DataType = CriterionDataType.Boolean,
            ResourceTypeKeys = new List<string> { "space" }
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
            DataType = CriterionDataType.Boolean,
            ResourceTypeKeys = new List<string> { "space" }
        };
        await _client.PostAsJsonAsync("/api/criteria", request1);

        // Act - Try to create duplicate with different case
        var request2 = new CreateCriterionRequest
        {
            Name = uniqueName.ToUpper(),
            DataType = CriterionDataType.Boolean,
            ResourceTypeKeys = new List<string> { "space" }
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
            DataType = CriterionDataType.Boolean,
            ResourceTypeKeys = new List<string> { "space" }
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
            DataType = CriterionDataType.Boolean,
            ResourceTypeKeys = new List<string> { "space" }
        });

        await _client.PostAsJsonAsync("/api/criteria", new CreateCriterionRequest
        {
            Name = name1,
            DataType = CriterionDataType.Boolean,
            ResourceTypeKeys = new List<string> { "space" }
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
            Unit = "m²",
            ResourceTypeKeys = new List<string> { "space" }
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
            DataType = CriterionDataType.Boolean,
            ResourceTypeKeys = new List<string> { "space" }
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
            Unit = "kg",
            ResourceTypeKeys = new List<string> { "space" }
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
            EnumValues = new List<string> { "A", "B" },
            ResourceTypeKeys = new List<string> { "space" }
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
            DataType = CriterionDataType.Boolean,
            ResourceTypeKeys = new List<string> { "space" }
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
            DataType = CriterionDataType.Boolean,
            ResourceTypeKeys = new List<string> { "space" }
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
            DataType = CriterionDataType.Boolean,
            ResourceTypeKeys = new List<string> { "space" }
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

    [Fact]
    public async Task DeleteCriterion_Returns409_WhenReferencedByResourceAssignment()
    {
        // Spec: deleting a criterion with existing assignments must be blocked.
        // The FKs are ON DELETE CASCADE in schema, so without the repository
        // guard the delete would silently destroy the assignments.
        var criterion = await CreateWithApplicabilityAsync(
            $"del_inuse_{Guid.NewGuid():N}", new[] { "person" });
        var person = await CreatePersonAsync($"DelInUse-{Guid.NewGuid().ToString("N")[..12]}");
        var capRequest = new AddResourceCapabilityRequest(
            criterion.Id, JsonSerializer.SerializeToElement(true));
        var capResponse = await _client.PostAsJsonAsync(
            $"/api/resources/{person.Id}/capabilities", capRequest);
        capResponse.EnsureSuccessStatusCode();

        var response = await _client.DeleteAsync($"/api/criteria/{criterion.Id}");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        // Criterion still exists after the failed delete
        var getResponse = await _client.GetAsync($"/api/criteria/{criterion.Id}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
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
        return (await response.Content.ReadFromJsonAsync<ResourceInfo>(_jsonOptions))!;
    }

    #endregion

    #region GET /criteria?resourceType — filter

    [Fact]
    public async Task GetCriteria_WithResourceTypeFilter_ReturnsOnlyTagged()
    {
        // Strict semantics (post-backfill): no open-world fallback. Only criteria
        // explicitly tagged for the requested resource type appear.
        var spaceOnly = await CreateWithApplicabilityAsync($"rt_space_{Guid.NewGuid():N}", new[] { "space" });
        var personOnly = await CreateWithApplicabilityAsync($"rt_person_{Guid.NewGuid():N}", new[] { "person" });
        var multi = await CreateWithApplicabilityAsync($"rt_multi_{Guid.NewGuid():N}", new[] { "space", "person" });

        var response = await _client.GetAsync("/api/criteria?resourceType=person");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await response.Content.ReadFromJsonAsync<List<CriterionInfo>>(_jsonOptions);
        Assert.NotNull(list);

        var ids = list!.Select(c => c.Id).ToHashSet();
        Assert.Contains(personOnly.Id, ids);
        Assert.Contains(multi.Id, ids);
        Assert.DoesNotContain(spaceOnly.Id, ids);
    }

    [Fact]
    public async Task GetCriteria_WithUnknownResourceType_Returns400()
    {
        var response = await _client.GetAsync("/api/criteria?resourceType=banana");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateCriterion_WithApplicability_PersistsAndReturnsKeys()
    {
        var request = new CreateCriterionRequest
        {
            Name = $"appl_persist_{Guid.NewGuid():N}",
            DataType = CriterionDataType.Boolean,
            ResourceTypeKeys = new List<string> { "space", "tool" },
        };
        var createResp = await _client.PostAsJsonAsync("/api/criteria", request);
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);
        var created = (await createResp.Content.ReadFromJsonAsync<CriterionInfo>(_jsonOptions))!;

        // Keys come back sorted alphabetically per SelectColumns ORDER BY rt.key.
        Assert.Equal(new[] { "space", "tool" }, created.ResourceTypeKeys);

        // Round-trip via GET /api/criteria/{id} returns the same set.
        var getResp = await _client.GetAsync($"/api/criteria/{created.Id}");
        var fetched = (await getResp.Content.ReadFromJsonAsync<CriterionInfo>(_jsonOptions))!;
        Assert.Equal(new[] { "space", "tool" }, fetched.ResourceTypeKeys);
    }

    [Fact]
    public async Task CreateCriterion_WithoutApplicability_Returns400()
    {
        var request = new CreateCriterionRequest
        {
            Name = $"appl_missing_{Guid.NewGuid():N}",
            DataType = CriterionDataType.Boolean,
            // ResourceTypeKeys deliberately omitted
        };
        var resp = await _client.PostAsJsonAsync("/api/criteria", request);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task CreateCriterion_WithEmptyApplicability_Returns400()
    {
        var request = new CreateCriterionRequest
        {
            Name = $"appl_empty_{Guid.NewGuid():N}",
            DataType = CriterionDataType.Boolean,
            ResourceTypeKeys = new List<string>(),
        };
        var resp = await _client.PostAsJsonAsync("/api/criteria", request);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task CreateCriterion_WithUnknownApplicability_Returns400()
    {
        var request = new CreateCriterionRequest
        {
            Name = $"appl_unknown_{Guid.NewGuid():N}",
            DataType = CriterionDataType.Boolean,
            ResourceTypeKeys = new List<string> { "flavor" },
        };
        var resp = await _client.PostAsJsonAsync("/api/criteria", request);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task CreateCriterion_WithDuplicateApplicability_Returns400()
    {
        var request = new CreateCriterionRequest
        {
            Name = $"appl_dup_{Guid.NewGuid():N}",
            DataType = CriterionDataType.Boolean,
            ResourceTypeKeys = new List<string> { "space", "space" },
        };
        var resp = await _client.PostAsJsonAsync("/api/criteria", request);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    private async Task<CriterionInfo> CreateWithApplicabilityAsync(string name, IReadOnlyCollection<string> resourceTypeKeys)
    {
        var create = new CreateCriterionRequest
        {
            Name = name,
            DataType = CriterionDataType.Boolean,
            ResourceTypeKeys = resourceTypeKeys.ToList(),
        };
        var resp = await _client.PostAsJsonAsync("/api/criteria", create);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<CriterionInfo>(_jsonOptions))!;
    }

    #endregion
}
