using System.Net;
using System.Net.Http.Json;
using Api.Models.Insights;
using Npgsql;
using Xunit;

namespace Orkyo.Foundation.Tests.Endpoints;

/// <summary>
/// Integration tests for the built-in Insights dashboard endpoints: input validation (fail fast),
/// site validation/filtering, empty-period behaviour, and request-count correctness. Data is seeded
/// into a unique site + a far-future window so assertions are immune to other tests sharing the DB.
/// </summary>
[Collection("Database collection")]
public class InsightsEndpointsTests
{
    private readonly DatabaseFixture _fixture;
    private readonly HttpClient _client;
    private readonly string _tenantCs;

    // Far-future window — isolates these counts from any other test's seeded requests.
    private const string From = "2099-01-01T00:00:00Z";
    private const string To = "2099-04-01T00:00:00Z";
    private static readonly DateTime CreatedAt = new(2099, 1, 15, 10, 0, 0, DateTimeKind.Utc);

    public InsightsEndpointsTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.CreateAuthorizedClient();
        _tenantCs = $"Host=localhost;Port={fixture.DatabasePort};Database={TestConstants.TenantDatabase};Username=postgres;Password=postgres";
    }

    // ── Validation (fail fast, no silent defaults) ────────────────────────────

    [Fact]
    public async Task Overview_MissingDates_Returns400()
    {
        var response = await _client.GetAsync("/api/insights/overview");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Overview_FromAfterTo_Returns400()
    {
        var response = await _client.GetAsync("/api/insights/overview?from=2099-02-01&to=2099-01-01");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task RequestTrend_MissingBucket_Returns400()
    {
        var response = await _client.GetAsync($"/api/insights/requests?from={From}&to={To}");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task RequestTrend_InvalidBucket_Returns400()
    {
        var response = await _client.GetAsync($"/api/insights/requests?from={From}&to={To}&bucket=decade");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Utilization_MissingResourceType_Returns400()
    {
        var response = await _client.GetAsync($"/api/insights/utilization?from={From}&to={To}&bucket=month");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Utilization_InvalidResourceType_Returns400()
    {
        var response = await _client.GetAsync($"/api/insights/utilization?from={From}&to={To}&bucket=month&resourceType=widget");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Conflicts_RangeTooLargeForBucket_Returns400()
    {
        // Weekly bucket caps at ~2 years; 50 years must be rejected.
        var response = await _client.GetAsync("/api/insights/conflicts?from=2000-01-01&to=2050-01-01&bucket=week");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Overview_UnknownSite_Returns404()
    {
        var response = await _client.GetAsync($"/api/insights/overview?from={From}&to={To}&siteId={Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Insights_WithoutAuth_Returns401()
    {
        var anon = _fixture.Factory.CreateClient();
        var response = await anon.GetAsync($"/api/insights/overview?from={From}&to={To}");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Empty period ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Overview_EmptyPeriod_ReturnsZeroCountsAndLiveSource()
    {
        var siteId = await SeedSiteAsync();
        // No scheduled work for this fresh site → the in-window-derived counts are zero. (Unscheduled
        // is not asserted to 0: site-neutral backlog from the shared DB legitimately shows under every
        // site now, so it's exercised by its own delta test instead.)
        var overview = await GetAsync<InsightsOverview>(
            $"/api/insights/overview?from={From}&to={To}&siteId={siteId}");

        overview.Requests.Scheduled.Should().Be(0);
        overview.Requests.Completed.Should().Be(0);
        overview.Requests.Cancelled.Should().Be(0);
        overview.Conflicts.Total.Should().Be(0);
        overview.Metadata.SourceMode.Should().Be("live");
    }

    [Fact]
    public async Task RequestTrend_EmptyPeriod_ReturnsContiguousZeroBuckets()
    {
        var siteId = await SeedSiteAsync();
        var trend = await GetAsync<InsightsRequests>(
            $"/api/insights/requests?from={From}&to={To}&bucket=month&siteId={siteId}");

        trend.Series.Should().HaveCount(3); // Jan, Feb, Mar 2099
        trend.Series.Should().OnlyContain(p => p.Total == 0);
    }

    // ── Request counts (anchored on scheduled date) ───────────────────────────

    [Fact]
    public async Task Overview_CountsScheduledWorkByStartTs()
    {
        var siteId = await SeedSiteAsync();
        await SeedRequestAsync(siteId, "done", StartJan);
        await SeedRequestAsync(siteId, "cancelled", StartJan);
        await SeedRequestAsync(siteId, "new", StartJan);

        var overview = await GetAsync<InsightsOverview>(
            $"/api/insights/overview?from={From}&to={To}&siteId={siteId}");

        // Site-specific + far-future window → isolated from other tests.
        overview.Requests.Completed.Should().Be(1); // in-window done
        overview.Requests.Cancelled.Should().Be(1); // in-window cancelled
        overview.Requests.Total.Should().BeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public async Task Overview_SiteNeutralBacklogCountedUnderSpecificSite()
    {
        // Regression: a site-neutral (site_id NULL) backlog request must appear under a specific site,
        // because it's schedulable anywhere. Delta-based so it's immune to other tests' data.
        var siteId = await SeedSiteAsync();
        var before = (await GetAsync<InsightsOverview>(
            $"/api/insights/overview?from={From}&to={To}&siteId={siteId}")).Requests.Unscheduled;

        await SeedRequestAsync(siteId: null, "new", startTs: null); // site-neutral backlog

        var after = (await GetAsync<InsightsOverview>(
            $"/api/insights/overview?from={From}&to={To}&siteId={siteId}")).Requests.Unscheduled;

        after.Should().Be(before + 1);
    }

    [Fact]
    public async Task RequestTrend_BucketsByScheduledDate_ExcludesBacklog()
    {
        var siteId = await SeedSiteAsync();
        await SeedRequestAsync(siteId, "new", StartJan);
        await SeedRequestAsync(siteId, "done", StartJan);
        await SeedRequestAsync(siteId, "new", StartFeb);
        await SeedRequestAsync(siteId, "new", startTs: null); // backlog → not on the timeline

        var trend = await GetAsync<InsightsRequests>(
            $"/api/insights/requests?from={From}&to={To}&bucket=month&siteId={siteId}");

        trend.Series[0].Total.Should().Be(2);   // January: both requests bucketed by their start date
        // Status is EFFECTIVE: 2099 is in the future, so both (incl. the stored-"done" one) derive to "new".
        trend.Series[0].New.Should().Be(2);
        trend.Series[1].Total.Should().Be(1);   // February
    }

    [Fact]
    public async Task RequestTrend_CountsByEffectiveStatus_DerivedFromSchedule()
    {
        var siteId = await SeedSiteAsync();
        var now = DateTime.UtcNow;
        // Effective status is derived from the schedule window vs now (SeedRequestAsync sets end = start + 1h):
        await SeedRequestAsync(siteId, "new", now.AddDays(-10));    // ended ~10d ago        → done
        await SeedRequestAsync(siteId, "new", now.AddMinutes(-30)); // started 30m ago, ends in 30m → in_progress
        await SeedRequestAsync(siteId, "new", now.AddDays(10));     // starts in 10d         → new

        var from = now.AddMonths(-1).ToString("yyyy-MM-ddTHH:mm:ssZ");
        var to = now.AddMonths(2).ToString("yyyy-MM-ddTHH:mm:ssZ");
        var trend = await GetAsync<InsightsRequests>(
            $"/api/insights/requests?from={from}&to={to}&bucket=month&siteId={siteId}");

        trend.Series.Sum(s => s.Done).Should().Be(1);
        trend.Series.Sum(s => s.InProgress).Should().Be(1);
        trend.Series.Sum(s => s.New).Should().Be(1);
    }

    // ── Utilization KPI reconciles with its trend chart (regression #2) ────────

    [Fact]
    public async Task Overview_SpaceUtilization_NeverExceedsTrendPeak()
    {
        var siteId = await SeedSiteAsync();
        var requestId = await SeedRequestAsync(siteId, "new", StartJan);
        var resourceId = await SeedSpaceResourceAsync();
        await SeedAssignmentAsync(requestId, resourceId, StartJan, StartJan.AddHours(4));

        var ov = await GetAsync<InsightsOverview>($"/api/insights/overview?from={From}&to={To}");
        var ut = await GetAsync<InsightsUtilization>(
            $"/api/insights/utilization?from={From}&to={To}&bucket=month&resourceType=space");

        var peak = ut.Series.Where(p => p.UtilizationPercent.HasValue)
            .Select(p => p.UtilizationPercent!.Value).DefaultIfEmpty(0m).Max();

        ov.Utilization.SpacesPercent.Should().NotBeNull();
        // The headline aggregate must lie within the chart it sits above — the exact bug we're guarding.
        ov.Utilization.SpacesPercent!.Value.Should().BeLessThanOrEqualTo(peak + 0.01m);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static readonly DateTime StartJan = new(2099, 1, 15, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime StartFeb = new(2099, 2, 15, 9, 0, 0, DateTimeKind.Utc);

    private async Task<T> GetAsync<T>(string url)
    {
        var response = await _client.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK, because: $"GET {url} should succeed");
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private async Task<Guid> SeedSiteAsync()
    {
        var id = Guid.NewGuid();
        await using var conn = new NpgsqlConnection(_tenantCs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO sites (id, name, code) VALUES (@id, @name, @code)", conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("name", $"Insights Site {id.ToString()[..8]}");
        cmd.Parameters.AddWithValue("code", $"INS-{id.ToString()[..8]}");
        await cmd.ExecuteNonQueryAsync();
        return id;
    }

    private async Task<Guid> SeedRequestAsync(Guid? siteId, string status, DateTime? startTs)
    {
        var id = Guid.NewGuid();
        await using var conn = new NpgsqlConnection(_tenantCs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(@"
            INSERT INTO requests
                (id, name, site_id, status, start_ts, end_ts, minimal_duration_value, minimal_duration_unit,
                 planning_mode, created_at, updated_at)
            VALUES
                (@id, @name, @siteId, @status, @startTs, @endTs, 60, 'minutes', 'leaf', @createdAt, @createdAt)", conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("name", $"Req {id.ToString()[..8]}");
        cmd.Parameters.AddWithValue("siteId", (object?)siteId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("status", status);
        cmd.Parameters.AddWithValue("startTs", (object?)startTs ?? DBNull.Value);
        cmd.Parameters.AddWithValue("endTs", (object?)startTs?.AddHours(1) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("createdAt", CreatedAt);
        await cmd.ExecuteNonQueryAsync();
        return id;
    }

    private async Task<Guid> SeedSpaceResourceAsync()
    {
        var id = Guid.NewGuid();
        await using var conn = new NpgsqlConnection(_tenantCs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(@"
            INSERT INTO resources (id, resource_type_id, name, allocation_mode, base_availability_percent, is_active)
            SELECT @id, rt.id, @name, 'Exclusive', 100, true
            FROM resource_types rt WHERE rt.key = 'space'", conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("name", $"Space {id.ToString()[..8]}");
        await cmd.ExecuteNonQueryAsync();
        return id;
    }

    private async Task SeedAssignmentAsync(Guid requestId, Guid resourceId, DateTime startUtc, DateTime endUtc)
    {
        await using var conn = new NpgsqlConnection(_tenantCs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(@"
            INSERT INTO resource_assignments (id, request_id, resource_id, start_utc, end_utc, assignment_status)
            VALUES (gen_random_uuid(), @requestId, @resourceId, @start, @end, 'Planned')", conn);
        cmd.Parameters.AddWithValue("requestId", requestId);
        cmd.Parameters.AddWithValue("resourceId", resourceId);
        cmd.Parameters.AddWithValue("start", startUtc);
        cmd.Parameters.AddWithValue("end", endUtc);
        await cmd.ExecuteNonQueryAsync();
    }
}
