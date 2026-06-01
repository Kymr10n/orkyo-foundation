using System.Net;
using System.Net.Http.Json;
using Api.Models;
using Xunit;

namespace Orkyo.Foundation.Tests.Endpoints;

/// <summary>
/// Tests for resource-type enforcement on resource groups and their membership.
///
/// Verifies:
/// 1. CreateGroup returns ResourceTypeKey in the response.
/// 2. SetMembers with same-type resources succeeds.
/// 3. SetMembers with cross-type resources returns 400.
/// </summary>
[Collection("Database collection")]
public class ResourceGroupTypingTests
{
    private readonly HttpClient _client;

    public ResourceGroupTypingTests(DatabaseFixture databaseFixture)
    {
        _client = databaseFixture.CreateAuthorizedClient();
    }

    private async Task<ResourceGroupInfo> CreateGroupAsync(string resourceTypeKey, string name)
    {
        var request = new CreateResourceGroupRequest
        {
            ResourceTypeKey = resourceTypeKey,
            Name = name,
        };
        var response = await _client.PostAsJsonAsync("/api/resource-groups", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ResourceGroupInfo>())!;
    }

    private async Task<ResourceInfo> CreateResourceAsync(string resourceTypeKey, string name)
    {
        var request = new CreateResourceRequest
        {
            ResourceTypeKey = resourceTypeKey,
            Name = name,
            AllocationMode = "Fractional",
        };
        var response = await _client.PostAsJsonAsync("/api/resources", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ResourceInfo>())!;
    }

    [Fact]
    public async Task CreateGroup_ReturnsResourceTypeKey()
    {
        var group = await CreateGroupAsync("space", $"TypingTest-{Guid.NewGuid():N}"[..20]);

        Assert.Equal("space", group.ResourceTypeKey);
    }

    [Fact]
    public async Task SetMembers_SameType_ReturnsOk()
    {
        var group = await CreateGroupAsync("person", $"TypingTest-{Guid.NewGuid():N}"[..20]);
        var person = await CreateResourceAsync("person", $"Person-{Guid.NewGuid():N}"[..20]);

        var response = await _client.PutAsJsonAsync(
            $"/api/resource-groups/{group.Id}/members",
            new { resourceIds = new[] { person.Id } });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SetMembers_CrossType_Returns400()
    {
        // Create a "space" group but try to add a "person" resource to it.
        var group = await CreateGroupAsync("space", $"TypingTest-{Guid.NewGuid():N}"[..20]);
        var person = await CreateResourceAsync("person", $"Person-{Guid.NewGuid():N}"[..20]);

        var response = await _client.PutAsJsonAsync(
            $"/api/resource-groups/{group.Id}/members",
            new { resourceIds = new[] { person.Id } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── GET /api/resource-groups?resourceTypeKey=... ──────────────────────────

    [Fact]
    public async Task GetResourceGroups_ByTypeKey_ReturnsMatchingGroups()
    {
        var name = $"GetList-{Guid.NewGuid():N}"[..20];
        await CreateGroupAsync("person", name);

        var response = await _client.GetAsync("/api/resource-groups?resourceTypeKey=person");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var groups = await response.Content.ReadFromJsonAsync<List<ResourceGroupInfo>>();
        Assert.NotNull(groups);
        Assert.Contains(groups, g => g.Name == name);
    }

    // ── GET /api/resource-groups/{id} ────────────────────────────────────────

    [Fact]
    public async Task GetResourceGroup_ExistingId_Returns200()
    {
        var created = await CreateGroupAsync("space", $"GetById-{Guid.NewGuid():N}"[..20]);

        var response = await _client.GetAsync($"/api/resource-groups/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var group = await response.Content.ReadFromJsonAsync<ResourceGroupInfo>();
        Assert.Equal(created.Id, group!.Id);
    }

    [Fact]
    public async Task GetResourceGroup_NonExistentId_Returns404()
    {
        var response = await _client.GetAsync($"/api/resource-groups/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── PUT /api/resource-groups/{id} ────────────────────────────────────────

    [Fact]
    public async Task UpdateResourceGroup_ValidData_Returns200()
    {
        var group = await CreateGroupAsync("person", $"UpdateTest-{Guid.NewGuid():N}"[..20]);

        var updateReq = new UpdateResourceGroupRequest { Name = $"Updated-{Guid.NewGuid():N}"[..20] };
        var response = await _client.PutAsJsonAsync($"/api/resource-groups/{group.Id}", updateReq);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<ResourceGroupInfo>();
        Assert.Equal(updateReq.Name, updated!.Name);
    }

    [Fact]
    public async Task UpdateResourceGroup_NonExistentId_Returns404()
    {
        var updateReq = new UpdateResourceGroupRequest { Name = "Ghost" };
        var response = await _client.PutAsJsonAsync($"/api/resource-groups/{Guid.NewGuid()}", updateReq);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── DELETE /api/resource-groups/{id} ─────────────────────────────────────

    [Fact]
    public async Task DeleteResourceGroup_ExistingId_Returns204()
    {
        var group = await CreateGroupAsync("space", $"DelTest-{Guid.NewGuid():N}"[..20]);

        var response = await _client.DeleteAsync($"/api/resource-groups/{group.Id}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task DeleteResourceGroup_NonExistentId_Returns404()
    {
        var response = await _client.DeleteAsync($"/api/resource-groups/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── GET /api/resource-groups/{id}/members ────────────────────────────────

    [Fact]
    public async Task GetResourceGroupMembers_Returns200WithMemberList()
    {
        var group = await CreateGroupAsync("person", $"MembersTest-{Guid.NewGuid():N}"[..20]);

        var response = await _client.GetAsync($"/api/resource-groups/{group.Id}/members");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
