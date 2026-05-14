using System.Net;
using System.Net.Http.Json;
using Api.Models;
using Xunit;

namespace Orkyo.Foundation.Tests.Endpoints;

[Collection("Database collection")]
public class ResourceAssignmentEndpointTests
{
    private readonly HttpClient _client;

    public ResourceAssignmentEndpointTests(DatabaseFixture databaseFixture)
    {
        _client = databaseFixture.CreateAuthorizedClient();
    }

    private async Task<ResourceInfo> CreateExclusiveResource()
    {
        var r = new CreateResourceRequest
        {
            ResourceTypeKey = "tool",
            Name = $"Tool-{Guid.NewGuid():N}"[..20],
            AllocationMode = "Exclusive",
        };
        var resp = await _client.PostAsJsonAsync("/api/resources", r);
        return (await resp.Content.ReadFromJsonAsync<ResourceInfo>())!;
    }

    private async Task<ResourceInfo> CreateFractionalResource(int basePct = 100)
    {
        var r = new CreateResourceRequest
        {
            ResourceTypeKey = "person",
            Name = $"Person-{Guid.NewGuid():N}"[..20],
            AllocationMode = "Fractional",
            BaseAvailabilityPercent = basePct,
        };
        var resp = await _client.PostAsJsonAsync("/api/resources", r);
        return (await resp.Content.ReadFromJsonAsync<ResourceInfo>())!;
    }

    private async Task<Guid> GetTestRequestIdAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/requests", new
        {
            Name = $"Test-{Guid.NewGuid():N}"[..20],
            MinimalDurationValue = 1,
            MinimalDurationUnit = "hours",
            SchedulingSettingsApply = false,
        });
        var obj = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        return obj.GetProperty("id").GetGuid();
    }

    private CreateResourceAssignmentRequest MakeRequest(Guid resourceId, Guid requestId, DateTime? start = null, DateTime? end = null, decimal? pct = null)
    {
        var s = start ?? new DateTime(2026, 6, 1, 9, 0, 0, DateTimeKind.Utc);
        var e = end ?? new DateTime(2026, 6, 1, 17, 0, 0, DateTimeKind.Utc);
        return new CreateResourceAssignmentRequest
        {
            RequestId = requestId,
            ResourceId = resourceId,
            StartUtc = s,
            EndUtc = e,
            AllocationPercent = pct,
        };
    }

    [Fact]
    public async Task CreateAssignment_Exclusive_HappyPath_Returns201()
    {
        var resource = await CreateExclusiveResource();
        var requestId = await GetTestRequestIdAsync();
        var response = await _client.PostAsJsonAsync("/api/resource-assignments", MakeRequest(resource.Id, requestId));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var a = await response.Content.ReadFromJsonAsync<ResourceAssignmentInfo>();
        Assert.Equal(resource.Id, a!.ResourceId);
        Assert.Equal("Planned", a.AssignmentStatus);
    }

    [Fact]
    public async Task CreateAssignment_Exclusive_OverlapReturns409()
    {
        var resource = await CreateExclusiveResource();
        var requestId1 = await GetTestRequestIdAsync();
        var requestId2 = await GetTestRequestIdAsync();
        await _client.PostAsJsonAsync("/api/resource-assignments", MakeRequest(resource.Id, requestId1));

        var conflictResp = await _client.PostAsJsonAsync("/api/resource-assignments", MakeRequest(resource.Id, requestId2));
        Assert.Equal(HttpStatusCode.Conflict, conflictResp.StatusCode);
    }

    [Fact]
    public async Task CreateAssignment_Exclusive_AdjacentSlots_BothAllowed()
    {
        var resource = await CreateExclusiveResource();
        var start = new DateTime(2026, 7, 1, 9, 0, 0, DateTimeKind.Utc);
        var req1 = await GetTestRequestIdAsync();
        var req2 = await GetTestRequestIdAsync();

        var r1 = await _client.PostAsJsonAsync("/api/resource-assignments",
            MakeRequest(resource.Id, req1, start, start.AddHours(2)));
        var r2 = await _client.PostAsJsonAsync("/api/resource-assignments",
            MakeRequest(resource.Id, req2, start.AddHours(2), start.AddHours(4)));

        Assert.Equal(HttpStatusCode.Created, r1.StatusCode);
        Assert.Equal(HttpStatusCode.Created, r2.StatusCode);
    }

    [Fact]
    public async Task CreateAssignment_Fractional_WithinCapacity_Returns201()
    {
        var resource = await CreateFractionalResource(100);
        var req1 = await GetTestRequestIdAsync();
        var req2 = await GetTestRequestIdAsync();
        var r1 = await _client.PostAsJsonAsync("/api/resource-assignments",
            MakeRequest(resource.Id, req1, pct: 30));
        var r2 = await _client.PostAsJsonAsync("/api/resource-assignments",
            MakeRequest(resource.Id, req2, pct: 50));

        Assert.Equal(HttpStatusCode.Created, r1.StatusCode);
        Assert.Equal(HttpStatusCode.Created, r2.StatusCode);
    }

    [Fact]
    public async Task CreateAssignment_Fractional_ExceedsCapacity_Returns409()
    {
        var resource = await CreateFractionalResource(100);
        var req1 = await GetTestRequestIdAsync();
        var req2 = await GetTestRequestIdAsync();
        await _client.PostAsJsonAsync("/api/resource-assignments", MakeRequest(resource.Id, req1, pct: 60));
        var over = await _client.PostAsJsonAsync("/api/resource-assignments",
            MakeRequest(resource.Id, req2, pct: 50));
        Assert.Equal(HttpStatusCode.Conflict, over.StatusCode);
    }

    [Fact]
    public async Task CreateAssignment_Fractional_ReducedBase_ExceedsCapacity_Returns409()
    {
        var resource = await CreateFractionalResource(80);
        var req1 = await GetTestRequestIdAsync();
        var req2 = await GetTestRequestIdAsync();
        await _client.PostAsJsonAsync("/api/resource-assignments", MakeRequest(resource.Id, req1, pct: 30));
        var over = await _client.PostAsJsonAsync("/api/resource-assignments",
            MakeRequest(resource.Id, req2, pct: 60));
        Assert.Equal(HttpStatusCode.Conflict, over.StatusCode);
    }

    [Fact]
    public async Task CancelAssignment_RemovesItFromConflictDetection()
    {
        var resource = await CreateExclusiveResource();
        var requestId = await GetTestRequestIdAsync();

        var first = await _client.PostAsJsonAsync("/api/resource-assignments", MakeRequest(resource.Id, requestId));
        var a1 = (await first.Content.ReadFromJsonAsync<ResourceAssignmentInfo>())!;

        await _client.DeleteAsync($"/api/resource-assignments/{a1.Id}");

        var requestId2 = await GetTestRequestIdAsync();
        var second = await _client.PostAsJsonAsync("/api/resource-assignments", MakeRequest(resource.Id, requestId2));
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
    }

    [Fact]
    public async Task CreateAssignment_InactiveResource_Returns409()
    {
        var resource = await CreateExclusiveResource();
        var requestId = await GetTestRequestIdAsync();
        await _client.DeleteAsync($"/api/resources/{resource.Id}");

        var response = await _client.PostAsJsonAsync("/api/resource-assignments", MakeRequest(resource.Id, requestId));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }
}
