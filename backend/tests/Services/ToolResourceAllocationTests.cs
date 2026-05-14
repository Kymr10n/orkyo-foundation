using System.Net;
using System.Net.Http.Json;
using Api.Models;
using Xunit;

namespace Orkyo.Foundation.Tests.Services;

/// <summary>
/// Verifies exclusive allocation rules for Tool resources end-to-end via HTTP.
/// </summary>
[Collection("Database collection")]
public class ToolResourceAllocationTests
{
    private readonly HttpClient _client;

    public ToolResourceAllocationTests(DatabaseFixture fixture)
    {
        _client = fixture.CreateAuthorizedClient();
    }

    private async Task<ResourceInfo> CreateToolAsync(string name)
    {
        var resp = await _client.PostAsJsonAsync("/api/resources", new CreateResourceRequest
        {
            ResourceTypeKey = "tool",
            Name = name,
            AllocationMode = "Exclusive",
            BaseAvailabilityPercent = 100,
        });
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<ResourceInfo>())!;
    }

    private async Task<Guid> CreateRequestIdAsync()
    {
        var resp = await _client.PostAsJsonAsync("/api/requests", new
        {
            Name = $"TestReq-{Guid.NewGuid():N}"[..20],
            MinimalDurationValue = 1,
            MinimalDurationUnit = "hours",
            SchedulingSettingsApply = false,
        });
        var obj = await resp.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        return obj.GetProperty("id").GetGuid();
    }

    private Task<HttpResponseMessage> AssignAsync(Guid resourceId, Guid requestId, DateTime start, DateTime end)
        => _client.PostAsJsonAsync("/api/resource-assignments", new CreateResourceAssignmentRequest
        {
            ResourceId = resourceId,
            RequestId = requestId,
            StartUtc = start,
            EndUtc = end,
        });

    [Fact]
    public async Task ExclusiveTool_FirstAssignment_Succeeds()
    {
        var tool = await CreateToolAsync($"Tool-A-{Guid.NewGuid():N}"[..20]);
        var start = DateTime.UtcNow.AddDays(1);

        var resp = await AssignAsync(tool.Id, await CreateRequestIdAsync(), start, start.AddDays(3));
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
    }

    [Fact]
    public async Task ExclusiveTool_OverlappingAssignment_Returns409()
    {
        var tool = await CreateToolAsync($"Tool-B-{Guid.NewGuid():N}"[..20]);
        var start = DateTime.UtcNow.AddDays(5);
        var end = start.AddDays(4);

        var r1 = await AssignAsync(tool.Id, await CreateRequestIdAsync(), start, end);
        Assert.Equal(HttpStatusCode.Created, r1.StatusCode);

        var r2 = await AssignAsync(tool.Id, await CreateRequestIdAsync(), start.AddDays(2), end.AddDays(2));
        Assert.Equal(HttpStatusCode.Conflict, r2.StatusCode);
    }

    [Fact]
    public async Task ExclusiveTool_NonOverlappingWindows_BothSucceed()
    {
        var tool = await CreateToolAsync($"Tool-C-{Guid.NewGuid():N}"[..20]);
        var start = DateTime.UtcNow.AddDays(15);

        var r1 = await AssignAsync(tool.Id, await CreateRequestIdAsync(), start, start.AddDays(3));
        Assert.Equal(HttpStatusCode.Created, r1.StatusCode);

        var r2 = await AssignAsync(tool.Id, await CreateRequestIdAsync(), start.AddDays(5), start.AddDays(8));
        Assert.Equal(HttpStatusCode.Created, r2.StatusCode);
    }

    [Fact]
    public async Task ExclusiveTool_WithAllocationPercent_Returns409()
    {
        var tool = await CreateToolAsync($"Tool-D-{Guid.NewGuid():N}"[..20]);
        var start = DateTime.UtcNow.AddDays(20);

        var resp = await _client.PostAsJsonAsync("/api/resource-assignments", new CreateResourceAssignmentRequest
        {
            ResourceId = tool.Id,
            RequestId = await CreateRequestIdAsync(),
            StartUtc = start,
            EndUtc = start.AddDays(2),
            AllocationPercent = 50m,
        });
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
    }
}
