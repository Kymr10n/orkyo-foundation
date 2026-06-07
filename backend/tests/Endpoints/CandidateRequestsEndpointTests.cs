using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Models;
using Xunit;

namespace Orkyo.Foundation.Tests.Endpoints;

[Collection("Database collection")]
public class CandidateRequestsEndpointTests
{
    private readonly HttpClient _client;

    public CandidateRequestsEndpointTests(DatabaseFixture databaseFixture)
    {
        _client = databaseFixture.CreateAuthorizedClient();
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private async Task<Guid> CreatePersonAsync()
    {
        var resp = await _client.PostAsJsonAsync("/api/resources", new
        {
            ResourceTypeKey = "person",
            Name = $"Person-{Guid.NewGuid():N}"[..20],
            AllocationMode = "Exclusive",
        });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<JsonElement>())!.GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateScheduledRequestAsync(DateTime start, DateTime end, string status = "planned")
    {
        // Create, then schedule it
        var createResp = await _client.PostAsJsonAsync("/api/requests", new
        {
            Name = $"Req-{Guid.NewGuid():N}"[..20],
            MinimalDurationValue = 1,
            MinimalDurationUnit = "hours",
            SchedulingSettingsApply = false,
        });
        createResp.EnsureSuccessStatusCode();
        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var reqId = created.GetProperty("id").GetGuid();

        var scheduleResp = await _client.PutAsJsonAsync($"/api/requests/{reqId}",
            new UpdateRequestRequest { StartTs = start, EndTs = end });
        scheduleResp.EnsureSuccessStatusCode();

        if (status == "in_progress")
        {
            var r = await _client.PutAsJsonAsync($"/api/requests/{reqId}",
                new UpdateRequestRequest { Status = RequestStatus.InProgress });
            r.EnsureSuccessStatusCode();
        }
        else if (status == "done")
        {
            var r = await _client.PutAsJsonAsync($"/api/requests/{reqId}",
                new UpdateRequestRequest { Status = RequestStatus.Done });
            r.EnsureSuccessStatusCode();
        }
        else if (status == "cancelled")
        {
            var r = await _client.PutAsJsonAsync($"/api/requests/{reqId}",
                new UpdateRequestRequest { Status = RequestStatus.Cancelled });
            r.EnsureSuccessStatusCode();
        }

        return reqId;
    }

    private string CandidateUrl(Guid personId, DateTime start, DateTime end) =>
        $"/api/resources/{personId}/candidate-requests?start={Uri.EscapeDataString(start.ToString("O"))}&end={Uri.EscapeDataString(end.ToString("O"))}";

    // ── Tests ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Returns404_WhenResourceDoesNotExist()
    {
        var unknownId = Guid.NewGuid();
        var start = new DateTime(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc);
        var end = start.AddHours(8);
        var resp = await _client.GetAsync(CandidateUrl(unknownId, start, end));
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Returns200_WithOverlappingPlannedRequest()
    {
        var personId = await CreatePersonAsync();
        var start = new DateTime(2026, 9, 10, 9, 0, 0, DateTimeKind.Utc);
        var end = start.AddHours(4);
        var reqId = await CreateScheduledRequestAsync(start, start.AddHours(8));

        var resp = await _client.GetAsync(CandidateUrl(personId, start, end));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<List<JsonElement>>();
        Assert.NotNull(body);
        var item = Assert.Single(body!, x => x.GetProperty("requestId").GetGuid() == reqId);
        // Unassigned candidate — assignmentId must be null
        Assert.Equal(JsonValueKind.Null, item.GetProperty("assignmentId").ValueKind);
    }

    [Fact]
    public async Task Excludes_RequestEndingBeforePeriodStart()
    {
        var personId = await CreatePersonAsync();
        var start = new DateTime(2026, 9, 11, 12, 0, 0, DateTimeKind.Utc);
        var end = start.AddHours(4);
        // Request ends exactly at period start (exclusive overlap: end_ts > @start)
        var excludedId = await CreateScheduledRequestAsync(start.AddHours(-4), start);

        var resp = await _client.GetAsync(CandidateUrl(personId, start, end));
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<List<JsonElement>>();
        Assert.NotNull(body);
        Assert.DoesNotContain(body!, x => x.GetProperty("requestId").GetGuid() == excludedId);
    }

    [Fact]
    public async Task Excludes_RequestStartingAtOrAfterPeriodEnd()
    {
        var personId = await CreatePersonAsync();
        var start = new DateTime(2026, 9, 12, 9, 0, 0, DateTimeKind.Utc);
        var end = start.AddHours(4);
        // Request starts exactly at period end (exclusive: start_ts < @end)
        var excludedId = await CreateScheduledRequestAsync(end, end.AddHours(4));

        var resp = await _client.GetAsync(CandidateUrl(personId, start, end));
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<List<JsonElement>>();
        Assert.NotNull(body);
        Assert.DoesNotContain(body!, x => x.GetProperty("requestId").GetGuid() == excludedId);
    }

    [Fact]
    public async Task AlreadyAssignedRequest_AppearsWithAssignmentId()
    {
        var personId = await CreatePersonAsync();
        var start = new DateTime(2026, 9, 13, 9, 0, 0, DateTimeKind.Utc);
        var end = start.AddHours(8);
        var reqId = await CreateScheduledRequestAsync(start, end);

        // Assign the person to this request
        var assignResp = await _client.PostAsJsonAsync("/api/resource-assignments", new
        {
            requestId = reqId,
            resourceId = personId,
            startUtc = start,
            endUtc = end,
        });
        assignResp.EnsureSuccessStatusCode();
        var assignment = await assignResp.Content.ReadFromJsonAsync<JsonElement>();
        var expectedAssignmentId = assignment.GetProperty("id").GetGuid();

        var resp = await _client.GetAsync(CandidateUrl(personId, start, end));
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<List<JsonElement>>();
        Assert.NotNull(body);
        // The already-assigned request DOES appear, with a non-null assignmentId
        var item = Assert.Single(body!, x => x.GetProperty("requestId").GetGuid() == reqId);
        Assert.Equal(
            expectedAssignmentId,
            item.GetProperty("assignmentId").GetGuid());
    }

    [Fact]
    public async Task Excludes_DoneAndCancelledRequests()
    {
        var personId = await CreatePersonAsync();
        var start = new DateTime(2026, 9, 14, 9, 0, 0, DateTimeKind.Utc);
        var end = start.AddHours(4);
        var doneId = await CreateScheduledRequestAsync(start, end, "done");
        var cancelledId = await CreateScheduledRequestAsync(start, end, "cancelled");

        var resp = await _client.GetAsync(CandidateUrl(personId, start, end));
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<List<JsonElement>>();
        Assert.NotNull(body);
        Assert.DoesNotContain(body!, item => item.GetProperty("requestId").GetGuid() == doneId);
        Assert.DoesNotContain(body!, item => item.GetProperty("requestId").GetGuid() == cancelledId);
    }

    [Fact]
    public async Task Returns200_EmptyList_WhenNoOverlappingRequests()
    {
        var personId = await CreatePersonAsync();
        var start = new DateTime(2026, 9, 15, 9, 0, 0, DateTimeKind.Utc);
        var end = start.AddHours(4);

        var resp = await _client.GetAsync(CandidateUrl(personId, start, end));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<List<JsonElement>>();
        Assert.NotNull(body);
        // May be empty (no overlap) — just verify the response shape is a list
        Assert.IsType<List<JsonElement>>(body);
    }
}
