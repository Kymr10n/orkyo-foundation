using System.Net;
using System.Net.Http.Json;
using Api.Models;
using Xunit;

namespace Orkyo.Foundation.Tests.Endpoints;

[Collection("Database collection")]
public class ResourceGroupMembershipTests
{
    private readonly HttpClient _client;
    private readonly DatabaseFixture _fixture;

    public ResourceGroupMembershipTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.CreateAuthorizedClient();
    }

    private async Task<SpaceGroupInfo> CreateGroupAsync(string name)
    {
        var resp = await _client.PostAsJsonAsync("/api/groups", new CreateSpaceGroupRequest
        {
            Name = name,
            DisplayOrder = 0,
        });
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<SpaceGroupInfo>())!;
    }

    private async Task<ResourceInfo> CreatePersonAsync(string name)
    {
        var resp = await _client.PostAsJsonAsync("/api/resources", new CreateResourceRequest
        {
            ResourceTypeKey = "person",
            Name = name,
            AllocationMode = "Fractional",
            BaseAvailabilityPercent = 100,
        });
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<ResourceInfo>())!;
    }

    [Fact]
    public async Task GetMembers_EmptyGroup_ReturnsEmptyList()
    {
        var group = await CreateGroupAsync($"grp-member-empty-{Guid.NewGuid():N}"[..30]);

        var resp = await _client.GetAsync($"/api/resource-groups/{group.Id}/members");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<ResourceGroupMembersResponse>();
        Assert.NotNull(body);
        Assert.Equal(group.Id, body.GroupId);
        Assert.Empty(body.Members);
    }

    [Fact]
    public async Task SetMembers_AddsAndReplacesMembers()
    {
        var group = await CreateGroupAsync($"grp-member-set-{Guid.NewGuid():N}"[..30]);
        var p1 = await CreatePersonAsync($"Person-A-{Guid.NewGuid():N}"[..20]);
        var p2 = await CreatePersonAsync($"Person-B-{Guid.NewGuid():N}"[..20]);

        // Add p1
        var set1 = await _client.PutAsJsonAsync($"/api/resource-groups/{group.Id}/members",
            new SetResourceGroupMembersRequest { ResourceIds = [p1.Id] });
        Assert.Equal(HttpStatusCode.OK, set1.StatusCode);
        var body1 = await set1.Content.ReadFromJsonAsync<ResourceGroupMembersResponse>();
        Assert.Single(body1!.Members);
        Assert.Equal(p1.Id, body1.Members[0].Id);

        // Replace with p2 only
        var set2 = await _client.PutAsJsonAsync($"/api/resource-groups/{group.Id}/members",
            new SetResourceGroupMembersRequest { ResourceIds = [p2.Id] });
        Assert.Equal(HttpStatusCode.OK, set2.StatusCode);
        var body2 = await set2.Content.ReadFromJsonAsync<ResourceGroupMembersResponse>();
        Assert.Single(body2!.Members);
        Assert.Equal(p2.Id, body2.Members[0].Id);
    }

    [Fact]
    public async Task SetMembers_EmptyList_ClearsMembers()
    {
        var group = await CreateGroupAsync($"grp-member-clear-{Guid.NewGuid():N}"[..30]);
        var person = await CreatePersonAsync($"Person-Clr-{Guid.NewGuid():N}"[..20]);

        await _client.PutAsJsonAsync($"/api/resource-groups/{group.Id}/members",
            new SetResourceGroupMembersRequest { ResourceIds = [person.Id] });

        var clear = await _client.PutAsJsonAsync($"/api/resource-groups/{group.Id}/members",
            new SetResourceGroupMembersRequest { ResourceIds = [] });
        Assert.Equal(HttpStatusCode.OK, clear.StatusCode);

        var get = await _client.GetAsync($"/api/resource-groups/{group.Id}/members");
        var body = await get.Content.ReadFromJsonAsync<ResourceGroupMembersResponse>();
        Assert.Empty(body!.Members);
    }

    [Fact]
    public async Task GetMembers_ReflectsSetMembers()
    {
        var group = await CreateGroupAsync($"grp-member-get-{Guid.NewGuid():N}"[..30]);
        var p1 = await CreatePersonAsync($"Person-G1-{Guid.NewGuid():N}"[..20]);
        var p2 = await CreatePersonAsync($"Person-G2-{Guid.NewGuid():N}"[..20]);

        await _client.PutAsJsonAsync($"/api/resource-groups/{group.Id}/members",
            new SetResourceGroupMembersRequest { ResourceIds = [p1.Id, p2.Id] });

        var resp = await _client.GetAsync($"/api/resource-groups/{group.Id}/members");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<ResourceGroupMembersResponse>();
        Assert.Equal(2, body!.Members.Count);
        Assert.Contains(body.Members, m => m.Id == p1.Id);
        Assert.Contains(body.Members, m => m.Id == p2.Id);
    }
}
