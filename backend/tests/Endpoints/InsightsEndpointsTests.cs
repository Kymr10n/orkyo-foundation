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
        // No requests seeded for this site → all zeros, never null.
        var overview = await GetAsync<InsightsOverview>(
            $"/api/insights/overview?from={From}&to={To}&siteId={siteId}");

        overview.Requests.Total.Should().Be(0);
        overview.Requests.Scheduled.Should().Be(0);
        overview.Requests.Unscheduled.Should().Be(0);
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

    // ── Request counts ────────────────────────────────────────────────────────

    [Fact]
    public async Task Overview_CountsRequestsByStatusForTheSite()
    {
        var siteId = await SeedSiteAsync();
        await SeedRequestAsync(siteId, "planned");
        await SeedRequestAsync(siteId, "in_progress");
        await SeedRequestAsync(siteId, "done");
        await SeedRequestAsync(siteId, "cancelled");

        var overview = await GetAsync<InsightsOverview>(
            $"/api/insights/overview?from={From}&to={To}&siteId={siteId}");

        overview.Requests.Total.Should().Be(4);
        overview.Requests.Cancelled.Should().Be(1);
        overview.Requests.Completed.Should().Be(1);              // done
        overview.Requests.Scheduled.Should().Be(0);              // no space assignments
        overview.Requests.Unscheduled.Should().Be(3);           // planned + in_progress + done
    }

    [Fact]
    public async Task Overview_SiteFilter_ExcludesOtherSites()
    {
        var siteA = await SeedSiteAsync();
        var siteB = await SeedSiteAsync();
        await SeedRequestAsync(siteA, "planned");
        await SeedRequestAsync(siteA, "planned");

        var a = await GetAsync<InsightsOverview>($"/api/insights/overview?from={From}&to={To}&siteId={siteA}");
        var b = await GetAsync<InsightsOverview>($"/api/insights/overview?from={From}&to={To}&siteId={siteB}");

        a.Requests.Total.Should().Be(2);
        b.Requests.Total.Should().Be(0);
    }

    [Fact]
    public async Task RequestTrend_PlacesRequestsInTheCreatedAtBucket()
    {
        var siteId = await SeedSiteAsync();
        await SeedRequestAsync(siteId, "planned"); // created 2099-01-15
        await SeedRequestAsync(siteId, "cancelled");

        var trend = await GetAsync<InsightsRequests>(
            $"/api/insights/requests?from={From}&to={To}&bucket=month&siteId={siteId}");

        trend.Series[0].Total.Should().Be(2);     // January
        trend.Series[0].Cancelled.Should().Be(1);
        trend.Series[1].Total.Should().Be(0);     // February
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

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

    private async Task SeedRequestAsync(Guid siteId, string status)
    {
        await using var conn = new NpgsqlConnection(_tenantCs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(@"
            INSERT INTO requests
                (id, name, site_id, status, minimal_duration_value, minimal_duration_unit, planning_mode, created_at, updated_at)
            VALUES
                (gen_random_uuid(), @name, @siteId, @status, 60, 'minutes', 'leaf', @createdAt, @createdAt)", conn);
        cmd.Parameters.AddWithValue("name", $"Req {Guid.NewGuid().ToString()[..8]}");
        cmd.Parameters.AddWithValue("siteId", siteId);
        cmd.Parameters.AddWithValue("status", status);
        cmd.Parameters.AddWithValue("createdAt", CreatedAt);
        await cmd.ExecuteNonQueryAsync();
    }
}
