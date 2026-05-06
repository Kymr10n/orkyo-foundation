using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Api.Models;
using Xunit;

namespace Orkyo.Foundation.Tests.Endpoints;

/// <summary>
/// Tests for AutoSchedule endpoints (preview + apply).
/// These endpoints require authentication and tenant membership.
/// </summary>
[Collection("Database collection")]
public class AutoScheduleEndpointsTests
{
    private readonly HttpClient _client;
    private readonly HttpClient _unauthenticatedClient;
    private readonly JsonSerializerOptions _jsonOptions;

    public AutoScheduleEndpointsTests(DatabaseFixture databaseFixture)
    {
        _client = databaseFixture.CreateAuthorizedClient();
        _unauthenticatedClient = databaseFixture.Factory.CreateClient();
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };
    }

    #region POST /api/scheduling/auto-schedule/preview

    [Fact]
    public async Task Preview_NoAuth_Returns401()
    {
        var request = new AutoSchedulePreviewRequest(
            SiteId: Guid.NewGuid(),
            HorizonStart: DateOnly.FromDateTime(DateTime.UtcNow),
            HorizonEnd: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)));

        var response = await _unauthenticatedClient.PostAsJsonAsync(
            "/api/scheduling/auto-schedule/preview", request, _jsonOptions);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Preview_WithValidSite_ReachesEndpoint()
    {
        var siteId = await TestHelpers.GetOrCreateTestSite(_client);

        var request = new AutoSchedulePreviewRequest(
            SiteId: siteId,
            HorizonStart: DateOnly.FromDateTime(DateTime.UtcNow),
            HorizonEnd: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)));

        var response = await _client.PostAsJsonAsync(
            "/api/scheduling/auto-schedule/preview", request, _jsonOptions);

        // Route is registered, auth passes — verify we don't get 401/403/404
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);

        if (response.StatusCode == HttpStatusCode.OK)
        {
            var body = await response.Content.ReadFromJsonAsync<AutoSchedulePreviewResponse>(_jsonOptions);
            Assert.NotNull(body);
            Assert.NotNull(body.Assignments);
            Assert.NotNull(body.Unscheduled);
            Assert.NotNull(body.Diagnostics);
            Assert.NotNull(body.Fingerprint);
        }
    }

    #endregion

    #region POST /api/scheduling/auto-schedule/apply

    [Fact]
    public async Task Apply_NoAuth_Returns401()
    {
        var request = new AutoScheduleApplyRequest(
            SiteId: Guid.NewGuid(),
            HorizonStart: DateOnly.FromDateTime(DateTime.UtcNow),
            HorizonEnd: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)));

        var response = await _unauthenticatedClient.PostAsJsonAsync(
            "/api/scheduling/auto-schedule/apply", request, _jsonOptions);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Apply_WithValidSite_ReachesEndpoint()
    {
        var siteId = await TestHelpers.GetOrCreateTestSite(_client);

        var request = new AutoScheduleApplyRequest(
            SiteId: siteId,
            HorizonStart: DateOnly.FromDateTime(DateTime.UtcNow),
            HorizonEnd: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)));

        var response = await _client.PostAsJsonAsync(
            "/api/scheduling/auto-schedule/apply", request, _jsonOptions);

        // Route is registered, auth passes — verify we don't get 401/403/404
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);

        if (response.StatusCode == HttpStatusCode.OK)
        {
            var body = await response.Content.ReadFromJsonAsync<AutoScheduleApplyResponse>(_jsonOptions);
            Assert.NotNull(body);
        }
    }

    #endregion

    #region Route existence

    [Fact]
    public async Task Preview_RouteExists_DoesNotReturn404()
    {
        var request = new AutoSchedulePreviewRequest(
            SiteId: Guid.NewGuid(),
            HorizonStart: DateOnly.FromDateTime(DateTime.UtcNow),
            HorizonEnd: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)));

        var response = await _unauthenticatedClient.PostAsJsonAsync(
            "/api/scheduling/auto-schedule/preview", request, _jsonOptions);

        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Apply_RouteExists_DoesNotReturn404()
    {
        var request = new AutoScheduleApplyRequest(
            SiteId: Guid.NewGuid(),
            HorizonStart: DateOnly.FromDateTime(DateTime.UtcNow),
            HorizonEnd: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)));

        var response = await _unauthenticatedClient.PostAsJsonAsync(
            "/api/scheduling/auto-schedule/apply", request, _jsonOptions);

        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    #endregion
}
