using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using Xunit;

namespace Orkyo.Foundation.Tests.Endpoints;

/// <summary>
/// Tests for the self-service announcement-email opt-in/out endpoints.
/// Routes: GET/PUT /api/account/notification-preferences (auth required, control-plane backed).
/// The test bearer token resolves to user 11111111-1111-1111-1111-111111111111.
/// </summary>
[Collection("Database collection")]
public class NotificationPreferencesEndpointsTests
{
    private static readonly Guid TestUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly HttpClient _client;
    private readonly HttpClient _unauthenticatedClient;
    private readonly string _conn;

    public NotificationPreferencesEndpointsTests(DatabaseFixture databaseFixture)
    {
        _client = databaseFixture.CreateAuthorizedClient();
        _unauthenticatedClient = databaseFixture.Factory.CreateClient();
        _conn = $"Host=localhost;Port={databaseFixture.DatabasePort};Database=control_plane;Username=postgres;Password=postgres";
    }

    private async Task SetOptOutAsync(bool optOut)
    {
        await using var conn = new NpgsqlConnection(_conn);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "UPDATE users SET announcement_email_opt_out = @v WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("v", optOut);
        cmd.Parameters.AddWithValue("id", TestUserId);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<bool> GetOptOutAsync()
    {
        await using var conn = new NpgsqlConnection(_conn);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT announcement_email_opt_out FROM users WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", TestUserId);
        return (bool)(await cmd.ExecuteScalarAsync())!;
    }

    private static async Task<bool> ReadOptOutAsync(HttpResponseMessage response)
    {
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("announcementEmailOptOut").GetBoolean();
    }

    [Fact]
    public async Task Get_WhenOptedIn_ReturnsFalse()
    {
        await SetOptOutAsync(false);

        var response = await _client.GetAsync("/api/account/notification-preferences");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadOptOutAsync(response)).Should().BeFalse();
    }

    [Fact]
    public async Task Get_WhenOptedOut_ReturnsTrue()
    {
        await SetOptOutAsync(true);

        var response = await _client.GetAsync("/api/account/notification-preferences");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadOptOutAsync(response)).Should().BeTrue();
    }

    [Fact]
    public async Task Put_OptOut_PersistsAndIsReflectedByGet()
    {
        await SetOptOutAsync(false);

        var put = await _client.PutAsJsonAsync(
            "/api/account/notification-preferences", new { announcementEmailOptOut = true });

        put.StatusCode.Should().Be(HttpStatusCode.OK);
        (await GetOptOutAsync()).Should().BeTrue();

        var get = await _client.GetAsync("/api/account/notification-preferences");
        (await ReadOptOutAsync(get)).Should().BeTrue();
    }

    [Fact]
    public async Task Put_OptIn_ClearsPreviousOptOut()
    {
        await SetOptOutAsync(true);

        var put = await _client.PutAsJsonAsync(
            "/api/account/notification-preferences", new { announcementEmailOptOut = false });

        put.StatusCode.Should().Be(HttpStatusCode.OK);
        (await GetOptOutAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Get_NoAuth_Returns401()
    {
        var response = await _unauthenticatedClient.GetAsync("/api/account/notification-preferences");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Put_NoAuth_Returns401()
    {
        var response = await _unauthenticatedClient.PutAsJsonAsync(
            "/api/account/notification-preferences", new { announcementEmailOptOut = true });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
