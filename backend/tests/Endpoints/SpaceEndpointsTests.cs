using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Constants;
using Api.Models;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Orkyo.Foundation.Tests.Endpoints;

/// <summary>
/// CRUD for placeable resources over the generic resource surface.
///
/// These moved off the retired site-scoped space routes and keep pinning the same behaviour:
/// physical-implies-geometry, per-site code uniqueness, deactivate-rather-than-delete, and the
/// site-scoped list. What deliberately did not move is the site-scoped 404 on writes — the
/// generic routes address a resource by id, so the site is no longer in the path.
/// </summary>
[Collection("Database collection")]
public class SpaceEndpointsTests
{
    private readonly HttpClient _client;

    public SpaceEndpointsTests(DatabaseFixture databaseFixture)
    {
        _client = databaseFixture.CreateAuthorizedClient();
    }

    /// <summary>The generic list answers with an envelope, not a bare array.</summary>
    private static async Task<List<ResourceInfo>> ReadListAsync(HttpResponseMessage response)
    {
        var envelope = await response.Content.ReadFromJsonAsync<JsonElement>();
        return envelope.GetProperty("items").Deserialize<List<ResourceInfo>>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
    }

    #region POST /api/resources - Create a placeable resource

    [Fact]
    public async Task CreateSpace_WithValidRectangleGeometry_ReturnsCreatedSpace()
    {
        // Arrange
        var siteId = DatabaseFixture.SiteId;
        var uniqueCode = $"A-{Guid.NewGuid():N}".Substring(0, 10);
        var request = new CreateResourceRequest
        {
            ResourceTypeKey = ResourceTypeKeys.Space,
            AllocationMode = AllocationModes.Exclusive,
            HomeSiteId = siteId,
            CrossSiteAllowed = false,
            Name = "Test Space A1",
            Code = uniqueCode,
            IsPhysical = true,
            Geometry = new ResourceGeometry
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
        var response = await _client.PostAsJsonAsync("/api/resources", request);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var space = await response.Content.ReadFromJsonAsync<ResourceInfo>();
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
        var siteId = DatabaseFixture.SiteId;
        var uniqueCode = $"P-{Guid.NewGuid():N}".Substring(0, 10);
        var request = new CreateResourceRequest
        {
            ResourceTypeKey = ResourceTypeKeys.Space,
            AllocationMode = AllocationModes.Exclusive,
            HomeSiteId = siteId,
            CrossSiteAllowed = false,
            Name = "Polygon Space",
            Code = uniqueCode,
            IsPhysical = true,
            Geometry = new ResourceGeometry
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
        var response = await _client.PostAsJsonAsync("/api/resources", request);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var space = await response.Content.ReadFromJsonAsync<ResourceInfo>();
        Assert.NotNull(space);
        Assert.Equal("polygon", space.Geometry?.Type);
        Assert.Equal(5, space.Geometry?.Coordinates.Count);
    }

    [Fact]
    public async Task CreateSpace_WithValidCircleGeometry_ReturnsCreatedSpace()
    {
        var siteId = DatabaseFixture.SiteId;
        var uniqueCode = $"C-{Guid.NewGuid():N}".Substring(0, 10);
        var request = new CreateResourceRequest
        {
            ResourceTypeKey = ResourceTypeKeys.Space,
            AllocationMode = AllocationModes.Exclusive,
            HomeSiteId = siteId,
            CrossSiteAllowed = false,
            Name = "Round Table",
            Code = uniqueCode,
            IsPhysical = true,
            Geometry = new ResourceGeometry
            {
                Type = "circle",
                Coordinates = new List<Coordinate>
                {
                    new() { X = 200, Y = 200 },  // centre
                    new() { X = 250, Y = 200 },  // rim -> r 50
                }
            }
        };

        var response = await _client.PostAsJsonAsync("/api/resources", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var space = await response.Content.ReadFromJsonAsync<ResourceInfo>();
        Assert.Equal("circle", space!.Geometry?.Type);
        // Both points survive the round trip — the rim is data, not a derived value.
        Assert.Equal(2, space.Geometry?.Coordinates.Count);
        Assert.Equal(200, space.Geometry?.Coordinates[0].X);
        Assert.Equal(250, space.Geometry?.Coordinates[1].X);
    }

    [Theory]
    [InlineData("circle", 1)]    // a centre without a rim point
    [InlineData("rectangle", 1)] // a rectangle is two corners
    [InlineData("polygon", 2)]   // a polygon needs at least three points
    [InlineData("hexagon", 2)]   // a type the allow-list has never heard of
    public async Task CreateSpace_InvalidGeometry_ReturnsBadRequest(string type, int pointCount)
    {
        var siteId = DatabaseFixture.SiteId;
        var request = new CreateResourceRequest
        {
            ResourceTypeKey = ResourceTypeKeys.Space,
            AllocationMode = AllocationModes.Exclusive,
            HomeSiteId = siteId,
            CrossSiteAllowed = false,
            Name = "Invalid Geometry",
            IsPhysical = true,
            Geometry = new ResourceGeometry
            {
                Type = type,
                Coordinates = Enumerable.Range(0, pointCount).Select(i => new Coordinate { X = i * 10, Y = i * 10 }).ToList()
            }
        };

        var response = await _client.PostAsJsonAsync("/api/resources", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateSpace_VirtualSpace_NoGeometryRequired()
    {
        // Arrange
        var siteId = DatabaseFixture.SiteId;
        var uniqueCode = $"V-{Guid.NewGuid():N}".Substring(0, 10);
        var request = new CreateResourceRequest
        {
            ResourceTypeKey = ResourceTypeKeys.Space,
            AllocationMode = AllocationModes.Exclusive,
            HomeSiteId = siteId,
            CrossSiteAllowed = false,
            Name = "Virtual Storage",
            Code = uniqueCode,
            IsPhysical = false,
            Geometry = null,
            Properties = new Dictionary<string, object> { { "capacity", 100 } }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/resources", request);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var space = await response.Content.ReadFromJsonAsync<ResourceInfo>();
        Assert.NotNull(space);
        Assert.False(space.IsPhysical);
        Assert.Null(space.Geometry);
    }

    [Fact]
    public async Task CreateSpace_PhysicalSpaceWithoutGeometry_ReturnsBadRequest()
    {
        // Arrange
        var siteId = DatabaseFixture.SiteId;
        var request = new CreateResourceRequest
        {
            ResourceTypeKey = ResourceTypeKeys.Space,
            AllocationMode = AllocationModes.Exclusive,
            HomeSiteId = siteId,
            CrossSiteAllowed = false,
            Name = "Invalid Physical Space",
            IsPhysical = true,
            Geometry = null // Physical space must have geometry
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/resources", request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateSpace_WithoutName_ReturnsBadRequest()
    {
        // Arrange
        var siteId = DatabaseFixture.SiteId;
        var request = new CreateResourceRequest
        {
            ResourceTypeKey = ResourceTypeKeys.Space,
            AllocationMode = AllocationModes.Exclusive,
            HomeSiteId = siteId,
            CrossSiteAllowed = false,
            Name = "", // Name is required
            IsPhysical = false
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/resources", request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateSpace_DuplicateCode_ReturnsBadRequest()
    {
        // Arrange
        var siteId = DatabaseFixture.SiteId;
        var uniqueCode = $"DUP-{Guid.NewGuid():N}".Substring(0, 10);
        var request1 = new CreateResourceRequest
        {
            ResourceTypeKey = ResourceTypeKeys.Space,
            AllocationMode = AllocationModes.Exclusive,
            HomeSiteId = siteId,
            CrossSiteAllowed = false,
            Name = "Space 1",
            Code = uniqueCode,
            IsPhysical = false
        };

        // Create first space
        await _client.PostAsJsonAsync("/api/resources", request1);

        // Try to create second space with same code
        var request2 = new CreateResourceRequest
        {
            ResourceTypeKey = ResourceTypeKeys.Space,
            AllocationMode = AllocationModes.Exclusive,
            HomeSiteId = siteId,
            CrossSiteAllowed = false,
            Name = "Space 2",
            Code = uniqueCode,
            IsPhysical = false
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/resources", request2);

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    #endregion

    #region GET /api/resources?hasGeometry - List placeable resources at a site

    [Fact]
    public async Task GetSpaces_ReturnsAllSpacesForSite()
    {
        // Arrange
        var siteId = DatabaseFixture.SiteId;

        // Create multiple spaces
        await CreateTestSpace(siteId, "Space 1", $"S1-{Guid.NewGuid():N}".Substring(0, 10));
        await CreateTestSpace(siteId, "Space 2", $"S2-{Guid.NewGuid():N}".Substring(0, 10));
        await CreateTestSpace(siteId, "Space 3", $"S3-{Guid.NewGuid():N}".Substring(0, 10));

        // Act — the scope the retired route had built in, spelled out as filters.
        var response = await _client.GetAsync(
            $"/api/resources?hasGeometry=true&isActive=true&siteId={siteId}");

        // Assert
        response.EnsureSuccessStatusCode();
        var spaces = await ReadListAsync(response);
        Assert.NotNull(spaces);
        Assert.True(spaces.Count >= 3, "Should return at least 3 spaces");
    }

    [Fact]
    public async Task GetSpaces_NonExistentSite_ReturnsEmptyList()
    {
        // Arrange
        var nonExistentSiteId = Guid.NewGuid();

        // Act
        var response = await _client.GetAsync(
            $"/api/resources?hasGeometry=true&isActive=true&siteId={nonExistentSiteId}");

        // Assert
        response.EnsureSuccessStatusCode();
        var spaces = await ReadListAsync(response);
        Assert.NotNull(spaces);
        Assert.Empty(spaces);
    }

    #endregion

    #region GET /api/resources/{id} - Get one

    [Fact]
    public async Task GetSpace_ExistingSpace_ReturnsSpace()
    {
        // Arrange
        var siteId = DatabaseFixture.SiteId;
        var createdSpace = await CreateTestSpace(siteId, "Test Space", $"T-{Guid.NewGuid():N}".Substring(0, 10));

        // Act
        var response = await _client.GetAsync($"/api/resources/{createdSpace.Id}");

        // Assert
        response.EnsureSuccessStatusCode();
        var space = await response.Content.ReadFromJsonAsync<ResourceInfo>();
        Assert.NotNull(space);
        Assert.Equal(createdSpace.Id, space.Id);
        Assert.Equal("Test Space", space.Name);
    }

    [Fact]
    public async Task GetSpace_NonExistentSpace_ReturnsNotFound()
    {
        // Arrange
        var siteId = DatabaseFixture.SiteId;
        var nonExistentResourceId = Guid.NewGuid();

        // Act
        var response = await _client.GetAsync($"/api/resources/{nonExistentResourceId}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    #endregion

    #region PUT /api/resources/{id}

    [Fact]
    public async Task UpdateSpace_ValidUpdate_ReturnsUpdatedSpace()
    {
        // Arrange
        var siteId = DatabaseFixture.SiteId;
        var originalCode = $"O-{Guid.NewGuid():N}".Substring(0, 10);
        var createdSpace = await CreateTestSpace(siteId, "Original Name", originalCode);

        var uniqueCode = $"U-{Guid.NewGuid():N}".Substring(0, 10);
        var updateRequest = new UpdateResourceRequest
        {
            Name = "Updated Name",
            Code = uniqueCode,
            Geometry = new ResourceGeometry
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
        var response = await _client.PutAsJsonAsync($"/api/resources/{createdSpace.Id}", updateRequest);

        // Assert
        response.EnsureSuccessStatusCode();
        var updatedSpace = await response.Content.ReadFromJsonAsync<ResourceInfo>();
        Assert.NotNull(updatedSpace);
        Assert.Equal("Updated Name", updatedSpace.Name);
        Assert.Equal(uniqueCode, updatedSpace.Code);
    }

    [Fact]
    public async Task UpdateSpace_NonExistentSpace_ReturnsNotFound()
    {
        // Arrange
        var siteId = DatabaseFixture.SiteId;
        var nonExistentResourceId = Guid.NewGuid();
        var updateRequest = new UpdateResourceRequest
        {
            Name = "Updated Name"
        };

        // Act
        var response = await _client.PutAsJsonAsync($"/api/resources/{nonExistentResourceId}", updateRequest);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    #endregion

    #region DELETE /api/resources/{id}

    [Fact]
    public async Task DeleteSpace_ExistingSpace_ReturnsNoContent()
    {
        // Arrange
        var siteId = DatabaseFixture.SiteId;
        var createdSpace = await CreateTestSpace(siteId, "To Delete", $"D-{Guid.NewGuid():N}".Substring(0, 10));

        // Act
        var response = await _client.DeleteAsync($"/api/resources/{createdSpace.Id}");

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // Deactivated, not deleted — the resource stays for its assignment history. The retired
        // route's read was scoped to active rows so it answered 404; the generic read by id
        // returns the row, and the flag is what carries the meaning.
        var getResponse = await _client.GetAsync($"/api/resources/{createdSpace.Id}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var deactivated = await getResponse.Content.ReadFromJsonAsync<ResourceInfo>();
        Assert.False(deactivated!.IsActive);

        // And it drops out of the site's placeable list, which is what the floorplan reads.
        var listResponse = await _client.GetAsync(
            $"/api/resources?hasGeometry=true&isActive=true&siteId={siteId}");
        Assert.DoesNotContain(await ReadListAsync(listResponse), r => r.Id == createdSpace.Id);
    }

    [Fact]
    public async Task DeleteSpace_NonExistentSpace_ReturnsNotFound()
    {
        // Arrange
        var siteId = DatabaseFixture.SiteId;
        var nonExistentResourceId = Guid.NewGuid();

        // Act
        var response = await _client.DeleteAsync($"/api/resources/{nonExistentResourceId}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    #endregion

    #region Description Field Tests

    [Fact]
    public async Task Description_IsReturnedByCreateGetAndList_AndNullWhenOmitted()
    {
        var siteId = DatabaseFixture.SiteId;
        var withCode = $"DESC-{Guid.NewGuid():N}".Substring(0, 10);
        var withoutCode = $"NODESC-{Guid.NewGuid():N}".Substring(0, 10);

        var withResponse = await _client.PostAsJsonAsync("/api/resources", new CreateResourceRequest
        {
            ResourceTypeKey = ResourceTypeKeys.Space,
            AllocationMode = AllocationModes.Exclusive,
            HomeSiteId = siteId,
            CrossSiteAllowed = false,
            Name = "Conference Room",
            Code = withCode,
            Description = "Large meeting room with projector and whiteboard",
            IsPhysical = false
        });
        withResponse.EnsureSuccessStatusCode();
        var created = (await withResponse.Content.ReadFromJsonAsync<ResourceInfo>())!;
        Assert.Equal("Large meeting room with projector and whiteboard", created.Description);

        var withoutResponse = await _client.PostAsJsonAsync("/api/resources", new CreateResourceRequest
        {
            ResourceTypeKey = ResourceTypeKeys.Space,
            AllocationMode = AllocationModes.Exclusive,
            HomeSiteId = siteId,
            CrossSiteAllowed = false,
            Name = "Storage Room",
            Code = withoutCode,
            IsPhysical = false
        });
        withoutResponse.EnsureSuccessStatusCode();
        Assert.Null((await withoutResponse.Content.ReadFromJsonAsync<ResourceInfo>())!.Description);

        var fetched = await _client.GetFromJsonAsync<ResourceInfo>($"/api/resources/{created.Id}");
        Assert.Equal("Large meeting room with projector and whiteboard", fetched!.Description);

        var list = await _client.GetAsync($"/api/resources?hasGeometry=true&isActive=true&siteId={siteId}");
        list.EnsureSuccessStatusCode();
        var spaces = await ReadListAsync(list);
        Assert.Equal("Large meeting room with projector and whiteboard", spaces.Single(s => s.Code == withCode).Description);
        Assert.Null(spaces.Single(s => s.Code == withoutCode).Description);
    }

    [Fact]
    public async Task UpdateSpace_Description_CanBeAddedChangedAndCleared()
    {
        var siteId = DatabaseFixture.SiteId;
        var space = await CreateTestSpace(siteId, "Auditorium", $"AUD-{Guid.NewGuid():N}".Substring(0, 10));

        async Task<string?> UpdateAsync(string description)
        {
            var response = await _client.PutAsJsonAsync($"/api/resources/{space.Id}",
                new UpdateResourceRequest { Description = description });
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<ResourceInfo>())!.Description;
        }

        Assert.Equal("Open office space with natural lighting", await UpdateAsync("Open office space with natural lighting"));

        var longDescription = string.Join(" ", Enumerable.Repeat(
            "This is a detailed description of the space with many features and amenities.", 20));
        Assert.Equal(longDescription, await UpdateAsync(longDescription));

        Assert.True(string.IsNullOrEmpty(await UpdateAsync("")));
    }

    #endregion

    #region Helper Methods

    private async Task<ResourceInfo> CreateTestSpace(Guid siteId, string name, string code)
    {
        var request = new CreateResourceRequest
        {
            ResourceTypeKey = ResourceTypeKeys.Space,
            AllocationMode = AllocationModes.Exclusive,
            HomeSiteId = siteId,
            CrossSiteAllowed = false,
            Name = name,
            Code = code,
            IsPhysical = false
        };

        var response = await _client.PostAsJsonAsync("/api/resources", request);
        response.EnsureSuccessStatusCode();
        var space = await response.Content.ReadFromJsonAsync<ResourceInfo>();
        Assert.NotNull(space);
        return space;
    }

    #endregion

    [Fact]
    public async Task Space_CarriesCustomFieldValues_OnCreateAndUpdate()
    {
        // foundation#110: a space is an ordinary resource, so it holds the custom fields its type
        // defines. Before this the space endpoints had nowhere to put them, and the values a
        // tenant entered anywhere else were dropped on the way through.
        var siteId = DatabaseFixture.SiteId;

        var types = await _client.GetFromJsonAsync<List<ResourceTypeInfo>>("/api/resource-types");
        var spaceType = types!.Single(t => t.Key == ResourceTypeKeys.Space);

        var key = $"floor_finish_{Guid.NewGuid():N}"[..40];
        var field = await _client.PostAsJsonAsync($"/api/resource-types/{spaceType.Id}/custom-fields",
            new CreateResourceCustomFieldRequest
            {
                Key = key,
                Label = "Floor finish",
                DataType = CustomFieldDataTypes.Text,
            });
        Assert.Equal(HttpStatusCode.Created, field.StatusCode);

        var created = await _client.PostAsJsonAsync("/api/resources", new CreateResourceRequest
        {
            ResourceTypeKey = ResourceTypeKeys.Space,
            AllocationMode = AllocationModes.Exclusive,
            HomeSiteId = siteId,
            CrossSiteAllowed = false,
            Name = "Workshop",
            Code = $"W-{Guid.NewGuid():N}"[..10],
            // Non-physical: a physical space must carry geometry, which is beside the point here.
            IsPhysical = false,
            CustomFields = new Dictionary<string, JsonElement>
            {
                [key] = JsonDocument.Parse("\"sealed concrete\"").RootElement,
            },
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var space = (await created.Content.ReadFromJsonAsync<ResourceInfo>())!;
        Assert.Equal("sealed concrete", space.CustomFields![key].GetString());

        var updated = await _client.PutAsJsonAsync($"/api/resources/{space.Id}",
            new UpdateResourceRequest
            {
                Name = "Workshop",
                CustomFields = new Dictionary<string, JsonElement>
                {
                    [key] = JsonDocument.Parse("\"epoxy\"").RootElement,
                },
            });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal("epoxy", (await updated.Content.ReadFromJsonAsync<ResourceInfo>())!.CustomFields![key].GetString());
    }
}
