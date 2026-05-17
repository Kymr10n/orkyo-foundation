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
}
