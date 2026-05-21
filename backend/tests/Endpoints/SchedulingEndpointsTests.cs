using System.Net;
using System.Net.Http.Json;
using Api.Models;

namespace Orkyo.Foundation.Tests.Endpoints;

[Collection("Database collection")]
public class SchedulingEndpointsTests
{
    private readonly HttpClient _client;

    public SchedulingEndpointsTests(DatabaseFixture databaseFixture)
    {
        _client = databaseFixture.CreateAuthorizedClient();
    }

    private async Task<Guid> GetOrCreateSiteId()
    {
        var response = await _client.GetAsync("/api/sites");
        var sites = await response.Content.ReadFromJsonAsync<List<SiteInfo>>();
        if (sites != null && sites.Count > 0)
            return sites[0].Id;

        // Create a site if none exists
        var createResponse = await _client.PostAsJsonAsync("/api/sites", new
        {
            Code = $"sched-{Guid.NewGuid():N}"[..20],
            Name = "Scheduling Test Site"
        });
        var created = await createResponse.Content.ReadFromJsonAsync<SiteInfo>();
        return created!.Id;
    }

    #region Scheduling Settings

    [Fact]
    public async Task GetSchedulingSettings_NoSettings_ReturnsDefaults()
    {
        var siteId = await GetOrCreateSiteId();

        // Delete in case a previous test created settings
        await _client.DeleteAsync($"/api/sites/{siteId}/scheduling");

        var response = await _client.GetAsync($"/api/sites/{siteId}/scheduling");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var settings = await response.Content.ReadFromJsonAsync<SchedulingSettingsInfo>();
        Assert.NotNull(settings);
        Assert.Equal(Guid.Empty, settings.Id);
        Assert.Equal(siteId, settings.SiteId);
        Assert.Equal("UTC", settings.TimeZone);
        Assert.False(settings.WorkingHoursEnabled);
        Assert.True(settings.WeekendsEnabled);
        Assert.False(settings.PublicHolidaysEnabled);
    }

    [Fact]
    public async Task UpsertSchedulingSettings_Creates_Returns200()
    {
        var siteId = await GetOrCreateSiteId();

        var request = new UpsertSchedulingSettingsRequest
        {
            TimeZone = "Europe/Berlin",
            WorkingHoursEnabled = true,
            WorkingDayStart = "09:00",
            WorkingDayEnd = "18:00",
            WeekendsEnabled = false,
            PublicHolidaysEnabled = false
        };

        var response = await _client.PutAsJsonAsync($"/api/sites/{siteId}/scheduling", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var settings = await response.Content.ReadFromJsonAsync<SchedulingSettingsInfo>();
        Assert.NotNull(settings);
        Assert.Equal(siteId, settings.SiteId);
        Assert.Equal("Europe/Berlin", settings.TimeZone);
        Assert.True(settings.WorkingHoursEnabled);
        Assert.Equal(new TimeOnly(9, 0), settings.WorkingDayStart);
        Assert.Equal(new TimeOnly(18, 0), settings.WorkingDayEnd);
        Assert.False(settings.WeekendsEnabled);
    }

    [Fact]
    public async Task UpsertSchedulingSettings_Updates_Returns200()
    {
        var siteId = await GetOrCreateSiteId();

        // Create initial settings
        await _client.PutAsJsonAsync($"/api/sites/{siteId}/scheduling",
            new UpsertSchedulingSettingsRequest
            {
                TimeZone = "UTC",
                WorkingHoursEnabled = true,
                WorkingDayStart = "08:00",
                WorkingDayEnd = "17:00"
            });

        // Update
        var request = new UpsertSchedulingSettingsRequest
        {
            TimeZone = "America/New_York",
            WorkingHoursEnabled = true,
            WorkingDayStart = "07:00",
            WorkingDayEnd = "16:00",
            WeekendsEnabled = true
        };

        var response = await _client.PutAsJsonAsync($"/api/sites/{siteId}/scheduling", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var settings = await response.Content.ReadFromJsonAsync<SchedulingSettingsInfo>();
        Assert.NotNull(settings);
        Assert.Equal("America/New_York", settings.TimeZone);
        Assert.Equal(new TimeOnly(7, 0), settings.WorkingDayStart);
    }

    [Fact]
    public async Task UpsertSchedulingSettings_InvalidTimezone_Returns400()
    {
        var siteId = await GetOrCreateSiteId();

        var request = new UpsertSchedulingSettingsRequest { TimeZone = "Invalid/Zone" };

        var response = await _client.PutAsJsonAsync($"/api/sites/{siteId}/scheduling", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetSchedulingSettings_AfterCreate_ReturnsSettings()
    {
        var siteId = await GetOrCreateSiteId();

        await _client.PutAsJsonAsync($"/api/sites/{siteId}/scheduling",
            new UpsertSchedulingSettingsRequest
            {
                TimeZone = "UTC",
                WorkingHoursEnabled = true,
                WorkingDayStart = "08:00",
                WorkingDayEnd = "17:00"
            });

        var response = await _client.GetAsync($"/api/sites/{siteId}/scheduling");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var settings = await response.Content.ReadFromJsonAsync<SchedulingSettingsInfo>();
        Assert.NotNull(settings);
        Assert.Equal("UTC", settings.TimeZone);
    }

    [Fact]
    public async Task DeleteSchedulingSettings_AfterCreate_Returns204()
    {
        var siteId = await GetOrCreateSiteId();

        await _client.PutAsJsonAsync($"/api/sites/{siteId}/scheduling",
            new UpsertSchedulingSettingsRequest { TimeZone = "UTC" });

        var response = await _client.DeleteAsync($"/api/sites/{siteId}/scheduling");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // Verify deleted — GET now returns defaults, not 404
        var getResponse = await _client.GetAsync($"/api/sites/{siteId}/scheduling");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var settings = await getResponse.Content.ReadFromJsonAsync<SchedulingSettingsInfo>();
        Assert.NotNull(settings);
        Assert.Equal(Guid.Empty, settings.Id);
        Assert.False(settings.WorkingHoursEnabled);
    }

    #endregion

    #region Availability Events

    [Fact]
    public async Task GetAvailabilityEvents_Empty_ReturnsEmptyList()
    {
        var siteId = await GetOrCreateSiteId();

        var response = await _client.GetAsync($"/api/sites/{siteId}/availability-events");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var events = await response.Content.ReadFromJsonAsync<List<AvailabilityEventInfo>>();
        Assert.NotNull(events);
    }

    [Fact]
    public async Task CreateAvailabilityEvent_Valid_Returns201()
    {
        var siteId = await GetOrCreateSiteId();

        var request = new CreateAvailabilityEventRequest
        {
            Title = $"Holiday {Guid.NewGuid():N}"[..30],
            EventType = AvailabilityEventType.PublicHoliday,
            DefaultEffect = DefaultEffect.Closed,
            StartTs = new DateTime(2026, 12, 25, 0, 0, 0, DateTimeKind.Utc),
            EndTs = new DateTime(2026, 12, 26, 0, 0, 0, DateTimeKind.Utc)
        };

        var response = await _client.PostAsJsonAsync(
            $"/api/sites/{siteId}/availability-events", request, TestHelpers.JsonOpts);

        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Created, $"Expected 201 Created, got {response.StatusCode}: {body}");
        var ev = await response.Content.ReadFromJsonAsync<AvailabilityEventInfo>(TestHelpers.JsonOpts);
        Assert.NotNull(ev);
        Assert.Equal(siteId, ev.SiteId);
        Assert.Equal(AvailabilityEventType.PublicHoliday, ev.EventType);
        Assert.Equal(DefaultEffect.Closed, ev.DefaultEffect);
        Assert.True(ev.Enabled);
    }

    [Fact]
    public async Task CreateAvailabilityEvent_EmptyTitle_Returns400()
    {
        var siteId = await GetOrCreateSiteId();

        var request = new CreateAvailabilityEventRequest
        {
            Title = "",
            StartTs = DateTime.UtcNow,
            EndTs = DateTime.UtcNow.AddHours(1)
        };

        var response = await _client.PostAsJsonAsync($"/api/sites/{siteId}/availability-events", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateAvailabilityEvent_EndBeforeStart_Returns400()
    {
        var siteId = await GetOrCreateSiteId();

        var request = new CreateAvailabilityEventRequest
        {
            Title = "Invalid",
            StartTs = DateTime.UtcNow.AddHours(1),
            EndTs = DateTime.UtcNow
        };

        var response = await _client.PostAsJsonAsync($"/api/sites/{siteId}/availability-events", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetAvailabilityEventById_AfterCreate_ReturnsEvent()
    {
        var siteId = await GetOrCreateSiteId();

        var createResponse = await _client.PostAsJsonAsync($"/api/sites/{siteId}/availability-events",
            new CreateAvailabilityEventRequest
            {
                Title = $"Get Test {Guid.NewGuid():N}"[..30],
                StartTs = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
                EndTs = new DateTime(2026, 7, 2, 0, 0, 0, DateTimeKind.Utc)
            }, TestHelpers.JsonOpts);

        var created = await createResponse.Content.ReadFromJsonAsync<AvailabilityEventInfo>(TestHelpers.JsonOpts);

        var response = await _client.GetAsync($"/api/sites/{siteId}/availability-events/{created!.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var ev = await response.Content.ReadFromJsonAsync<AvailabilityEventInfo>(TestHelpers.JsonOpts);
        Assert.NotNull(ev);
        Assert.Equal(created.Id, ev.Id);
    }

    [Fact]
    public async Task GetAvailabilityEventById_NonExistent_Returns404()
    {
        var siteId = await GetOrCreateSiteId();

        var response = await _client.GetAsync($"/api/sites/{siteId}/availability-events/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateAvailabilityEvent_PartialUpdate_Returns200()
    {
        var siteId = await GetOrCreateSiteId();

        var createResponse = await _client.PostAsJsonAsync($"/api/sites/{siteId}/availability-events",
            new CreateAvailabilityEventRequest
            {
                Title = $"Update Test {Guid.NewGuid():N}"[..30],
                StartTs = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
                EndTs = new DateTime(2026, 8, 2, 0, 0, 0, DateTimeKind.Utc)
            }, TestHelpers.JsonOpts);

        var created = await createResponse.Content.ReadFromJsonAsync<AvailabilityEventInfo>(TestHelpers.JsonOpts);

        var updateRequest = new UpdateAvailabilityEventRequest { Title = "Updated Title", Enabled = false };
        var response = await _client.PutAsJsonAsync(
            $"/api/sites/{siteId}/availability-events/{created!.Id}", updateRequest, TestHelpers.JsonOpts);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<AvailabilityEventInfo>(TestHelpers.JsonOpts);
        Assert.NotNull(updated);
        Assert.Equal("Updated Title", updated.Title);
        Assert.False(updated.Enabled);
    }

    [Fact]
    public async Task DeleteAvailabilityEvent_AfterCreate_Returns204()
    {
        var siteId = await GetOrCreateSiteId();

        var createResponse = await _client.PostAsJsonAsync($"/api/sites/{siteId}/availability-events",
            new CreateAvailabilityEventRequest
            {
                Title = $"Delete Test {Guid.NewGuid():N}"[..30],
                StartTs = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
                EndTs = new DateTime(2026, 9, 2, 0, 0, 0, DateTimeKind.Utc)
            }, TestHelpers.JsonOpts);

        var created = await createResponse.Content.ReadFromJsonAsync<AvailabilityEventInfo>(TestHelpers.JsonOpts);

        var response = await _client.DeleteAsync($"/api/sites/{siteId}/availability-events/{created!.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var getResponse = await _client.GetAsync($"/api/sites/{siteId}/availability-events/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task DeleteAvailabilityEvent_NonExistent_Returns404()
    {
        var siteId = await GetOrCreateSiteId();

        var response = await _client.DeleteAsync($"/api/sites/{siteId}/availability-events/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    #endregion

    #region Scheduling + Request Integration

    [Fact]
    public async Task CreateRequest_WithSchedulingAndOffTime_ExtendsEndTs()
    {
        var siteId = await GetOrCreateSiteId();
        var resourceId = await TestHelpers.GetOrCreateTestSpace(_client);

        // 1. Set up scheduling: 08:00-17:00 UTC, no weekends
        await _client.PutAsJsonAsync($"/api/sites/{siteId}/scheduling",
            new UpsertSchedulingSettingsRequest
            {
                TimeZone = "UTC",
                WorkingHoursEnabled = true,
                WorkingDayStart = "08:00",
                WorkingDayEnd = "17:00",
                WeekendsEnabled = true
            });

        // 2. Create a maintenance availability event from 10:00-14:00 on 2027-01-05 (Monday)
        await _client.PostAsJsonAsync($"/api/sites/{siteId}/availability-events",
            new CreateAvailabilityEventRequest
            {
                Title = "Test Maintenance",
                EventType = AvailabilityEventType.Maintenance,
                DefaultEffect = DefaultEffect.Closed,
                StartTs = new DateTime(2027, 1, 5, 10, 0, 0, DateTimeKind.Utc),
                EndTs = new DateTime(2027, 1, 5, 14, 0, 0, DateTimeKind.Utc),
            }, TestHelpers.JsonOpts);

        // 3. Create a request: start 09:00, duration 4 hours, scheduling applied
        var requestPayload = new CreateRequestRequest
        {
            Name = $"Sched Integration {Guid.NewGuid():N}"[..30],
            ResourceId = resourceId,
            StartTs = new DateTime(2027, 1, 5, 9, 0, 0, DateTimeKind.Utc),
            EndTs = new DateTime(2027, 1, 5, 13, 0, 0, DateTimeKind.Utc), // naive: 4h from 09:00
            MinimalDurationValue = 4,
            MinimalDurationUnit = DurationUnit.Hours,
            SchedulingSettingsApply = true
        };

        var response = await _client.PostAsJsonAsync("/api/requests", requestPayload);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = await response.Content.ReadFromJsonAsync<RequestInfo>();
        Assert.NotNull(created);

        // Without scheduling: EndTs = 13:00
        // With scheduling: 1h (09:00-10:00) + skip off-time (10:00-14:00) + 3h (14:00-17:00) = EndTs 17:00
        Assert.True(created.EndTs > new DateTime(2027, 1, 5, 13, 0, 0, DateTimeKind.Utc),
            $"EndTs {created.EndTs:u} should be extended past the off-time to at least 17:00");
    }

    [Fact]
    public async Task CreateRequest_WithSchedulingDisabled_UsesPlainEndTs()
    {
        var resourceId = await TestHelpers.GetOrCreateTestSpace(_client);

        var requestPayload = new CreateRequestRequest
        {
            Name = $"NoSched {Guid.NewGuid():N}"[..30],
            ResourceId = resourceId,
            StartTs = new DateTime(2027, 2, 1, 9, 0, 0, DateTimeKind.Utc),
            EndTs = new DateTime(2027, 2, 1, 13, 0, 0, DateTimeKind.Utc),
            MinimalDurationValue = 4,
            MinimalDurationUnit = DurationUnit.Hours,
            SchedulingSettingsApply = false
        };

        var response = await _client.PostAsJsonAsync("/api/requests", requestPayload);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = await response.Content.ReadFromJsonAsync<RequestInfo>();
        Assert.NotNull(created);

        // scheduling disabled: EndTs stays as-is
        Assert.Equal(new DateTime(2027, 2, 1, 13, 0, 0, DateTimeKind.Utc), created.EndTs);
    }

    #endregion
}
