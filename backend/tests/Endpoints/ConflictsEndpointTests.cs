using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Orkyo.Foundation.Tests.Endpoints;

[Collection("Database collection")]
public class ConflictsEndpointTests
{
    private readonly HttpClient _client;
    private readonly HttpClient _anonClient;

    public ConflictsEndpointTests(DatabaseFixture databaseFixture)
    {
        _client = databaseFixture.CreateAuthorizedClient();
        _anonClient = databaseFixture.Factory.CreateClient();
    }

    [Fact]
    public async Task GetConflicts_Returns200_Array()
    {
        var response = await _client.GetAsync("/api/conflicts");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Array, body.ValueKind);
    }

    [Fact]
    public async Task GetConflicts_WithWindow_Returns200_Array()
    {
        var from = Uri.EscapeDataString(DateTime.UtcNow.AddDays(-7).ToString("o"));
        var to = Uri.EscapeDataString(DateTime.UtcNow.AddDays(7).ToString("o"));
        var response = await _client.GetAsync($"/api/conflicts?from={from}&to={to}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Array, body.ValueKind);
    }

    [Fact]
    public async Task GetConflicts_Unauthenticated_Returns401()
    {
        var response = await _anonClient.GetAsync("/api/conflicts");
        Assert.True(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task GetRequests_ConflictedTrue_Returns200_Array()
    {
        var response = await _client.GetAsync("/api/requests?conflicted=true");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Array, body.ValueKind);
    }

    [Fact]
    public async Task GetRequests_BacklogScheduledFalse_Returns200_Array()
    {
        var response = await _client.GetAsync("/api/requests?scheduled=false");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Array, body.ValueKind);
    }

    [Fact]
    public async Task GetSiteRequests_WithWindow_Returns200_Array()
    {
        var from = Uri.EscapeDataString(DateTime.UtcNow.AddDays(-7).ToString("o"));
        var to = Uri.EscapeDataString(DateTime.UtcNow.AddDays(7).ToString("o"));
        var response = await _client.GetAsync($"/api/sites/{Guid.NewGuid()}/requests?from={from}&to={to}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Array, body.ValueKind);
    }
}
