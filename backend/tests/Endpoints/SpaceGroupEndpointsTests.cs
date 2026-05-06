using System.Net;
using System.Net.Http.Json;
using Api.Models;
using Xunit;

namespace Orkyo.Foundation.Tests.Endpoints;

[Collection("Database collection")]
public class SpaceGroupEndpointsTests : IAsyncLifetime
{
    private readonly HttpClient _client;

    public SpaceGroupEndpointsTests(DatabaseFixture fixture)
    {
        _client = fixture.CreateAuthorizedClient();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GetAllGroups_ReturnsSuccessfully()
    {
        // Act
        var response = await _client.GetAsync("/api/groups");

        // Assert
        response.EnsureSuccessStatusCode();
        var groups = await response.Content.ReadFromJsonAsync<List<SpaceGroupInfo>>();
        Assert.NotNull(groups);
        // Don't assert empty or specific count since other tests may have created groups
    }

    [Fact]
    public async Task CreateGroup_WithValidData_ReturnsCreated()
    {
        // Arrange
        var request = new CreateSpaceGroupRequest
        {
            Name = "Meeting Rooms",
            Description = "All meeting rooms",
            Color = "#3b82f6",
            DisplayOrder = 0
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/groups", request);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var group = await response.Content.ReadFromJsonAsync<SpaceGroupInfo>();
        Assert.NotNull(group);
        Assert.Equal("Meeting Rooms", group.Name);
        Assert.Equal("All meeting rooms", group.Description);
        Assert.Equal("#3b82f6", group.Color);
        Assert.Equal(0, group.DisplayOrder);
        Assert.Equal(0, group.SpaceCount);
    }

    [Fact]
    public async Task CreateGroup_WithoutOptionalFields_ReturnsCreated()
    {
        // Arrange
        var request = new CreateSpaceGroupRequest
        {
            Name = "Test Group",
            DisplayOrder = 0
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/groups", request);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var group = await response.Content.ReadFromJsonAsync<SpaceGroupInfo>();
        Assert.NotNull(group);
        Assert.Equal("Test Group", group.Name);
        Assert.Null(group.Description);
        Assert.Null(group.Color);
    }

    [Fact]
    public async Task CreateGroup_WithEmptyName_ReturnsBadRequest()
    {
        // Arrange
        var request = new CreateSpaceGroupRequest
        {
            Name = "",
            DisplayOrder = 0
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/groups", request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateGroup_WithInvalidColor_ReturnsBadRequest()
    {
        // Arrange
        var request = new CreateSpaceGroupRequest
        {
            Name = "Test Group",
            Color = "invalid-color",
            DisplayOrder = 0
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/groups", request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetGroupById_WhenExists_ReturnsGroup()
    {
        // Arrange
        var createResponse = await _client.PostAsJsonAsync("/api/groups", new CreateSpaceGroupRequest
        {
            Name = "Test Group",
            DisplayOrder = 0
        });
        var createdGroup = await createResponse.Content.ReadFromJsonAsync<SpaceGroupInfo>();

        // Act
        var response = await _client.GetAsync($"/api/groups/{createdGroup!.Id}");

        // Assert
        response.EnsureSuccessStatusCode();
        var group = await response.Content.ReadFromJsonAsync<SpaceGroupInfo>();
        Assert.NotNull(group);
        Assert.Equal(createdGroup.Id, group.Id);
        Assert.Equal("Test Group", group.Name);
    }

    [Fact]
    public async Task GetGroupById_WhenNotExists_ReturnsNotFound()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();

        // Act
        var response = await _client.GetAsync($"/api/groups/{nonExistentId}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateGroup_WithValidData_ReturnsUpdated()
    {
        // Arrange
        var createResponse = await _client.PostAsJsonAsync("/api/groups", new CreateSpaceGroupRequest
        {
            Name = "Original Name",
            DisplayOrder = 0
        });
        var createdGroup = await createResponse.Content.ReadFromJsonAsync<SpaceGroupInfo>();

        var updateRequest = new UpdateSpaceGroupRequest
        {
            Name = "Updated Name",
            Description = "Updated description",
            Color = "#ef4444",
            DisplayOrder = 5
        };

        // Act
        var response = await _client.PutAsJsonAsync($"/api/groups/{createdGroup!.Id}", updateRequest);

        // Assert
        response.EnsureSuccessStatusCode();
        var updated = await response.Content.ReadFromJsonAsync<SpaceGroupInfo>();
        Assert.NotNull(updated);
        Assert.Equal("Updated Name", updated.Name);
        Assert.Equal("Updated description", updated.Description);
        Assert.Equal("#ef4444", updated.Color);
        Assert.Equal(5, updated.DisplayOrder);
    }

    [Fact]
    public async Task UpdateGroup_PartialUpdate_OnlyUpdatesProvidedFields()
    {
        // Arrange
        var createResponse = await _client.PostAsJsonAsync("/api/groups", new CreateSpaceGroupRequest
        {
            Name = "Original Name",
            Description = "Original description",
            Color = "#3b82f6",
            DisplayOrder = 0
        });
        var createdGroup = await createResponse.Content.ReadFromJsonAsync<SpaceGroupInfo>();

        var updateRequest = new UpdateSpaceGroupRequest
        {
            Name = "Updated Name"
            // Only updating name, leaving other fields
        };

        // Act
        var response = await _client.PutAsJsonAsync($"/api/groups/{createdGroup!.Id}", updateRequest);

        // Assert
        response.EnsureSuccessStatusCode();
        var updated = await response.Content.ReadFromJsonAsync<SpaceGroupInfo>();
        Assert.NotNull(updated);
        Assert.Equal("Updated Name", updated.Name);
        Assert.Equal("Original description", updated.Description); // Should remain unchanged
        Assert.Equal("#3b82f6", updated.Color); // Should remain unchanged
    }

    [Fact]
    public async Task UpdateGroup_WhenNotExists_ReturnsNotFound()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();
        var updateRequest = new UpdateSpaceGroupRequest
        {
            Name = "Updated Name"
        };

        // Act
        var response = await _client.PutAsJsonAsync($"/api/groups/{nonExistentId}", updateRequest);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteGroup_WhenExists_ReturnsNoContent()
    {
        // Arrange
        var createResponse = await _client.PostAsJsonAsync("/api/groups", new CreateSpaceGroupRequest
        {
            Name = "To Delete",
            DisplayOrder = 0
        });
        var createdGroup = await createResponse.Content.ReadFromJsonAsync<SpaceGroupInfo>();

        // Act
        var response = await _client.DeleteAsync($"/api/groups/{createdGroup!.Id}");

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // Verify it's deleted
        var getResponse = await _client.GetAsync($"/api/groups/{createdGroup.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task DeleteGroup_WhenNotExists_ReturnsNotFound()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();

        // Act
        var response = await _client.DeleteAsync($"/api/groups/{nonExistentId}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetAllGroups_ReturnsGroupsInDisplayOrder()
    {
        // Arrange - Create groups with specific display orders
        var groupCResp = await _client.PostAsJsonAsync("/api/groups", new CreateSpaceGroupRequest
        {
            Name = "Group C",
            DisplayOrder = 2
        });
        var groupC = await groupCResp.Content.ReadFromJsonAsync<SpaceGroupInfo>();

        var groupAResp = await _client.PostAsJsonAsync("/api/groups", new CreateSpaceGroupRequest
        {
            Name = "Group A",
            DisplayOrder = 0
        });
        var groupA = await groupAResp.Content.ReadFromJsonAsync<SpaceGroupInfo>();

        var groupBResp = await _client.PostAsJsonAsync("/api/groups", new CreateSpaceGroupRequest
        {
            Name = "Group B",
            DisplayOrder = 1
        });
        var groupB = await groupBResp.Content.ReadFromJsonAsync<SpaceGroupInfo>();

        // Act
        var response = await _client.GetAsync("/api/groups");

        // Assert
        response.EnsureSuccessStatusCode();
        var groups = await response.Content.ReadFromJsonAsync<List<SpaceGroupInfo>>();
        Assert.NotNull(groups);

        // Find our created groups and verify they're in the correct order
        var ourGroups = groups!.Where(g =>
            g.Id == groupA!.Id || g.Id == groupB!.Id || g.Id == groupC!.Id
        ).ToList();

        Assert.Equal(3, ourGroups.Count);
        Assert.Equal(groupA!.Id, ourGroups[0].Id);
        Assert.Equal(groupB!.Id, ourGroups[1].Id);
        Assert.Equal(groupC!.Id, ourGroups[2].Id);
    }
}
