using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using Orkyo.Foundation.Tests.Mocks;
using Xunit;

namespace Orkyo.Foundation.Tests.Endpoints;

[Collection("Database collection")]
public class AccountLifecycleEndpointsTests
{
    private readonly HttpClient _client;
    private readonly MockKeycloakAdminService _mockKeycloak;
    private readonly string _cpConnectionString;

    public AccountLifecycleEndpointsTests(DatabaseFixture databaseFixture)
    {
        var factory = databaseFixture.Factory;
        _mockKeycloak = factory.MockKeycloakAdminService;
        _mockKeycloak.Reset();
        _cpConnectionString = $"Host=localhost;Port={databaseFixture.DatabasePort};Database=control_plane;Username=postgres;Password=postgres";

        // AllowAutoRedirect = false so we can assert the Location header
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        // /api/account/* skips API-key and tenant-slug requirements — no default headers needed
    }

    // ─── helpers ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a user in control_plane.users with lifecycle columns set and returns
    /// the user's ID and confirm token. Optionally sets a keycloak_id (needed for
    /// testing the dormant re-enable flow).
    /// </summary>
    private async Task<(Guid userId, string token)> CreateUserWithLifecycleTokenAsync(
        string lifecycleStatus = "warned",
        string? keycloakId = null)
    {
        var email = $"lifecycle_{Guid.NewGuid()}@example.com";
        // No tenant membership needed — the endpoint only touches control_plane.users
        var userId = await DatabaseTestUtils.CreateTestUserAsync(
            email,
            displayName: "Lifecycle Test",
            tenantSlug: null,
            active: true);

        var token = Guid.NewGuid().ToString();

        await using var conn = new NpgsqlConnection(_cpConnectionString);
        await conn.OpenAsync();

        if (keycloakId != null)
        {
            await using var kcCmd = new NpgsqlCommand(
                "UPDATE users SET keycloak_id = @kcId WHERE id = @id", conn);
            kcCmd.Parameters.AddWithValue("kcId", keycloakId);
            kcCmd.Parameters.AddWithValue("id", userId);
            await kcCmd.ExecuteNonQueryAsync();
        }

        await using var cmd = new NpgsqlCommand(@"
            UPDATE users
            SET lifecycle_status        = @status,
                lifecycle_warning_count = 1,
                lifecycle_last_warned_at = NOW(),
                lifecycle_confirm_token = @token
            WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("status", lifecycleStatus);
        cmd.Parameters.AddWithValue("token", token);
        cmd.Parameters.AddWithValue("id", userId);
        await cmd.ExecuteNonQueryAsync();

        return (userId, token);
    }

    /// <summary>
    /// Reads the lifecycle columns from control_plane.users for the given user.
    /// </summary>
    private async Task<(string? status, int warningCount, string? confirmToken)> GetUserLifecycleStateAsync(Guid userId)
    {
        await using var conn = new NpgsqlConnection(_cpConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT lifecycle_status, lifecycle_warning_count, lifecycle_confirm_token FROM users WHERE id = @id",
            conn);
        cmd.Parameters.AddWithValue("id", userId);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return (null, 0, null);
        var status = reader.IsDBNull(0) ? null : reader.GetString(0);
        var count = reader.GetInt32(1);
        var confirmToken = reader.IsDBNull(2) ? null : reader.GetString(2);
        return (status, count, confirmToken);
    }

    // ─── no-token / invalid-token ────────────────────────────────────────────────

    [Fact]
    public async Task ConfirmActivity_WithNoToken_RedirectsToInvalid()
    {
        var response = await _client.GetAsync("/api/account/confirm-activity");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Contain("lifecycle=invalid");
    }

    [Fact]
    public async Task ConfirmActivity_WithEmptyToken_RedirectsToInvalid()
    {
        var response = await _client.GetAsync("/api/account/confirm-activity?token=");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Contain("lifecycle=invalid");
    }

    [Fact]
    public async Task ConfirmActivity_WithUnknownToken_RedirectsToExpired()
    {
        var fakeToken = Guid.NewGuid().ToString();

        var response = await _client.GetAsync($"/api/account/confirm-activity?token={fakeToken}");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Contain("lifecycle=expired");
    }

    // ─── warned user ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task ConfirmActivity_WithValidWarnedToken_ClearsLifecycleStateAndRedirectsToConfirmed()
    {
        var (userId, token) = await CreateUserWithLifecycleTokenAsync("warned");

        var response = await _client.GetAsync($"/api/account/confirm-activity?token={token}");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Contain("lifecycle=confirmed");

        // Lifecycle columns must be cleared
        var (status, count, confirmToken) = await GetUserLifecycleStateAsync(userId);
        status.Should().BeNull();
        count.Should().Be(0);
        confirmToken.Should().BeNull();

        // User was not dormant, so Keycloak re-enable should NOT be called
        _mockKeycloak.EnableUserCallCount.Should().Be(0);
    }

    // ─── dormant user ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task ConfirmActivity_WithValidDormantToken_ReEnablesKeycloakAndClearsState()
    {
        var keycloakId = Guid.NewGuid().ToString();
        var (userId, token) = await CreateUserWithLifecycleTokenAsync("dormant", keycloakId);

        var response = await _client.GetAsync($"/api/account/confirm-activity?token={token}");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Contain("lifecycle=confirmed");

        // Keycloak should have been asked to re-enable the user
        _mockKeycloak.EnableUserCallCount.Should().Be(1);
        _mockKeycloak.LastEnabledKeycloakId.Should().Be(keycloakId);

        // DB state must be cleared regardless
        var (status, count, confirmToken) = await GetUserLifecycleStateAsync(userId);
        status.Should().BeNull();
        count.Should().Be(0);
        confirmToken.Should().BeNull();
    }

    [Fact]
    public async Task ConfirmActivity_WhenKeycloakReEnableFails_StillClearsLifecycleState()
    {
        var keycloakId = Guid.NewGuid().ToString();
        var (userId, token) = await CreateUserWithLifecycleTokenAsync("dormant", keycloakId);
        _mockKeycloak.EnableUserSuccess = false;
        _mockKeycloak.EnableUserError = "Keycloak temporarily unavailable";

        var response = await _client.GetAsync($"/api/account/confirm-activity?token={token}");

        // Endpoint continues on Keycloak failure — state is still cleared and redirect is confirmed
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Contain("lifecycle=confirmed");

        var (status, count, confirmToken) = await GetUserLifecycleStateAsync(userId);
        status.Should().BeNull();
        count.Should().Be(0);
        confirmToken.Should().BeNull();
    }

    [Fact]
    public async Task ConfirmActivity_DormantUserWithNoKeycloakId_StillClearsStateWithoutCallingKeycloak()
    {
        // keycloakId = null — user has no Keycloak account (e.g. created directly in DB)
        var (userId, token) = await CreateUserWithLifecycleTokenAsync("dormant", keycloakId: null);

        var response = await _client.GetAsync($"/api/account/confirm-activity?token={token}");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Contain("lifecycle=confirmed");

        _mockKeycloak.EnableUserCallCount.Should().Be(0);

        var (status, _, _) = await GetUserLifecycleStateAsync(userId);
        status.Should().BeNull();
    }

    // ─── idempotency guard ────────────────────────────────────────────────────────

    [Fact]
    public async Task ConfirmActivity_CannotBeConfirmedTwice_SecondCallRedirectsToExpired()
    {
        var (_, token) = await CreateUserWithLifecycleTokenAsync("warned");

        var first = await _client.GetAsync($"/api/account/confirm-activity?token={token}");
        first.StatusCode.Should().Be(HttpStatusCode.Redirect);
        first.Headers.Location!.ToString().Should().Contain("lifecycle=confirmed");

        // Token is consumed on first use; second call should not find it
        var second = await _client.GetAsync($"/api/account/confirm-activity?token={token}");
        second.StatusCode.Should().Be(HttpStatusCode.Redirect);
        second.Headers.Location!.ToString().Should().Contain("lifecycle=expired");
    }
}
