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

    // ── site model (request site_id, implicit site-on-schedule, site-scoped feeds) ──

    [Fact]
    public async Task GetScheduled_SiteScopedSpacelessRequest_AppearsForItsSite()
    {
        // The orphan fix: a request scoped to a site with a time but NO space must still show on
        // that site's calendar feed (previously it was excluded for lacking a space assignment).
        var siteId = await TestHelpers.GetOrCreateTestSite(_client);
        var start = DateTime.UtcNow.Date.AddDays(3).AddHours(9);
        var end = start.AddHours(2);

        var createResp = await _client.PostAsJsonAsync("/api/requests", new CreateRequestRequest
        {
            Name = $"Sited-{Guid.NewGuid():N}"[..20],
            SiteId = siteId,
            StartTs = start,
            EndTs = end,
            MinimalDurationValue = 2,
            MinimalDurationUnit = DurationUnit.Hours,
            SchedulingSettingsApply = false,
        });
        createResp.EnsureSuccessStatusCode();
        var created = (await createResp.Content.ReadFromJsonAsync<RequestInfo>())!;
        Assert.Equal(siteId, created.SiteId);
        Assert.Empty(created.Assignments); // no space

        var feedResp = await _client.GetAsync(
            $"/api/sites/{siteId}/requests?from={start.AddHours(-1):o}&to={end.AddHours(1):o}");
        feedResp.EnsureSuccessStatusCode();
        var feed = await feedResp.Content.ReadFromJsonAsync<List<RequestInfo>>();
        Assert.Contains(feed!, r => r.Id == created.Id);
    }

    [Fact]
    public async Task Schedule_SiteNeutralRequestIntoSpace_AdoptsSpaceSite()
    {
        // Implicit site-on-schedule: a site-neutral (NULL) request adopts the space's site when placed.
        var siteId = await TestHelpers.GetOrCreateTestSite(_client);
        var spaceId = await TestHelpers.GetOrCreateTestSpace(_client);
        var neutralId = await CreateUnscheduledRequestAsync();

        // Sanity: starts site-neutral.
        var before = await (await _client.GetAsync($"/api/requests/{neutralId}")).Content.ReadFromJsonAsync<RequestInfo>();
        Assert.Null(before!.SiteId);

        var schedResp = await _client.PatchAsJsonAsync($"/api/requests/{neutralId}/schedule",
            new ScheduleRequestRequest
            {
                ResourceId = spaceId,
                StartTs = DateTime.UtcNow.AddDays(1),
                EndTs = DateTime.UtcNow.AddDays(1).AddHours(2),
            });
        schedResp.EnsureSuccessStatusCode();

        var after = await (await _client.GetAsync($"/api/requests/{neutralId}")).Content.ReadFromJsonAsync<RequestInfo>();
        Assert.Equal(siteId, after!.SiteId);
    }

    [Fact]
    public async Task Update_ClearSiteWithChangeSiteId_ResetsToSiteNeutral()
    {
        // Re-scoping a site-scoped request back to "any site" (NULL): the FE sends siteId=null with
        // changeSiteId=true so the backend can distinguish "clear" from "absent". Without the flag a
        // null siteId is preserved (the omit-on-unchanged contract — see buildUpdatePayload).
        var siteId = await TestHelpers.GetOrCreateTestSite(_client);
        var createResp = await _client.PostAsJsonAsync("/api/requests", new CreateRequestRequest
        {
            Name = $"ClearSite-{Guid.NewGuid():N}"[..20],
            SiteId = siteId,
            MinimalDurationValue = 1,
            MinimalDurationUnit = DurationUnit.Hours,
            SchedulingSettingsApply = false,
        });
        createResp.EnsureSuccessStatusCode();
        var id = (await createResp.Content.ReadFromJsonAsync<RequestInfo>())!.Id;

        // A null SiteId WITHOUT the flag preserves the existing site (here paired with a name edit so
        // the update isn't empty).
        var preserve = await _client.PutAsJsonAsync($"/api/requests/{id}", new UpdateRequestRequest
        {
            Name = $"Renamed-{Guid.NewGuid():N}"[..20],
            SiteId = null,
            ChangeSiteId = false,
        });
        preserve.EnsureSuccessStatusCode();
        var afterPreserve = await (await _client.GetAsync($"/api/requests/{id}")).Content.ReadFromJsonAsync<RequestInfo>();
        Assert.Equal(siteId, afterPreserve!.SiteId);

        // A null SiteId WITH the flag clears it to site-neutral.
        var cleared = await _client.PutAsJsonAsync($"/api/requests/{id}", new UpdateRequestRequest
        {
            SiteId = null,
            ChangeSiteId = true,
        });
        cleared.EnsureSuccessStatusCode();
        var afterClear = await (await _client.GetAsync($"/api/requests/{id}")).Content.ReadFromJsonAsync<RequestInfo>();
        Assert.Null(afterClear!.SiteId);
    }

    [Fact]
    public async Task GetUnscheduled_WithSite_IncludesSiteScopedAndNeutral()
    {
        var siteId = await TestHelpers.GetOrCreateTestSite(_client);
        var neutralId = await CreateUnscheduledRequestAsync(); // site-neutral leaf, no start

        var scopedResp = await _client.PostAsJsonAsync("/api/requests", new CreateRequestRequest
        {
            Name = $"ScopedBacklog-{Guid.NewGuid():N}"[..20],
            SiteId = siteId,
            MinimalDurationValue = 1,
            MinimalDurationUnit = DurationUnit.Hours,
            SchedulingSettingsApply = false,
        });
        scopedResp.EnsureSuccessStatusCode();
        var scopedId = (await scopedResp.Content.ReadFromJsonAsync<RequestInfo>())!.Id;

        var resp = await _client.GetAsync($"/api/requests?scheduled=false&siteId={siteId}");
        resp.EnsureSuccessStatusCode();
        var backlog = await resp.Content.ReadFromJsonAsync<List<RequestInfo>>();

        Assert.Contains(backlog!, r => r.Id == neutralId); // site-neutral is schedulable anywhere
        Assert.Contains(backlog!, r => r.Id == scopedId);  // this site's own backlog
    }

    [Fact]
    public async Task GetUnscheduled_ExcludesGroupsAndIncludesLeaves()
    {
        // A schedulable leaf with no start_ts…
        var leafId = await CreateUnscheduledRequestAsync();

        // …and an unscheduled group (summary): its null start_ts is derived, not unscheduled.
        var groupResp = await _client.PostAsJsonAsync("/api/requests", new CreateRequestRequest
        {
            Name = $"Group-{Guid.NewGuid():N}"[..25],
            PlanningMode = PlanningMode.Summary,
            StartTs = null,
            EndTs = null,
            MinimalDurationValue = 1,
            MinimalDurationUnit = DurationUnit.Hours,
            SchedulingSettingsApply = false,
        });
        groupResp.EnsureSuccessStatusCode();
        var groupId = (await groupResp.Content.ReadFromJsonAsync<RequestInfo>())!.Id;

        var resp = await _client.GetAsync("/api/requests?scheduled=false");
        resp.EnsureSuccessStatusCode();
        var backlog = await resp.Content.ReadFromJsonAsync<List<RequestInfo>>();

        Assert.NotNull(backlog);
        Assert.Contains(backlog, r => r.Id == leafId);
        Assert.DoesNotContain(backlog, r => r.Id == groupId);
    }
}
