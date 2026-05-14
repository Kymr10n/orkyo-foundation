using System.Net;
using System.Net.Http.Json;
using Api.Models;
using Xunit;

namespace Orkyo.Foundation.Tests.Services;

/// <summary>
/// Verifies fractional allocation rules for Person resources end-to-end via HTTP.
/// </summary>
[Collection("Database collection")]
public class PersonResourceAllocationTests
{
    private readonly HttpClient _client;

    public PersonResourceAllocationTests(DatabaseFixture fixture)
    {
        _client = fixture.CreateAuthorizedClient();
    }

    private async Task<ResourceInfo> CreatePersonAsync(string name, int availabilityPct = 100)
    {
        var resp = await _client.PostAsJsonAsync("/api/resources", new CreateResourceRequest
        {
            ResourceTypeKey = "person",
            Name = name,
            AllocationMode = "Fractional",
            BaseAvailabilityPercent = availabilityPct,
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

    private Task<HttpResponseMessage> AssignAsync(Guid resourceId, Guid requestId, decimal? pct, DateTime start, DateTime end)
        => _client.PostAsJsonAsync("/api/resource-assignments", new CreateResourceAssignmentRequest
        {
            ResourceId = resourceId,
            RequestId = requestId,
            StartUtc = start,
            EndUtc = end,
            AllocationPercent = pct,
        });

    [Fact]
    public async Task FractionalPerson_50Pct_CanBeAssignedTwice()
    {
        var person = await CreatePersonAsync($"P50-{Guid.NewGuid():N}"[..20], availabilityPct: 100);
        var start = DateTime.UtcNow.AddDays(1);
        var end = start.AddDays(5);

        var r1 = await AssignAsync(person.Id, await CreateRequestIdAsync(), 50m, start, end);
        Assert.Equal(HttpStatusCode.Created, r1.StatusCode);

        var r2 = await AssignAsync(person.Id, await CreateRequestIdAsync(), 50m, start, end);
        Assert.Equal(HttpStatusCode.Created, r2.StatusCode);
    }

    [Fact]
    public async Task FractionalPerson_ExceedsCapacity_Returns409()
    {
        var person = await CreatePersonAsync($"PCap-{Guid.NewGuid():N}"[..20], availabilityPct: 100);
        var start = DateTime.UtcNow.AddDays(10);
        var end = start.AddDays(5);

        await AssignAsync(person.Id, await CreateRequestIdAsync(), 60m, start, end);
        await AssignAsync(person.Id, await CreateRequestIdAsync(), 30m, start, end);

        // 60 + 30 = 90 already used; 20 more → exceeds 100
        var r = await AssignAsync(person.Id, await CreateRequestIdAsync(), 20m, start, end);
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
    }

    [Fact]
    public async Task FractionalPerson_MissingAllocationPct_Returns409()
    {
        var person = await CreatePersonAsync($"PNoPct-{Guid.NewGuid():N}"[..20]);
        var start = DateTime.UtcNow.AddDays(20);

        var r = await AssignAsync(person.Id, await CreateRequestIdAsync(), null, start, start.AddDays(3));
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
    }

    [Fact]
    public async Task FractionalPerson_ZeroAllocationPct_Returns409()
    {
        var person = await CreatePersonAsync($"PZero-{Guid.NewGuid():N}"[..20]);
        var start = DateTime.UtcNow.AddDays(25);

        var r = await AssignAsync(person.Id, await CreateRequestIdAsync(), 0m, start, start.AddDays(3));
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
    }
}
