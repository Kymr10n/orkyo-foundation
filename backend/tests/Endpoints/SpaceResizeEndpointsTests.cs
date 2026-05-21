using System.Net;
using System.Net.Http.Json;
using Api.Models;

namespace Orkyo.Foundation.Tests.Endpoints;

/// <summary>
/// Integration tests for space resize operations via the API.
/// Tests the complete workflow of resizing spaces through PUT /sites/{siteId}/spaces/{spaceId}
/// </summary>
[Collection("Database collection")]
public class SpaceResizeEndpointsTests
{
    private readonly HttpClient _client;

    public SpaceResizeEndpointsTests(DatabaseFixture databaseFixture)
    {
        _client = databaseFixture.CreateAuthorizedClient();
    }

    #region Rectangle Resize Tests

    [Fact]
    public async Task ResizeRectangle_EnlargeByDraggingCorner_UpdatesSuccessfully()
    {
        // Arrange - Create a rectangle space
        var siteId = await TestHelpers.GetOrCreateTestSite(_client);
        var space = await CreateRectangleSpace(siteId, "Room A", 100, 100, 300, 300);

        // Act - Resize by moving bottom-right corner
        var resizeRequest = new UpdateSpaceRequest
        {
            Geometry = new SpaceGeometry
            {
                Type = "rectangle",
                Coordinates = new List<Coordinate>
                {
                    new() { X = 100, Y = 100 },
                    new() { X = 400, Y = 400 } // Enlarged from 300,300
                }
            }
        };

        var response = await _client.PutAsJsonAsync(
            $"/api/sites/{siteId}/spaces/{space.Id}",
            resizeRequest);

        // Assert
        response.EnsureSuccessStatusCode();
        var updated = await response.Content.ReadFromJsonAsync<SpaceInfo>();
        Assert.NotNull(updated);
        Assert.Equal("rectangle", updated.Geometry?.Type);
        Assert.Equal(400, updated.Geometry?.Coordinates[1].X);
        Assert.Equal(400, updated.Geometry?.Coordinates[1].Y);
    }

    [Fact]
    public async Task ResizeRectangle_ShrinkByDraggingCorner_UpdatesSuccessfully()
    {
        // Arrange
        var siteId = await TestHelpers.GetOrCreateTestSite(_client);
        var space = await CreateRectangleSpace(siteId, "Room B", 0, 0, 500, 500);

        // Act - Shrink rectangle
        var resizeRequest = new UpdateSpaceRequest
        {
            Geometry = new SpaceGeometry
            {
                Type = "rectangle",
                Coordinates = new List<Coordinate>
                {
                    new() { X = 0, Y = 0 },
                    new() { X = 250, Y = 250 } // Shrunk from 500,500
                }
            }
        };

        var response = await _client.PutAsJsonAsync(
            $"/api/sites/{siteId}/spaces/{space.Id}",
            resizeRequest);

        // Assert
        response.EnsureSuccessStatusCode();
        var updated = await response.Content.ReadFromJsonAsync<SpaceInfo>();
        Assert.NotNull(updated);
        Assert.Equal(250, updated.Geometry?.Coordinates[1].X);
    }

    [Fact]
    public async Task ResizeRectangle_MoveTopLeftCorner_UpdatesSuccessfully()
    {
        // Arrange
        var siteId = await TestHelpers.GetOrCreateTestSite(_client);
        var space = await CreateRectangleSpace(siteId, "Room C", 100, 100, 300, 300);

        // Act - Resize by moving top-left corner
        var resizeRequest = new UpdateSpaceRequest
        {
            Geometry = new SpaceGeometry
            {
                Type = "rectangle",
                Coordinates = new List<Coordinate>
                {
                    new() { X = 50, Y = 50 },   // Moved from 100,100
                    new() { X = 300, Y = 300 }
                }
            }
        };

        var response = await _client.PutAsJsonAsync(
            $"/api/sites/{siteId}/spaces/{space.Id}",
            resizeRequest);

        // Assert
        response.EnsureSuccessStatusCode();
        var updated = await response.Content.ReadFromJsonAsync<SpaceInfo>();
        Assert.NotNull(updated);
        Assert.Equal(50, updated.Geometry?.Coordinates[0].X);
        Assert.Equal(50, updated.Geometry?.Coordinates[0].Y);
    }

    #endregion

    #region Polygon Resize Tests

    [Fact]
    public async Task ResizePolygon_MoveOneVertex_UpdatesSuccessfully()
    {
        // Arrange - Create triangle
        var siteId = await TestHelpers.GetOrCreateTestSite(_client);
        var space = await CreatePolygonSpace(siteId, "Triangle Area", new[]
        {
            (0.0, 0.0),
            (100.0, 0.0),
            (50.0, 100.0)
        });

        // Act - Move the right vertex
        var resizeRequest = new UpdateSpaceRequest
        {
            Geometry = new SpaceGeometry
            {
                Type = "polygon",
                Coordinates = new List<Coordinate>
                {
                    new() { X = 0, Y = 0 },
                    new() { X = 150, Y = 0 },    // Moved from 100,0
                    new() { X = 50, Y = 100 }
                }
            }
        };

        var response = await _client.PutAsJsonAsync(
            $"/api/sites/{siteId}/spaces/{space.Id}",
            resizeRequest);

        // Assert
        response.EnsureSuccessStatusCode();
        var updated = await response.Content.ReadFromJsonAsync<SpaceInfo>();
        Assert.NotNull(updated);
        Assert.Equal(3, updated.Geometry?.Coordinates.Count);
        Assert.Equal(150, updated.Geometry?.Coordinates[1].X);
    }

    [Fact]
    public async Task ResizePolygon_MoveMultipleVertices_UpdatesSuccessfully()
    {
        // Arrange - Create pentagon
        var siteId = await TestHelpers.GetOrCreateTestSite(_client);
        var space = await CreatePolygonSpace(siteId, "Pentagon", new[]
        {
            (50.0, 0.0),
            (100.0, 38.0),
            (81.0, 95.0),
            (19.0, 95.0),
            (0.0, 38.0)
        });

        // Act - Move two vertices
        var resizeRequest = new UpdateSpaceRequest
        {
            Geometry = new SpaceGeometry
            {
                Type = "polygon",
                Coordinates = new List<Coordinate>
                {
                    new() { X = 50, Y = 0 },
                    new() { X = 120, Y = 40 },   // Moved
                    new() { X = 90, Y = 100 },   // Moved
                    new() { X = 19, Y = 95 },
                    new() { X = 0, Y = 38 }
                }
            }
        };

        var response = await _client.PutAsJsonAsync(
            $"/api/sites/{siteId}/spaces/{space.Id}",
            resizeRequest);

        // Assert
        response.EnsureSuccessStatusCode();
        var updated = await response.Content.ReadFromJsonAsync<SpaceInfo>();
        Assert.NotNull(updated);
        Assert.Equal(5, updated.Geometry?.Coordinates.Count);
    }

    #endregion

    #region Edge Cases and Validation

    [Fact]
    public async Task ResizeSpace_WithInvalidGeometry_ReturnsBadRequest()
    {
        // Arrange
        var siteId = await TestHelpers.GetOrCreateTestSite(_client);
        var space = await CreateRectangleSpace(siteId, "Room D", 0, 0, 100, 100);

        // Act - Try to resize with invalid geometry (only 1 coordinate)
        var invalidRequest = new UpdateSpaceRequest
        {
            Geometry = new SpaceGeometry
            {
                Type = "rectangle",
                Coordinates = new List<Coordinate>
                {
                    new() { X = 0, Y = 0 }  // Missing second coordinate
                }
            }
        };

        var response = await _client.PutAsJsonAsync(
            $"/api/sites/{siteId}/spaces/{space.Id}",
            invalidRequest);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ResizeSpace_ToZeroArea_IsAllowed()
    {
        // Arrange
        var siteId = await TestHelpers.GetOrCreateTestSite(_client);
        var space = await CreateRectangleSpace(siteId, "Room E", 0, 0, 100, 100);

        // Act - Resize to zero area (same point)
        var request = new UpdateSpaceRequest
        {
            Geometry = new SpaceGeometry
            {
                Type = "rectangle",
                Coordinates = new List<Coordinate>
                {
                    new() { X = 50, Y = 50 },
                    new() { X = 50, Y = 50 }  // Zero area
                }
            }
        };

        var response = await _client.PutAsJsonAsync(
            $"/api/sites/{siteId}/spaces/{space.Id}",
            request);

        // Assert - Should succeed (zero area is technically valid)
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task ResizeSpace_WithNegativeCoordinates_IsAllowed()
    {
        // Arrange
        var siteId = await TestHelpers.GetOrCreateTestSite(_client);
        var space = await CreateRectangleSpace(siteId, "Room F", 0, 0, 100, 100);

        // Act - Resize with negative coordinates
        var request = new UpdateSpaceRequest
        {
            Geometry = new SpaceGeometry
            {
                Type = "rectangle",
                Coordinates = new List<Coordinate>
                {
                    new() { X = -50, Y = -50 },
                    new() { X = 100, Y = 100 }
                }
            }
        };

        var response = await _client.PutAsJsonAsync(
            $"/api/sites/{siteId}/spaces/{space.Id}",
            request);

        // Assert
        response.EnsureSuccessStatusCode();
        var updated = await response.Content.ReadFromJsonAsync<SpaceInfo>();
        Assert.Equal(-50, updated?.Geometry?.Coordinates[0].X);
    }

    [Fact]
    public async Task ResizeSpace_PreservesOtherFields_WhenOnlyGeometryChanged()
    {
        // Arrange
        var siteId = await TestHelpers.GetOrCreateTestSite(_client);
        var originalCode = $"PRESERVE-{Guid.NewGuid():N}".Substring(0, 15);
        var space = await CreateRectangleSpace(siteId, "Original Name", 0, 0, 100, 100, originalCode);

        // Act - Update only geometry
        var request = new UpdateSpaceRequest
        {
            Geometry = new SpaceGeometry
            {
                Type = "rectangle",
                Coordinates = new List<Coordinate>
                {
                    new() { X = 0, Y = 0 },
                    new() { X = 200, Y = 200 }
                }
            }
        };

        var response = await _client.PutAsJsonAsync(
            $"/api/sites/{siteId}/spaces/{space.Id}",
            request);

        // Assert - Other fields should remain unchanged
        response.EnsureSuccessStatusCode();
        var updated = await response.Content.ReadFromJsonAsync<SpaceInfo>();
        Assert.Equal("Original Name", updated?.Name);
        Assert.Equal(originalCode, updated?.Code);
        Assert.Equal(200, updated?.Geometry?.Coordinates[1].X);
    }

    #endregion

    #region Concurrent Resize Tests

    [Fact]
    public async Task ResizeSpace_MultipleSequentialResizes_AllSucceed()
    {
        // Arrange
        var siteId = await TestHelpers.GetOrCreateTestSite(_client);
        var space = await CreateRectangleSpace(siteId, "Room G", 0, 0, 100, 100);

        // Act - Perform multiple resizes in sequence
        var sizes = new[] { 150, 200, 250, 300 };
        SpaceInfo? lastUpdate = null;

        foreach (var size in sizes)
        {
            var request = new UpdateSpaceRequest
            {
                Geometry = new SpaceGeometry
                {
                    Type = "rectangle",
                    Coordinates = new List<Coordinate>
                    {
                        new() { X = 0, Y = 0 },
                        new() { X = size, Y = size }
                    }
                }
            };

            var response = await _client.PutAsJsonAsync(
                $"/api/sites/{siteId}/spaces/{space.Id}",
                request);

            response.EnsureSuccessStatusCode();
            lastUpdate = await response.Content.ReadFromJsonAsync<SpaceInfo>();
        }

        // Assert - Final size should be 300
        Assert.NotNull(lastUpdate);
        Assert.Equal(300, lastUpdate.Geometry?.Coordinates[1].X);
    }

    #endregion

    #region Description Field Tests

    [Fact]
    public async Task UpdateSpace_WithDescriptionAndResize_BothUpdate()
    {
        // Arrange
        var siteId = await TestHelpers.GetOrCreateTestSite(_client);
        var space = await CreateRectangleSpace(siteId, "Room H", 0, 0, 100, 100);

        // Act - Update both description and geometry
        var request = new UpdateSpaceRequest
        {
            Description = "This room was resized to accommodate more people",
            Geometry = new SpaceGeometry
            {
                Type = "rectangle",
                Coordinates = new List<Coordinate>
                {
                    new() { X = 0, Y = 0 },
                    new() { X = 200, Y = 200 }
                }
            }
        };

        var response = await _client.PutAsJsonAsync(
            $"/api/sites/{siteId}/spaces/{space.Id}",
            request);

        // Assert
        response.EnsureSuccessStatusCode();
        var updated = await response.Content.ReadFromJsonAsync<SpaceInfo>();
        Assert.NotNull(updated);
        Assert.Equal("This room was resized to accommodate more people", updated.Description);
        Assert.Equal(200, updated.Geometry?.Coordinates[1].X);
    }

    [Fact]
    public async Task UpdateSpace_ClearDescription_WithResize()
    {
        // Arrange - Create space with description
        var siteId = await TestHelpers.GetOrCreateTestSite(_client);
        var space = await CreateRectangleSpace(siteId, "Room I", 0, 0, 100, 100);

        // First, add a description
        await _client.PutAsJsonAsync($"/api/sites/{siteId}/spaces/{space.Id}",
            new UpdateSpaceRequest { Description = "Original description" });

        // Act - Clear description while resizing
        var request = new UpdateSpaceRequest
        {
            Description = "", // Clear description
            Geometry = new SpaceGeometry
            {
                Type = "rectangle",
                Coordinates = new List<Coordinate>
                {
                    new() { X = 0, Y = 0 },
                    new() { X = 150, Y = 150 }
                }
            }
        };

        var response = await _client.PutAsJsonAsync(
            $"/api/sites/{siteId}/spaces/{space.Id}",
            request);

        // Assert
        response.EnsureSuccessStatusCode();
        var updated = await response.Content.ReadFromJsonAsync<SpaceInfo>();
        Assert.True(string.IsNullOrEmpty(updated?.Description));
        Assert.Equal(150, updated?.Geometry?.Coordinates[1].X);
    }

    #endregion

    #region Helper Methods

    private async Task<SpaceInfo> CreateRectangleSpace(
        Guid siteId,
        string name,
        double x1,
        double y1,
        double x2,
        double y2,
        string? code = null)
    {
        var request = new CreateSpaceRequest
        {
            Name = name,
            Code = code ?? $"R-{Guid.NewGuid():N}".Substring(0, 10),
            IsPhysical = true,
            Geometry = new SpaceGeometry
            {
                Type = "rectangle",
                Coordinates = new List<Coordinate>
                {
                    new() { X = (decimal)x1, Y = (decimal)y1 },
                    new() { X = (decimal)x2, Y = (decimal)y2 }
                }
            }
        };

        var response = await _client.PostAsJsonAsync($"/api/sites/{siteId}/spaces", request);
        response.EnsureSuccessStatusCode();
        var space = await response.Content.ReadFromJsonAsync<SpaceInfo>();
        Assert.NotNull(space);
        return space;
    }

    private async Task<SpaceInfo> CreatePolygonSpace(
        Guid siteId,
        string name,
        (double x, double y)[] vertices)
    {
        var request = new CreateSpaceRequest
        {
            Name = name,
            Code = $"P-{Guid.NewGuid():N}".Substring(0, 10),
            IsPhysical = true,
            Geometry = new SpaceGeometry
            {
                Type = "polygon",
                Coordinates = vertices.Select(v => new Coordinate { X = (decimal)v.x, Y = (decimal)v.y }).ToList()
            }
        };

        var response = await _client.PostAsJsonAsync($"/api/sites/{siteId}/spaces", request);
        response.EnsureSuccessStatusCode();
        var space = await response.Content.ReadFromJsonAsync<SpaceInfo>();
        Assert.NotNull(space);
        return space;
    }

    #endregion
}
