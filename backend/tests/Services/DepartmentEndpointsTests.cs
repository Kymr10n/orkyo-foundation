using System.Net;
using System.Net.Http.Json;
using Api.Models;
using Xunit;

namespace Orkyo.Foundation.Tests.Services;

[Collection("Database collection")]
public class DepartmentEndpointsTests
{
    private readonly HttpClient _client;

    public DepartmentEndpointsTests(DatabaseFixture fixture)
    {
        _client = fixture.CreateAuthorizedClient();
    }

    [Fact]
    public async Task Create_RootAndChild_RoundTrips()
    {
        var rootName = $"D-R-{Guid.NewGuid():N}"[..20];
        var root = await CreateAsync(rootName, parentId: null);
        Assert.Null(root.ParentDepartmentId);

        var childName = $"D-C-{Guid.NewGuid():N}"[..20];
        var child = await CreateAsync(childName, parentId: root.Id);
        Assert.Equal(root.Id, child.ParentDepartmentId);

        var treeResp = await _client.GetAsync("/api/departments/tree");
        treeResp.EnsureSuccessStatusCode();
        var tree = await treeResp.Content.ReadFromJsonAsync<List<DepartmentTreeNode>>();
        Assert.NotNull(tree);
        var rootNode = tree.SingleOrDefault(n => n.Id == root.Id);
        Assert.NotNull(rootNode);
        Assert.Single(rootNode.Children, c => c.Id == child.Id);
    }

    [Fact]
    public async Task Create_DuplicateSiblingName_Returns409()
    {
        var parentName = $"D-P-{Guid.NewGuid():N}"[..20];
        var parent = await CreateAsync(parentName, parentId: null);

        var dupName = $"D-DUP-{Guid.NewGuid():N}"[..20];
        var first = await CreateAsync(dupName, parentId: parent.Id);

        var secondResp = await _client.PostAsJsonAsync("/api/departments",
            new CreateDepartmentRequest { Name = dupName, ParentDepartmentId = parent.Id });
        Assert.Equal(HttpStatusCode.Conflict, secondResp.StatusCode);

        // Same name under a *different* parent is allowed.
        var otherParent = await CreateAsync($"D-OP-{Guid.NewGuid():N}"[..20], parentId: null);
        var allowedResp = await _client.PostAsJsonAsync("/api/departments",
            new CreateDepartmentRequest { Name = dupName, ParentDepartmentId = otherParent.Id });
        Assert.Equal(HttpStatusCode.Created, allowedResp.StatusCode);
    }

    [Fact]
    public async Task Update_CircularReparent_Returns409()
    {
        // Build A -> B -> C
        var a = await CreateAsync($"D-A-{Guid.NewGuid():N}"[..20], parentId: null);
        var b = await CreateAsync($"D-B-{Guid.NewGuid():N}"[..20], parentId: a.Id);
        var c = await CreateAsync($"D-C-{Guid.NewGuid():N}"[..20], parentId: b.Id);

        // Attempting to set A.parent = C would create a cycle: A -> C -> B -> A
        var resp = await _client.PutAsJsonAsync($"/api/departments/{a.Id}",
            new UpdateDepartmentRequest { ParentDepartmentId = c.Id, ChangeParent = true });
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
    }

    [Fact]
    public async Task Update_SelfParent_Returns409()
    {
        var d = await CreateAsync($"D-S-{Guid.NewGuid():N}"[..20], parentId: null);
        var resp = await _client.PutAsJsonAsync($"/api/departments/{d.Id}",
            new UpdateDepartmentRequest { ParentDepartmentId = d.Id, ChangeParent = true });
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
    }

    [Fact]
    public async Task Delete_DepartmentWithChildren_Returns409()
    {
        var parent = await CreateAsync($"D-PWC-{Guid.NewGuid():N}"[..20], parentId: null);
        await CreateAsync($"D-C-{Guid.NewGuid():N}"[..20], parentId: parent.Id);

        // FK ON DELETE RESTRICT blocks the parent delete; the repo surfaces this
        // as InvalidOperationException → 409.
        var resp = await _client.DeleteAsync($"/api/departments/{parent.Id}");
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
    }

    private async Task<DepartmentInfo> CreateAsync(string name, Guid? parentId)
    {
        var resp = await _client.PostAsJsonAsync("/api/departments",
            new CreateDepartmentRequest { Name = name, ParentDepartmentId = parentId });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<DepartmentInfo>())!;
    }
}
