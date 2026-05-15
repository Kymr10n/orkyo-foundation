using System.Net;
using System.Net.Http.Json;
using Api.Constants;
using Api.Models;
using Xunit;

namespace Orkyo.Foundation.Tests.Repositories;

/// <summary>
/// Verifies Phase 6 read-model unification: the v_requests_with_assignments view
/// populates RequestInfo.Assignments correctly across all resource types.
/// Tests use the full HTTP stack (same as RequestEndpointsTests) so the mapper,
/// view, and JSON deserialization are all exercised end-to-end.
/// </summary>
[Collection("Database collection")]
public class RequestRepositoryTests
{
    private readonly HttpClient _client;

    public RequestRepositoryTests(DatabaseFixture fixture)
    {
        _client = fixture.CreateAuthorizedClient();
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private async Task<Guid> CreateUnscheduledRequestAsync()
    {
        var resp = await _client.PostAsJsonAsync("/api/requests", new CreateRequestRequest
        {
            Name = $"RepoTest-{Guid.NewGuid():N}"[..25],
            MinimalDurationValue = 1,
            MinimalDurationUnit = DurationUnit.Hours,
            SchedulingSettingsApply = false,
        });
        resp.EnsureSuccessStatusCode();
        var created = await resp.Content.ReadFromJsonAsync<RequestInfo>();
        return created!.Id;
    }

    private async Task<Guid> CreatePersonResourceAsync()
    {
        var resp = await _client.PostAsJsonAsync("/api/resources", new CreateResourceRequest
        {
            ResourceTypeKey = ResourceTypeKeys.Person,
            Name = $"Person-{Guid.NewGuid():N}"[..20],
            AllocationMode = AllocationModes.Fractional,
            BaseAvailabilityPercent = 100,
        });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<ResourceInfo>())!.Id;
    }

    private Task<HttpResponseMessage> AssignPersonAsync(Guid personId, Guid requestId)
        => _client.PostAsJsonAsync("/api/resource-assignments", new CreateResourceAssignmentRequest
        {
            ResourceId = personId,
            RequestId = requestId,
            StartUtc = DateTime.UtcNow.AddDays(1),
            EndUtc = DateTime.UtcNow.AddDays(2),
            AllocationPercent = 100,
        });

    private async Task<RequestInfo> GetRequestAsync(Guid id)
    {
        var resp = await _client.GetAsync($"/api/requests/{id}");
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<RequestInfo>())!;
    }

    // ── tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetById_UnassignedRequest_ReturnsEmptyAssignments()
    {
        var requestId = await CreateUnscheduledRequestAsync();

        var result = await GetRequestAsync(requestId);

        Assert.NotNull(result.Assignments);
        Assert.Empty(result.Assignments);
        Assert.False(result.IsScheduled);
    }

    [Fact]
    public async Task GetById_ScheduledRequest_ReturnsSpaceAssignment()
    {
        var spaceId = await TestHelpers.GetOrCreateTestSpace(_client);
        var requestId = await CreateUnscheduledRequestAsync();

        var scheduleResp = await _client.PatchAsJsonAsync(
            $"/api/requests/{requestId}/schedule",
            new ScheduleRequestRequest
            {
                ResourceId = spaceId,
                StartTs = DateTime.UtcNow.AddDays(1),
                EndTs = DateTime.UtcNow.AddDays(2),
            });
        scheduleResp.EnsureSuccessStatusCode();

        var result = await GetRequestAsync(requestId);

        var spaceAssignment = Assert.Single(result.Assignments);
        Assert.Equal(ResourceTypeKeys.Space, spaceAssignment.ResourceTypeKey);
        Assert.Equal(spaceId, spaceAssignment.ResourceId);
        Assert.True(result.IsScheduled);
    }

    [Fact]
    public async Task GetById_ReturnsAllAssignmentTypes_SpaceAndPerson()
    {
        // Schedule a request to a space, then add a person assignment.
        // Verifies that the view surfaces both (Phase 4 person assignments
        // were previously invisible in API responses).
        var spaceId = await TestHelpers.GetOrCreateTestSpace(_client);
        var personId = await CreatePersonResourceAsync();
        var requestId = await CreateUnscheduledRequestAsync();

        var scheduleResp = await _client.PatchAsJsonAsync(
            $"/api/requests/{requestId}/schedule",
            new ScheduleRequestRequest
            {
                ResourceId = spaceId,
                StartTs = DateTime.UtcNow.AddDays(1),
                EndTs = DateTime.UtcNow.AddDays(2),
            });
        scheduleResp.EnsureSuccessStatusCode();

        var assignResp = await AssignPersonAsync(personId, requestId);
        Assert.Equal(HttpStatusCode.Created, assignResp.StatusCode);

        var result = await GetRequestAsync(requestId);

        Assert.Equal(2, result.Assignments.Count);

        var spaceAssignment = result.Assignments.SingleOrDefault(a => a.ResourceTypeKey == ResourceTypeKeys.Space);
        Assert.NotNull(spaceAssignment);
        Assert.Equal(spaceId, spaceAssignment.ResourceId);

        var personAssignment = result.Assignments.SingleOrDefault(a => a.ResourceTypeKey == ResourceTypeKeys.Person);
        Assert.NotNull(personAssignment);
        Assert.Equal(personId, personAssignment.ResourceId);
    }

    [Fact]
    public async Task GetById_ExcludesCancelledAssignments()
    {
        var spaceId = await TestHelpers.GetOrCreateTestSpace(_client);
        var requestId = await CreateUnscheduledRequestAsync();

        // Schedule request (creates a space assignment).
        var scheduleResp = await _client.PatchAsJsonAsync(
            $"/api/requests/{requestId}/schedule",
            new ScheduleRequestRequest
            {
                ResourceId = spaceId,
                StartTs = DateTime.UtcNow.AddDays(1),
                EndTs = DateTime.UtcNow.AddDays(2),
            });
        scheduleResp.EnsureSuccessStatusCode();

        // Unschedule (cancels the space assignment).
        var unscheduleResp = await _client.PatchAsJsonAsync(
            $"/api/requests/{requestId}/schedule",
            new ScheduleRequestRequest { ResourceId = null, StartTs = null, EndTs = null });
        unscheduleResp.EnsureSuccessStatusCode();

        var result = await GetRequestAsync(requestId);

        Assert.Empty(result.Assignments);
        Assert.False(result.IsScheduled);
    }

    [Fact]
    public async Task GetAll_ReturnsAssignmentsForAllRequests()
    {
        var spaceId = await TestHelpers.GetOrCreateTestSpace(_client);

        // Create a scheduled and an unscheduled request.
        var unscheduledId = await CreateUnscheduledRequestAsync();
        var scheduledId = await CreateUnscheduledRequestAsync();
        await _client.PatchAsJsonAsync(
            $"/api/requests/{scheduledId}/schedule",
            new ScheduleRequestRequest
            {
                ResourceId = spaceId,
                StartTs = DateTime.UtcNow.AddDays(1),
                EndTs = DateTime.UtcNow.AddDays(2),
            });

        var allResp = await _client.GetAsync("/api/requests");
        allResp.EnsureSuccessStatusCode();
        var all = await allResp.Content.ReadFromJsonAsync<List<RequestInfo>>();

        var unscheduled = all!.FirstOrDefault(r => r.Id == unscheduledId);
        var scheduled = all!.FirstOrDefault(r => r.Id == scheduledId);

        Assert.NotNull(unscheduled);
        Assert.Empty(unscheduled.Assignments);

        Assert.NotNull(scheduled);
        Assert.Single(scheduled.Assignments);
        Assert.Equal(ResourceTypeKeys.Space, scheduled.Assignments[0].ResourceTypeKey);
    }
}
