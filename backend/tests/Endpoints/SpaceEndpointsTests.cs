using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Orkyo.Foundation.Tests.Endpoints;

/// <summary>
/// Tests for Space CRUD endpoints following TDD approach.
/// These tests define the expected behavior before implementation.
/// Uses unique identifiers to prevent test conflicts.
/// </summary>
[Collection("Database collection")]
public class SpaceEndpointsTests
{
    private readonly HttpClient _client;

    public SpaceEndpointsTests(DatabaseFixture databaseFixture)
    {
        _client = databaseFixture.CreateAuthorizedClient();
    }

    #region POST /sites/{siteId}/spaces - Create Space

    [Fact]
    public async Task CreateSpace_WithValidRectangleGeometry_ReturnsCreatedSpace()
    {
        // Arrange
        var siteId = await TestHelpers.GetOrCreateTestSite(_client);
        var uniqueCode = $"A-{Guid.NewGuid():N}".Substring(0, 10);
        var request = new CreateSpaceRequest
        {
            Name = "Test Space A1",
            Code = uniqueCode,
            IsPhysical = true,
            Geometry = new SpaceGeometry
            {
                Type = "rectangle",
                Coordinates = new List<Coordinate>
                {
                    new() { X = 100, Y = 100 },
                    new() { X = 300, Y = 250 }
                }
            },
            Properties = new Dictionary<string, object>
            {
                { "capacity", 10 },
                { "hasWifi", true }
            }
        };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/sites/{siteId}/spaces", request);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var space = await response.Content.ReadFromJsonAsync<SpaceInfo>();
        Assert.NotNull(space);
        Assert.NotEqual(Guid.Empty, space.Id);
        Assert.Equal("Test Space A1", space.Name);
        Assert.Equal(uniqueCode, space.Code);
        Assert.True(space.IsPhysical);
        Assert.NotNull(space.Geometry);
        Assert.Equal("rectangle", space.Geometry.Type);
        Assert.Equal(2, space.Geometry.Coordinates.Count);
    }

    [Fact]
    public async Task CreateSpace_WithValidPolygonGeometry_ReturnsCreatedSpace()
    {
        // Arrange
        var siteId = await TestHelpers.GetOrCreateTestSite(_client);
        var uniqueCode = $"P-{Guid.NewGuid():N}".Substring(0, 10);
        var request = new CreateSpaceRequest
        {
            Name = "Polygon Space",
            Code = uniqueCode,
            IsPhysical = true,
            Geometry = new SpaceGeometry
            {
                Type = "polygon",
                Coordinates = new List<Coordinate>
                {
                    new() { X = 0, Y = 0 },
                    new() { X = 100, Y = 0 },
                    new() { X = 100, Y = 100 },
                    new() { X = 50, Y = 150 },
                    new() { X = 0, Y = 100 }
                }
            }
        };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/sites/{siteId}/spaces", request);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var space = await response.Content.ReadFromJsonAsync<SpaceInfo>();
        Assert.NotNull(space);
        Assert.Equal("polygon", space.Geometry?.Type);
        Assert.Equal(5, space.Geometry?.Coordinates.Count);
    }

    [Fact]
    public async Task CreateSpace_VirtualSpace_NoGeometryRequired()
    {
        // Arrange
        var siteId = await TestHelpers.GetOrCreateTestSite(_client);
        var uniqueCode = $"V-{Guid.NewGuid():N}".Substring(0, 10);
        var request = new CreateSpaceRequest
        {
            Name = "Virtual Storage",
            Code = uniqueCode,
            IsPhysical = false,
            Geometry = null,
            Properties = new Dictionary<string, object> { { "capacity", 100 } }
        };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/sites/{siteId}/spaces", request);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var space = await response.Content.ReadFromJsonAsync<SpaceInfo>();
        Assert.NotNull(space);
        Assert.False(space.IsPhysical);
        Assert.Null(space.Geometry);
    }

    [Fact]
    public async Task CreateSpace_PhysicalSpaceWithoutGeometry_ReturnsBadRequest()
    {
        // Arrange
        var siteId = await TestHelpers.GetOrCreateTestSite(_client);
        var request = new CreateSpaceRequest
        {
            Name = "Invalid Physical Space",
            IsPhysical = true,
            Geometry = null // Physical space must have geometry
        };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/sites/{siteId}/spaces", request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateSpace_WithoutName_ReturnsBadRequest()
    {
        // Arrange
        var siteId = await TestHelpers.GetOrCreateTestSite(_client);
        var request = new CreateSpaceRequest
        {
            Name = "", // Name is required
            IsPhysical = false
        };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/sites/{siteId}/spaces", request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateSpace_DuplicateCode_ReturnsBadRequest()
    {
        // Arrange
        var siteId = await TestHelpers.GetOrCreateTestSite(_client);
        var uniqueCode = $"DUP-{Guid.NewGuid():N}".Substring(0, 10);
        var request1 = new CreateSpaceRequest
        {
            Name = "Space 1",
            Code = uniqueCode,
            IsPhysical = false
        };

        // Create first space
        await _client.PostAsJsonAsync($"/api/sites/{siteId}/spaces", request1);

        // Try to create second space with same code
        var request2 = new CreateSpaceRequest
        {
            Name = "Space 2",
            Code = uniqueCode,
            IsPhysical = false
        };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/sites/{siteId}/spaces", request2);

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task CreateSpace_InvalidGeometryType_ReturnsBadRequest()
    {
        // Arrange
        var siteId = await TestHelpers.GetOrCreateTestSite(_client);
        var request = new CreateSpaceRequest
        {
            Name = "Invalid Geometry",
            IsPhysical = true,
            Geometry = new SpaceGeometry
            {
                Type = "circle", // Only rectangle and polygon are valid
                Coordinates = new List<Coordinate> { new() { X = 0, Y = 0 } }
            }
        };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/sites/{siteId}/spaces", request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateSpace_RectangleWithWrongNumberOfPoints_ReturnsBadRequest()
    {
        // Arrange
        var siteId = await TestHelpers.GetOrCreateTestSite(_client);
        var request = new CreateSpaceRequest
        {
            Name = "Bad Rectangle",
            IsPhysical = true,
            Geometry = new SpaceGeometry
            {
                Type = "rectangle",
                Coordinates = new List<Coordinate> { new() { X = 0, Y = 0 } } // Rectangle needs 2 points
            }
        };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/sites/{siteId}/spaces", request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateSpace_PolygonWithTooFewPoints_ReturnsBadRequest()
    {
        // Arrange
        var siteId = await TestHelpers.GetOrCreateTestSite(_client);
        var request = new CreateSpaceRequest
        {
            Name = "Bad Polygon",
            IsPhysical = true,
            Geometry = new SpaceGeometry
            {
                Type = "polygon",
                Coordinates = new List<Coordinate>
                {
                    new() { X = 0, Y = 0 },
                    new() { X = 100, Y = 0 }
                    // Polygon needs at least 3 points
                }
            }
        };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/sites/{siteId}/spaces", request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    #endregion

    #region GET /sites/{siteId}/spaces - List Spaces

    [Fact]
    public async Task GetSpaces_ReturnsAllSpacesForSite()
    {
        // Arrange
        var siteId = await TestHelpers.GetOrCreateTestSite(_client);

        // Create multiple spaces
        await CreateTestSpace(siteId, "Space 1", $"S1-{Guid.NewGuid():N}".Substring(0, 10));
        await CreateTestSpace(siteId, "Space 2", $"S2-{Guid.NewGuid():N}".Substring(0, 10));
        await CreateTestSpace(siteId, "Space 3", $"S3-{Guid.NewGuid():N}".Substring(0, 10));

        // Act
        var response = await _client.GetAsync($"/api/sites/{siteId}/spaces");

        // Assert
        response.EnsureSuccessStatusCode();
        var spaces = await response.Content.ReadFromJsonAsync<List<SpaceInfo>>();
        Assert.NotNull(spaces);
        Assert.True(spaces.Count >= 3, "Should return at least 3 spaces");
    }

    [Fact]
    public async Task GetSpaces_NonExistentSite_ReturnsEmptyList()
    {
        // Arrange
        var nonExistentSiteId = Guid.NewGuid();

        // Act
        var response = await _client.GetAsync($"/api/sites/{nonExistentSiteId}/spaces");

        // Assert
        response.EnsureSuccessStatusCode();
        var spaces = await response.Content.ReadFromJsonAsync<List<SpaceInfo>>();
        Assert.NotNull(spaces);
        Assert.Empty(spaces);
    }

    #endregion

    #region GET /sites/{siteId}/spaces/{spaceId} - Get Single Space

    [Fact]
    public async Task GetSpace_ExistingSpace_ReturnsSpace()
    {
        // Arrange
        var siteId = await TestHelpers.GetOrCreateTestSite(_client);
        var createdSpace = await CreateTestSpace(siteId, "Test Space", $"T-{Guid.NewGuid():N}".Substring(0, 10));

        // Act
        var response = await _client.GetAsync($"/api/sites/{siteId}/spaces/{createdSpace.Id}");

        // Assert
        response.EnsureSuccessStatusCode();
        var space = await response.Content.ReadFromJsonAsync<SpaceInfo>();
        Assert.NotNull(space);
        Assert.Equal(createdSpace.Id, space.Id);
        Assert.Equal("Test Space", space.Name);
    }

    [Fact]
    public async Task GetSpace_NonExistentSpace_ReturnsNotFound()
    {
        // Arrange
        var siteId = await TestHelpers.GetOrCreateTestSite(_client);
        var nonExistentSpaceId = Guid.NewGuid();

        // Act
        var response = await _client.GetAsync($"/api/sites/{siteId}/spaces/{nonExistentSpaceId}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    #endregion

    #region PUT /sites/{siteId}/spaces/{spaceId} - Update Space

    [Fact]
    public async Task UpdateSpace_ValidUpdate_ReturnsUpdatedSpace()
    {
        // Arrange
        var siteId = await TestHelpers.GetOrCreateTestSite(_client);
        var originalCode = $"O-{Guid.NewGuid():N}".Substring(0, 10);
        var createdSpace = await CreateTestSpace(siteId, "Original Name", originalCode);

        var uniqueCode = $"U-{Guid.NewGuid():N}".Substring(0, 10);
        var updateRequest = new UpdateSpaceRequest
        {
            Name = "Updated Name",
            Code = uniqueCode,
            Geometry = new SpaceGeometry
            {
                Type = "rectangle",
                Coordinates = new List<Coordinate>
                {
                    new() { X = 200, Y = 200 },
                    new() { X = 400, Y = 400 }
                }
            },
            Properties = new Dictionary<string, object> { { "updated", true } }
        };

        // Act
        var response = await _client.PutAsJsonAsync($"/api/sites/{siteId}/spaces/{createdSpace.Id}", updateRequest);

        // Assert
        response.EnsureSuccessStatusCode();
        var updatedSpace = await response.Content.ReadFromJsonAsync<SpaceInfo>();
        Assert.NotNull(updatedSpace);
        Assert.Equal("Updated Name", updatedSpace.Name);
        Assert.Equal(uniqueCode, updatedSpace.Code);
    }

    [Fact]
    public async Task UpdateSpace_NonExistentSpace_ReturnsNotFound()
    {
        // Arrange
        var siteId = await TestHelpers.GetOrCreateTestSite(_client);
        var nonExistentSpaceId = Guid.NewGuid();
        var updateRequest = new UpdateSpaceRequest
        {
            Name = "Updated Name"
        };

        // Act
        var response = await _client.PutAsJsonAsync($"/api/sites/{siteId}/spaces/{nonExistentSpaceId}", updateRequest);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    #endregion

    #region DELETE /sites/{siteId}/spaces/{spaceId} - Delete Space

    [Fact]
    public async Task DeleteSpace_ExistingSpace_ReturnsNoContent()
    {
        // Arrange
        var siteId = await TestHelpers.GetOrCreateTestSite(_client);
        var createdSpace = await CreateTestSpace(siteId, "To Delete", $"D-{Guid.NewGuid():N}".Substring(0, 10));

        // Act
        var response = await _client.DeleteAsync($"/api/sites/{siteId}/spaces/{createdSpace.Id}");

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // Verify space is deleted
        var getResponse = await _client.GetAsync($"/api/sites/{siteId}/spaces/{createdSpace.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task DeleteSpace_NonExistentSpace_ReturnsNotFound()
    {
        // Arrange
        var siteId = await TestHelpers.GetOrCreateTestSite(_client);
        var nonExistentSpaceId = Guid.NewGuid();

        // Act
        var response = await _client.DeleteAsync($"/api/sites/{siteId}/spaces/{nonExistentSpaceId}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    #endregion

    #region Description Field Tests

    [Fact]
    public async Task CreateSpace_WithDescription_StoresDescription()
    {
        // Arrange
        var siteId = await TestHelpers.GetOrCreateTestSite(_client);
        var uniqueCode = $"DESC-{Guid.NewGuid():N}".Substring(0, 10);
        var request = new CreateSpaceRequest
        {
            Name = "Conference Room",
            Code = uniqueCode,
            Description = "Large meeting room with projector and whiteboard",
            IsPhysical = false
        };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/sites/{siteId}/spaces", request);

        // Assert
        response.EnsureSuccessStatusCode();
        var space = await response.Content.ReadFromJsonAsync<SpaceInfo>();
        Assert.NotNull(space);
        Assert.Equal("Large meeting room with projector and whiteboard", space.Description);
    }

    [Fact]
    public async Task CreateSpace_WithoutDescription_DescriptionIsNull()
    {
        // Arrange
        var siteId = await TestHelpers.GetOrCreateTestSite(_client);
        var uniqueCode = $"NODESC-{Guid.NewGuid():N}".Substring(0, 10);
        var request = new CreateSpaceRequest
        {
            Name = "Storage Room",
            Code = uniqueCode,
            IsPhysical = false
        };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/sites/{siteId}/spaces", request);

        // Assert
        response.EnsureSuccessStatusCode();
        var space = await response.Content.ReadFromJsonAsync<SpaceInfo>();
        Assert.NotNull(space);
        Assert.Null(space.Description);
    }

    [Fact]
    public async Task UpdateSpace_AddDescription_UpdatesSuccessfully()
    {
        // Arrange
        var siteId = await TestHelpers.GetOrCreateTestSite(_client);
        var space = await CreateTestSpace(siteId, "Office", $"OFF-{Guid.NewGuid():N}".Substring(0, 10));

        var updateRequest = new UpdateSpaceRequest
        {
            Description = "Open office space with natural lighting"
        };

        // Act
        var response = await _client.PutAsJsonAsync($"/api/sites/{siteId}/spaces/{space.Id}", updateRequest);

        // Assert
        response.EnsureSuccessStatusCode();
        var updated = await response.Content.ReadFromJsonAsync<SpaceInfo>();
        Assert.NotNull(updated);
        Assert.Equal("Open office space with natural lighting", updated.Description);
    }

    [Fact]
    public async Task UpdateSpace_ChangeDescription_UpdatesSuccessfully()
    {
        // Arrange
        var siteId = await TestHelpers.GetOrCreateTestSite(_client);
        var uniqueCode = $"CHG-{Guid.NewGuid():N}".Substring(0, 10);
        var createRequest = new CreateSpaceRequest
        {
            Name = "Lab",
            Code = uniqueCode,
            Description = "Original description",
            IsPhysical = false
        };

        var createResponse = await _client.PostAsJsonAsync($"/api/sites/{siteId}/spaces", createRequest);
        var space = await createResponse.Content.ReadFromJsonAsync<SpaceInfo>();

        var updateRequest = new UpdateSpaceRequest
        {
            Description = "Updated description with more details"
        };

        // Act
        var response = await _client.PutAsJsonAsync($"/api/sites/{siteId}/spaces/{space!.Id}", updateRequest);

        // Assert
        response.EnsureSuccessStatusCode();
        var updated = await response.Content.ReadFromJsonAsync<SpaceInfo>();
        Assert.Equal("Updated description with more details", updated?.Description);
    }

    [Fact]
    public async Task UpdateSpace_ClearDescription_RemovesDescription()
    {
        // Arrange
        var siteId = await TestHelpers.GetOrCreateTestSite(_client);
        var uniqueCode = $"CLR-{Guid.NewGuid():N}".Substring(0, 10);
        var createRequest = new CreateSpaceRequest
        {
            Name = "Workshop",
            Code = uniqueCode,
            Description = "Original description to be cleared",
            IsPhysical = false
        };

        var createResponse = await _client.PostAsJsonAsync($"/api/sites/{siteId}/spaces", createRequest);
        var space = await createResponse.Content.ReadFromJsonAsync<SpaceInfo>();

        var updateRequest = new UpdateSpaceRequest
        {
            Description = "" // Clear the description
        };

        // Act
        var response = await _client.PutAsJsonAsync($"/api/sites/{siteId}/spaces/{space!.Id}", updateRequest);

        // Assert
        response.EnsureSuccessStatusCode();
        var updated = await response.Content.ReadFromJsonAsync<SpaceInfo>();
        Assert.True(string.IsNullOrEmpty(updated?.Description));
    }

    [Fact]
    public async Task UpdateSpace_LongDescription_HandlesCorrectly()
    {
        // Arrange
        var siteId = await TestHelpers.GetOrCreateTestSite(_client);
        var space = await CreateTestSpace(siteId, "Auditorium", $"AUD-{Guid.NewGuid():N}".Substring(0, 10));

        var longDescription = string.Join(" ", Enumerable.Repeat(
            "This is a detailed description of the space with many features and amenities.", 20));

        var updateRequest = new UpdateSpaceRequest
        {
            Description = longDescription
        };

        // Act
        var response = await _client.PutAsJsonAsync($"/api/sites/{siteId}/spaces/{space.Id}", updateRequest);

        // Assert
        response.EnsureSuccessStatusCode();
        var updated = await response.Content.ReadFromJsonAsync<SpaceInfo>();
        Assert.NotNull(updated);
        Assert.Equal(longDescription, updated.Description);
    }

    [Fact]
    public async Task GetSpace_ReturnsDescription()
    {
        // Arrange
        var siteId = await TestHelpers.GetOrCreateTestSite(_client);
        var uniqueCode = $"GET-{Guid.NewGuid():N}".Substring(0, 10);
        var createRequest = new CreateSpaceRequest
        {
            Name = "Cafeteria",
            Code = uniqueCode,
            Description = "Employee dining area with seating for 50",
            IsPhysical = false
        };

        var createResponse = await _client.PostAsJsonAsync($"/api/sites/{siteId}/spaces", createRequest);
        var createdSpace = await createResponse.Content.ReadFromJsonAsync<SpaceInfo>();

        // Act
        var response = await _client.GetAsync($"/api/sites/{siteId}/spaces/{createdSpace!.Id}");

        // Assert
        response.EnsureSuccessStatusCode();
        var space = await response.Content.ReadFromJsonAsync<SpaceInfo>();
        Assert.NotNull(space);
        Assert.Equal("Employee dining area with seating for 50", space.Description);
    }

    [Fact]
    public async Task GetSpaces_ListIncludesDescriptions()
    {
        // Arrange
        var siteId = await TestHelpers.GetOrCreateTestSite(_client);

        // Create spaces with descriptions
        var uniqueCode1 = $"LST1-{Guid.NewGuid():N}".Substring(0, 10);
        var uniqueCode2 = $"LST2-{Guid.NewGuid():N}".Substring(0, 10);

        await _client.PostAsJsonAsync($"/api/sites/{siteId}/spaces", new CreateSpaceRequest
        {
            Name = "Space 1",
            Code = uniqueCode1,
            Description = "Description for space 1",
            IsPhysical = false
        });

        await _client.PostAsJsonAsync($"/api/sites/{siteId}/spaces", new CreateSpaceRequest
        {
            Name = "Space 2",
            Code = uniqueCode2,
            Description = "Description for space 2",
            IsPhysical = false
        });

        // Act
        var response = await _client.GetAsync($"/api/sites/{siteId}/spaces");

        // Assert
        response.EnsureSuccessStatusCode();
        var spaces = await response.Content.ReadFromJsonAsync<List<SpaceInfo>>();
        Assert.NotNull(spaces);

        var space1 = spaces.FirstOrDefault(s => s.Code == uniqueCode1);
        var space2 = spaces.FirstOrDefault(s => s.Code == uniqueCode2);

        Assert.NotNull(space1);
        Assert.NotNull(space2);
        Assert.Equal("Description for space 1", space1.Description);
        Assert.Equal("Description for space 2", space2.Description);
    }

    #endregion

    #region Helper Methods

    private async Task<SpaceInfo> CreateTestSpace(Guid siteId, string name, string code)
    {
        var request = new CreateSpaceRequest
        {
            Name = name,
            Code = code,
            IsPhysical = false
        };

        var response = await _client.PostAsJsonAsync($"/api/sites/{siteId}/spaces", request);
        response.EnsureSuccessStatusCode();
        var space = await response.Content.ReadFromJsonAsync<SpaceInfo>();
        Assert.NotNull(space);
        return space;
    }

    #endregion
}
