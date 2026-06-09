using System.Net.Http.Json;
using System.Text.Json;
using Api.Constants;
using Api.Models;
using Xunit;

namespace Orkyo.Foundation.Tests.Endpoints;

/// <summary>
/// Integration coverage for the scoped-grid + conflicts-registry SQL that unit tests (which mock
/// the repo/validator) can't reach: the site + time-window overlap predicate of
/// <c>GET /api/sites/{siteId}/requests</c>, the unscheduled backlog filter
/// (<c>GET /api/requests?scheduled=false</c>), and that <c>GET /api/conflicts</c> surfaces a real
/// seeded conflict end-to-end. The shared test DB persists across tests, so every assertion is
/// membership-based (about the rows this test seeds), never absolute counts.
/// </summary>
[Collection("Database collection")]
public class ScopedGridEndpointsTests
{
    private readonly HttpClient _client;

    public ScopedGridEndpointsTests(DatabaseFixture fixture)
    {
        _client = fixture.CreateAuthorizedClient();
    }

    // Far-future, per-run-unique base day so seeded bars never collide with other tests' dates.
    private static readonly DateTime BaseDay = new DateTime(2031, 1, 1, 9, 0, 0, DateTimeKind.Utc)
        .AddMinutes(Random.Shared.Next(0, 500_000));

    private async Task<Guid> CreateRequestAsync(List<CreateRequestRequirementRequest>? requirements = null)
    {
        var resp = await _client.PostAsJsonAsync("/api/requests", new CreateRequestRequest
        {
            Name = $"Scoped-{Guid.NewGuid():N}"[..25],
            MinimalDurationValue = 1,
            MinimalDurationUnit = DurationUnit.Hours,
            SchedulingSettingsApply = false,
            Requirements = requirements,
        });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<RequestInfo>())!.Id;
    }

    private async Task ScheduleAsync(Guid requestId, Guid spaceId, DateTime start, DateTime end)
    {
        var resp = await _client.PatchAsJsonAsync(
            $"/api/requests/{requestId}/schedule",
            new ScheduleRequestRequest { ResourceId = spaceId, StartTs = start, EndTs = end });
        resp.EnsureSuccessStatusCode();
    }

    private async Task<Guid> CreateSpaceInSiteAsync(Guid siteId)
    {
        var code = $"SCOPED-{Guid.NewGuid():N}"[..15];
        var resp = await _client.PostAsJsonAsync($"/api/sites/{siteId}/spaces", new CreateSpaceRequest
        {
            Name = $"Scoped Space {code}",
            Code = code,
            IsPhysical = false,
            Geometry = null,
        });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<SpaceInfo>())!.Id;
    }

    private async Task<HashSet<Guid>> GetSiteWindowIdsAsync(Guid siteId, DateTime from, DateTime to)
    {
        var resp = await _client.GetAsync(
            $"/api/sites/{siteId}/requests?from={from:o}&to={to:o}");
        resp.EnsureSuccessStatusCode();
        var list = await resp.Content.ReadFromJsonAsync<List<RequestInfo>>();
        return list!.Select(r => r.Id).ToHashSet();
    }

    [Fact]
    public async Task SiteWindow_ReturnsOnlyRequestsWhoseBarOverlapsTheWindow()
    {
        var siteId = await TestHelpers.GetOrCreateTestSite(_client);
        var spaceId = await TestHelpers.CreateUniqueTestSpace(_client); // in the test site

        var inWindow = await CreateRequestAsync();
        var outOfWindow = await CreateRequestAsync();
        await ScheduleAsync(inWindow, spaceId, BaseDay, BaseDay.AddHours(2));
        await ScheduleAsync(outOfWindow, spaceId, BaseDay.AddDays(30), BaseDay.AddDays(30).AddHours(2));

        var ids = await GetSiteWindowIdsAsync(siteId, BaseDay.AddDays(-1), BaseDay.AddDays(1));

        Assert.Contains(inWindow, ids);
        Assert.DoesNotContain(outOfWindow, ids);
    }

    [Fact]
    public async Task SiteWindow_IncludesBarTouchingTheWindowStart_AndExcludesOtherSites()
    {
        var siteId = await TestHelpers.GetOrCreateTestSite(_client);
        var spaceId = await TestHelpers.CreateUniqueTestSpace(_client);

        // A bar ending exactly at `from` overlaps (predicate is end_ts >= from).
        var touchesStart = await CreateRequestAsync();
        var windowStart = BaseDay.AddDays(100);
        await ScheduleAsync(touchesStart, spaceId, windowStart.AddHours(-2), windowStart);

        // A bar on a different site within the same window must be excluded by the site join.
        var otherSiteResp = await _client.PostAsJsonAsync(
            "/api/sites", new CreateSiteRequest($"SC{Guid.NewGuid():N}"[..8], "Scoped Other Site", null, null));
        otherSiteResp.EnsureSuccessStatusCode();
        var otherSiteId = (await otherSiteResp.Content.ReadFromJsonAsync<SiteInfo>())!.Id;
        var otherSpaceId = await CreateSpaceInSiteAsync(otherSiteId);
        var otherSiteRequest = await CreateRequestAsync();
        await ScheduleAsync(otherSiteRequest, otherSpaceId, windowStart.AddMinutes(30), windowStart.AddHours(1));

        var ids = await GetSiteWindowIdsAsync(siteId, windowStart, windowStart.AddDays(1));

        Assert.Contains(touchesStart, ids);
        Assert.DoesNotContain(otherSiteRequest, ids);
    }

    [Fact]
    public async Task Backlog_ReturnsUnscheduledRequests_AndExcludesScheduledOnes()
    {
        var spaceId = await TestHelpers.CreateUniqueTestSpace(_client);

        var unscheduled = await CreateRequestAsync();
        var scheduled = await CreateRequestAsync();
        await ScheduleAsync(scheduled, spaceId, BaseDay.AddDays(200), BaseDay.AddDays(200).AddHours(2));

        var resp = await _client.GetAsync("/api/requests?scheduled=false");
        resp.EnsureSuccessStatusCode();
        var ids = (await resp.Content.ReadFromJsonAsync<List<RequestInfo>>())!.Select(r => r.Id).ToHashSet();

        Assert.Contains(unscheduled, ids);
        Assert.DoesNotContain(scheduled, ids);
    }

    [Fact]
    public async Task Registry_SurfacesConnectorMismatch_WhenScheduledSpaceLacksRequiredCapability()
    {
        // A freshly created space has no capabilities, so any requirement is unsatisfiable.
        var spaceId = await TestHelpers.CreateUniqueTestSpace(_client);
        var criteria = await TestHelpers.GetAvailableCriteria(_client);
        var criterion = Assert.Single(criteria, c => c.Name == "seed_boolean");

        var requestId = await CreateRequestAsync([
            new CreateRequestRequirementRequest
            {
                CriterionId = criterion.Id,
                Value = JsonSerializer.SerializeToElement(true),
            },
        ]);
        // capability.missing is a soft constraint → scheduling onto the unqualified space succeeds.
        await ScheduleAsync(requestId, spaceId, BaseDay.AddDays(300), BaseDay.AddDays(300).AddHours(2));

        var resp = await _client.GetAsync("/api/conflicts");
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();

        var entry = body.EnumerateArray()
            .FirstOrDefault(e => e.GetProperty("requestId").GetGuid() == requestId);
        Assert.NotEqual(JsonValueKind.Undefined, entry.ValueKind);
        var kinds = entry.GetProperty("conflicts").EnumerateArray()
            .Select(c => c.GetProperty("kind").GetString())
            .ToList();
        Assert.Contains("connector_mismatch", kinds);
    }
}
