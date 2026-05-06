using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Orkyo.Foundation.Tests.Endpoints;

/// <summary>
/// Tests for user-facing announcement endpoints.
/// Routes: GET /api/announcements, GET /api/announcements/unread-count, POST /api/announcements/{id}/read
/// Requires authentication (SkipTenantResolution — no tenant header needed).
/// </summary>
[Collection("Database collection")]
public class UserAnnouncementEndpointsTests
{
    private readonly HttpClient _client;
    private readonly HttpClient _unauthenticatedClient;

    public UserAnnouncementEndpointsTests(DatabaseFixture databaseFixture)
    {
        _client = databaseFixture.CreateAuthorizedClient();
        _unauthenticatedClient = databaseFixture.Factory.CreateClient();
    }

    #region GET /api/announcements

    [Fact]
    public async Task GetActive_NoAuth_Returns401()
    {
        var response = await _unauthenticatedClient.GetAsync("/api/announcements");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetActive_Authenticated_Returns200()
    {
        var response = await _client.GetAsync("/api/announcements");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);
        Assert.True(doc.RootElement.TryGetProperty("announcements", out var announcements));
        Assert.Equal(JsonValueKind.Array, announcements.ValueKind);
    }

    #endregion

    #region GET /api/announcements/unread-count

    [Fact]
    public async Task GetUnreadCount_NoAuth_Returns401()
    {
        var response = await _unauthenticatedClient.GetAsync("/api/announcements/unread-count");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetUnreadCount_Authenticated_Returns200WithCount()
    {
        var response = await _client.GetAsync("/api/announcements/unread-count");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);
        Assert.True(doc.RootElement.TryGetProperty("unreadCount", out var count));
        Assert.True(count.ValueKind == JsonValueKind.Number);
        Assert.True(count.GetInt32() >= 0);
    }

    #endregion

    #region POST /api/announcements/{id}/read

    [Fact]
    public async Task MarkRead_NoAuth_Returns401()
    {
        var response = await _unauthenticatedClient.PostAsync(
            $"/api/announcements/{Guid.NewGuid()}/read", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task MarkRead_NonExistentAnnouncement_Returns204()
    {
        // Marking a non-existent announcement as read should be idempotent
        var response = await _client.PostAsync(
            $"/api/announcements/{Guid.NewGuid()}/read", null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    #endregion

    #region Route existence

    [Fact]
    public async Task Announcements_RouteExists_DoesNotReturn404()
    {
        var response = await _unauthenticatedClient.GetAsync("/api/announcements");
        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UnreadCount_RouteExists_DoesNotReturn404()
    {
        var response = await _unauthenticatedClient.GetAsync("/api/announcements/unread-count");
        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task MarkRead_RouteExists_DoesNotReturn404()
    {
        var response = await _unauthenticatedClient.PostAsync(
            $"/api/announcements/{Guid.NewGuid()}/read", null);
        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    #endregion
}
