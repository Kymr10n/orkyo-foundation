using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Orkyo.Foundation.Tests.Endpoints;

/// <summary>
/// Integration tests for the calendar subscription endpoints: create a token, fetch the
/// feed anonymously with it, revoke it.
/// </summary>
[Collection("Database collection")]
public class CalendarFeedEndpointsTests
{
    private readonly DatabaseFixture _fixture;
    private readonly HttpClient _client;

    public CalendarFeedEndpointsTests(DatabaseFixture databaseFixture)
    {
        _fixture = databaseFixture;
        _client = databaseFixture.CreateAuthorizedClient();
    }

    /// <summary>The feed is fetched by a calendar client with no session — only the token.</summary>
    private HttpClient CreateAnonymousClient()
    {
        var client = _fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Add(HeaderConstants.TenantSlug, TestConstants.TenantSlug);
        return client;
    }

    private async Task<(Guid Id, string FeedUrl)> CreateSubscriptionAsync(string label)
    {
        var response = await _client.PostAsJsonAsync("/api/calendar/subscriptions", new { label });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return (
            body.RootElement.GetProperty("id").GetGuid(),
            body.RootElement.GetProperty("feedUrl").GetString()!);
    }

    /// <summary>The feed URL is absolute; the test client speaks to the host by path.</summary>
    private static string FeedPath(string feedUrl) => new Uri(feedUrl).PathAndQuery;

    [Fact]
    public async Task Create_ReturnsAFeedUrlContainingTheToken()
    {
        var (id, feedUrl) = await CreateSubscriptionAsync("Outlook");

        id.Should().NotBeEmpty();
        feedUrl.Should().Contain("/api/calendar/feed/").And.EndWith(".ics");
    }

    [Fact]
    public async Task List_OmitsTheTokenValue()
    {
        await CreateSubscriptionAsync("Listed");

        var response = await _client.GetAsync("/api/calendar/subscriptions");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // The plaintext token exists exactly once, in the create response. Anything
        // that echoed it here would defeat storing only a hash.
        var payload = await response.Content.ReadAsStringAsync();
        payload.Should().Contain("Listed").And.NotContain("tokenHash");
    }

    [Fact]
    public async Task Feed_ServesICalendarToAnAnonymousClientHoldingTheToken()
    {
        var (_, feedUrl) = await CreateSubscriptionAsync("Anonymous fetch");

        var response = await CreateAnonymousClient().GetAsync(FeedPath(feedUrl));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/calendar");
        (await response.Content.ReadAsStringAsync()).Should().StartWith("BEGIN:VCALENDAR");
    }

    [Fact]
    public async Task Feed_ReturnsNotFoundForAnUnknownToken()
    {
        var response = await CreateAnonymousClient()
            .GetAsync("/api/calendar/feed/not-a-real-token.ics");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Revoke_StopsTheFeedFromServing()
    {
        var (id, feedUrl) = await CreateSubscriptionAsync("To be revoked");

        var revoke = await _client.DeleteAsync($"/api/calendar/subscriptions/{id}");
        revoke.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Revoked and never-existed give the same answer on purpose: distinguishing
        // them would confirm to a prober that some other token exists.
        var afterRevoke = await CreateAnonymousClient().GetAsync(FeedPath(feedUrl));
        afterRevoke.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // NOTE: the entitlement-denied paths (create → 402 upgrade_required, feed → 404) are not
    // integration-tested here. FoundationWebApplicationFactory registers AllFeaturesEnabledGate
    // in DI and does not support per-test service overrides (see the skipped overrides in
    // ContactEndpointsTests), so the gate never fires in this harness — the same limitation
    // documented for the reporting-token 402 in ReportingEndpointsTests. The tests above do
    // cover that adding the gate left the entitled path intact; the denied path is covered on
    // the frontend by CalendarFeedDialog.test.tsx / useCalendarFeedAvailable.test.ts, and in
    // SaaS by the tier-quota integration tests.
}
