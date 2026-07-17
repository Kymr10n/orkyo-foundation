using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using Orkyo.Foundation.Tests.Mocks;
using Xunit;

namespace Orkyo.Foundation.Tests.Endpoints;

[Collection("Database collection")]
public class AccountEmailChangeEndpointsTests
{
    private readonly HttpClient _client;
    private readonly MockKeycloakAdminService _mockKeycloak;
    private readonly MockEmailService _mockEmail;
    private readonly FoundationWebApplicationFactory _factory;
    private readonly string _cpConnectionString;

    public AccountEmailChangeEndpointsTests(DatabaseFixture databaseFixture)
    {
        var factory = databaseFixture.Factory;
        _factory = factory;
        _mockKeycloak = factory.MockKeycloakAdminService;
        _mockEmail = factory.MockEmailService;
        _mockKeycloak.Reset();
        _mockEmail.Reset();
        _cpConnectionString = $"Host=localhost;Port={databaseFixture.DatabasePort};Database=control_plane;Username=postgres;Password=postgres";

        _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        _client.DefaultRequestHeaders.Add("Authorization", $"Bearer {TestConstants.TestBearerToken}");
        _client.DefaultRequestHeaders.Add(HeaderConstants.TenantSlug, TestConstants.TenantSlug);
    }

    // ─── helpers ────────────────────────────────────────────────────────────────

    private async Task SetUserEmailAsync(string email)
    {
        await DeleteOtherUserAsync();
        var userId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        await using var conn = new NpgsqlConnection(_cpConnectionString);
        await conn.OpenAsync();
        await using (var identityCmd = new NpgsqlCommand(
            "DELETE FROM user_identities WHERE user_id = @id", conn))
        {
            identityCmd.Parameters.AddWithValue("id", userId);
            await identityCmd.ExecuteNonQueryAsync();
        }

        await using var cmd = new NpgsqlCommand(
            @"UPDATE users
              SET email = @email,
                  keycloak_id = NULL,
                  pending_email = NULL,
                  email_change_token = NULL,
                  email_change_requested_at = NULL
              WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("email", email);
        cmd.Parameters.AddWithValue("id", userId);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task DeleteOtherUserAsync()
    {
        await using var conn = new NpgsqlConnection(_cpConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "DELETE FROM users WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", new Guid("22222222-2222-2222-2222-222222222222"));
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task InsertOtherUserAsync(string email, string? pendingEmail = null)
    {
        await using var conn = new NpgsqlConnection(_cpConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(@"
            INSERT INTO users (id, email, display_name, status, pending_email, created_at, updated_at)
            VALUES (@id, @email, 'Other User', 'active', @pending, NOW(), NOW())
            ON CONFLICT (id) DO UPDATE
            SET email = @email,
                pending_email = @pending,
                email_change_token = NULL,
                email_change_requested_at = NULL", conn);
        cmd.Parameters.AddWithValue("id", new Guid("22222222-2222-2222-2222-222222222222"));
        cmd.Parameters.AddWithValue("email", email);
        cmd.Parameters.AddWithValue("pending", (object?)pendingEmail ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task SetUserKeycloakIdAsync(string keycloakId)
    {
        var userId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        await using var conn = new NpgsqlConnection(_cpConnectionString);
        await conn.OpenAsync();
        await using (var cmd = new NpgsqlCommand(
            "UPDATE users SET keycloak_id = @kcId WHERE id = @id", conn))
        {
            cmd.Parameters.AddWithValue("kcId", keycloakId);
            cmd.Parameters.AddWithValue("id", userId);
            await cmd.ExecuteNonQueryAsync();
        }

        await using (var identityCmd = new NpgsqlCommand(@"
            INSERT INTO user_identities (user_id, provider, provider_subject, provider_email, created_at)
            VALUES (@id, 'keycloak', @kcId, NULL, NOW())
            ON CONFLICT (provider, provider_subject)
            DO UPDATE SET user_id = @id", conn))
        {
            identityCmd.Parameters.AddWithValue("id", userId);
            identityCmd.Parameters.AddWithValue("kcId", keycloakId);
            await identityCmd.ExecuteNonQueryAsync();
        }
    }

    private async Task SetUserIdentityOnlyKeycloakIdAsync(string keycloakId)
    {
        var userId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        await using var conn = new NpgsqlConnection(_cpConnectionString);
        await conn.OpenAsync();
        await using (var userCmd = new NpgsqlCommand(
            "UPDATE users SET keycloak_id = NULL WHERE id = @id", conn))
        {
            userCmd.Parameters.AddWithValue("id", userId);
            await userCmd.ExecuteNonQueryAsync();
        }

        await using var identityCmd = new NpgsqlCommand(@"
            INSERT INTO user_identities (user_id, provider, provider_subject, provider_email, created_at)
            VALUES (@id, 'keycloak', @kcId, NULL, NOW())
            ON CONFLICT (provider, provider_subject)
            DO UPDATE SET user_id = @id", conn);
        identityCmd.Parameters.AddWithValue("id", userId);
        identityCmd.Parameters.AddWithValue("kcId", keycloakId);
        await identityCmd.ExecuteNonQueryAsync();
    }

    private async Task<(string? pendingEmail, string? token)> GetPendingEmailStateAsync()
    {
        var userId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        await using var conn = new NpgsqlConnection(_cpConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT pending_email, email_change_token FROM users WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", userId);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return (null, null);
        return (
            reader.IsDBNull(0) ? null : reader.GetString(0),
            reader.IsDBNull(1) ? null : reader.GetString(1)
        );
    }

    private async Task<string> GetCurrentEmailAsync()
    {
        var userId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        await using var conn = new NpgsqlConnection(_cpConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT email FROM users WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", userId);
        return (string)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task<string?> GetKeycloakIdentityEmailAsync()
    {
        var userId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        await using var conn = new NpgsqlConnection(_cpConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT provider_email FROM user_identities WHERE user_id = @id AND provider = 'keycloak' LIMIT 1", conn);
        cmd.Parameters.AddWithValue("id", userId);
        return await cmd.ExecuteScalarAsync() as string;
    }

    private async Task<string> StorePendingEmailChangeAsync(
        string pendingEmail, string? keycloakId = null, int hoursAgo = 0)
    {
        await DeleteOtherUserAsync();
        var userId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var token = Guid.NewGuid().ToString();

        await using var conn = new NpgsqlConnection(_cpConnectionString);
        await conn.OpenAsync();
        await using (var identityCmd = new NpgsqlCommand(
            "DELETE FROM user_identities WHERE user_id = @id", conn))
        {
            identityCmd.Parameters.AddWithValue("id", userId);
            await identityCmd.ExecuteNonQueryAsync();
        }

        if (keycloakId != null)
            await SetUserKeycloakIdAsync(keycloakId);

        await using var cmd = new NpgsqlCommand(@"
            UPDATE users
            SET pending_email             = @pending,
                email_change_token        = @token,
                email_change_requested_at = NOW() - (@hours * INTERVAL '1 hour'),
                keycloak_id                = @keycloakId
            WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("pending", pendingEmail);
        cmd.Parameters.AddWithValue("token", token);
        cmd.Parameters.AddWithValue("hours", hoursAgo);
        cmd.Parameters.AddWithValue("keycloakId", (object?)keycloakId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("id", userId);
        await cmd.ExecuteNonQueryAsync();

        return token;
    }

    // ─── POST /api/account/email ─────────────────────────────────────────────────

    [Fact]
    public async Task RequestEmailChange_WithValidNewEmail_Returns200AndSendsEmail()
    {
        await SetUserEmailAsync("current@example.com");
        _mockKeycloak.UserExistsResult = false;

        var response = await _client.PostAsJsonAsync("/api/account/email",
            new { newEmail = "new@example.com" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("message").GetString().Should().Contain("Confirmation email sent");

        // Pending change must be stored in DB
        var (pending, token) = await GetPendingEmailStateAsync();
        pending.Should().Be("new@example.com");
        token.Should().NotBeNullOrEmpty();

        // Confirmation email must be sent to the new address
        _mockEmail.SendEmailChangeConfirmationCallCount.Should().Be(1);
        _mockEmail.LastSendEmailChangeConfirmationCall.toEmail.Should().Be("new@example.com");
    }

    [Fact]
    public async Task RequestEmailChange_WhenEmailSendFails_Returns502AndClearsPendingRow()
    {
        await SetUserEmailAsync("current@example.com");
        _mockKeycloak.UserExistsResult = false;
        _mockEmail.FailNextEmailChangeConfirmation = true;

        var response = await _client.PostAsJsonAsync("/api/account/email",
            new { newEmail = "new@example.com" });

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);

        // Pending row must be cleared so the orphan UNIQUE index entry is released
        // and the user (or anyone else) can retry the same address.
        var (pending, token) = await GetPendingEmailStateAsync();
        pending.Should().BeNull();
        token.Should().BeNull();

        _mockEmail.SendEmailChangeConfirmationCallCount.Should().Be(1);
    }

    [Fact]
    public async Task RequestEmailChange_WithSameEmail_Returns400()
    {
        await SetUserEmailAsync("same@example.com");

        var response = await _client.PostAsJsonAsync("/api/account/email",
            new { newEmail = "same@example.com" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _mockEmail.SendEmailChangeConfirmationCallCount.Should().Be(0);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    public async Task RequestEmailChange_WithInvalidEmail_Returns400(string newEmail)
    {
        await SetUserEmailAsync("current@example.com");

        var response = await _client.PostAsJsonAsync("/api/account/email", new { newEmail });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _mockEmail.SendEmailChangeConfirmationCallCount.Should().Be(0);
    }

    [Fact]
    public async Task RequestEmailChange_WithAlreadyTakenEmail_Returns409()
    {
        await SetUserEmailAsync("current@example.com");
        _mockKeycloak.UserExistsResult = true;

        var response = await _client.PostAsJsonAsync("/api/account/email",
            new { newEmail = "taken@example.com" });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        _mockEmail.SendEmailChangeConfirmationCallCount.Should().Be(0);
    }

    [Fact]
    public async Task RequestEmailChange_WithEmailPendingForAnotherUser_Returns409()
    {
        // Keycloak does not yet know about a pending email; the DB UNIQUE (lower(pending_email))
        // index is what guarantees no two users can have the same pending change in flight.
        await SetUserEmailAsync("current@example.com");
        await InsertOtherUserAsync("other@example.com", pendingEmail: "reserved@example.com");
        _mockKeycloak.UserExistsResult = false;

        var response = await _client.PostAsJsonAsync("/api/account/email",
            new { newEmail = "reserved@example.com" });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        _mockEmail.SendEmailChangeConfirmationCallCount.Should().Be(0);
    }

    [Fact]
    public async Task RequestEmailChange_WithoutAuth_Returns401()
    {
        var anonClient = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var response = await anonClient.SendAsync(
            new HttpRequestMessage(HttpMethod.Post, "/api/account/email")
            {
                Content = JsonContent.Create(new { newEmail = "new@example.com" }),
                Headers = { { HeaderConstants.TenantSlug, TestConstants.TenantSlug } }
            });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RequestEmailChange_OverwritesPreviousPendingRequest()
    {
        await SetUserEmailAsync("current@example.com");
        _mockKeycloak.UserExistsResult = false;

        // First request
        await _client.PostAsJsonAsync("/api/account/email",
            new { newEmail = "first@example.com" });

        // Second request — should overwrite
        await _client.PostAsJsonAsync("/api/account/email",
            new { newEmail = "second@example.com" });

        var (pending, _) = await GetPendingEmailStateAsync();
        pending.Should().Be("second@example.com");
        _mockEmail.SendEmailChangeConfirmationCallCount.Should().Be(2);
    }

    // ─── GET /api/account/confirm-email ──────────────────────────────────────────

    [Fact]
    public async Task ConfirmEmail_WithNoToken_RedirectsToInvalid()
    {
        var response = await _client.GetAsync("/api/account/confirm-email");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().StartWith("http://localhost:5173/account?");
        response.Headers.Location!.ToString().Should().Contain("email-change=invalid");
    }

    [Fact]
    public async Task ConfirmEmail_WithUnknownToken_RedirectsToExpired()
    {
        var response = await _client.GetAsync(
            $"/api/account/confirm-email?token={Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().StartWith("http://localhost:5173/account?");
        response.Headers.Location!.ToString().Should().Contain("email-change=expired");
    }

    [Fact]
    public async Task ConfirmEmail_WithExpiredToken_RedirectsToExpired()
    {
        await SetUserEmailAsync("old@example.com");
        var keycloakId = Guid.NewGuid().ToString();
        var token = await StorePendingEmailChangeAsync("new@example.com", keycloakId, hoursAgo: 25);

        var response = await _client.GetAsync(
            $"/api/account/confirm-email?token={token}");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Contain("email-change=expired");

        // Email in DB must NOT have changed
        var email = await GetCurrentEmailAsync();
        email.Should().Be("old@example.com");
    }

    [Fact]
    public async Task ConfirmEmail_WithValidToken_UpdatesEmailInDbAndKeycloak()
    {
        await SetUserEmailAsync("old@example.com");
        var keycloakId = Guid.NewGuid().ToString();
        var token = await StorePendingEmailChangeAsync("new@example.com", keycloakId);

        var response = await _client.GetAsync(
            $"/api/account/confirm-email?token={token}");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Contain("email-change=confirmed");

        // DB: email promoted, pending cleared
        var email = await GetCurrentEmailAsync();
        email.Should().Be("new@example.com");

        var (pending, storedToken) = await GetPendingEmailStateAsync();
        pending.Should().BeNull();
        storedToken.Should().BeNull();

        // Keycloak: UpdateEmailForAccountAsync called with correct args
        _mockKeycloak.UpdateEmailForAccountCallCount.Should().Be(1);
        _mockKeycloak.LastUpdateEmailForAccountCall.newEmail.Should().Be("new@example.com");
        _mockKeycloak.LastUpdateEmailForAccountCall.keycloakSub.Should().Be(keycloakId);
        (await GetKeycloakIdentityEmailAsync()).Should().Be("new@example.com");
    }

    [Fact]
    public async Task ConfirmEmail_WithIdentityOnlyKeycloakSubject_UpdatesEmailInDbAndKeycloak()
    {
        await SetUserEmailAsync("old@example.com");
        var keycloakId = Guid.NewGuid().ToString();
        var token = await StorePendingEmailChangeAsync("new@example.com");
        await SetUserIdentityOnlyKeycloakIdAsync(keycloakId);

        var response = await _client.GetAsync(
            $"/api/account/confirm-email?token={token}");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Contain("email-change=confirmed");
        (await GetCurrentEmailAsync()).Should().Be("new@example.com");
        _mockKeycloak.LastUpdateEmailForAccountCall.keycloakSub.Should().Be(keycloakId);
    }

    [Fact]
    public async Task ConfirmEmail_WithNoStoredKeycloakSubject_FallsBackToCurrentEmail()
    {
        await SetUserEmailAsync("old@example.com");
        var token = await StorePendingEmailChangeAsync("new@example.com");

        var response = await _client.GetAsync(
            $"/api/account/confirm-email?token={token}");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Contain("email-change=confirmed");
        (await GetCurrentEmailAsync()).Should().Be("new@example.com");
        _mockKeycloak.UpdateEmailForAccountCallCount.Should().Be(1);
        _mockKeycloak.LastUpdateEmailForAccountCall.keycloakSub.Should().BeNull();
        _mockKeycloak.LastUpdateEmailForAccountCall.currentEmail.Should().Be("old@example.com");
        _mockKeycloak.LastUpdateEmailForAccountCall.newEmail.Should().Be("new@example.com");
    }

    [Fact]
    public async Task ConfirmEmail_WhenPendingEmailIsNowTakenLocally_DoesNotUpdateKeycloak()
    {
        await SetUserEmailAsync("old@example.com");
        var keycloakId = Guid.NewGuid().ToString();
        var token = await StorePendingEmailChangeAsync("new@example.com", keycloakId);
        await InsertOtherUserAsync("new@example.com");

        var response = await _client.GetAsync(
            $"/api/account/confirm-email?token={token}");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Contain("email-change=conflict");
        (await GetCurrentEmailAsync()).Should().Be("old@example.com");
        _mockKeycloak.UpdateEmailForAccountCallCount.Should().Be(0);
    }

    [Fact]
    public async Task ConfirmEmail_WhenKeycloakFails_DoesNotUpdateDbAndRedirectsToError()
    {
        await SetUserEmailAsync("old@example.com");
        var keycloakId = Guid.NewGuid().ToString();
        var token = await StorePendingEmailChangeAsync("new@example.com", keycloakId);

        _mockKeycloak.UpdateEmailSuccess = false;
        _mockKeycloak.UpdateEmailError = "Keycloak temporarily unavailable";

        var response = await _client.GetAsync(
            $"/api/account/confirm-email?token={token}");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Contain("email-change=error");

        // DB email must NOT have changed — pending is preserved for retry
        var email = await GetCurrentEmailAsync();
        email.Should().Be("old@example.com");

        var (pending, _) = await GetPendingEmailStateAsync();
        pending.Should().Be("new@example.com");
    }

    [Fact]
    public async Task ConfirmEmail_CannotBeConfirmedTwice_SecondCallRedirectsToExpired()
    {
        var keycloakId = Guid.NewGuid().ToString();
        var token = await StorePendingEmailChangeAsync("new@example.com", keycloakId);

        var first = await _client.GetAsync($"/api/account/confirm-email?token={token}");
        first.Headers.Location!.ToString().Should().Contain("email-change=confirmed");

        var second = await _client.GetAsync($"/api/account/confirm-email?token={token}");
        second.Headers.Location!.ToString().Should().Contain("email-change=expired");

        _mockKeycloak.UpdateEmailForAccountCallCount.Should().Be(1);
    }

    [Fact]
    public async Task ConfirmEmail_IsAccessibleWithoutAuthentication()
    {
        // The endpoint is [AllowAnonymous] — must work without a Bearer token.
        var anonClient = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var response = await anonClient.GetAsync(
            $"/api/account/confirm-email?token={Guid.NewGuid()}");

        // Token won't be found but the endpoint should respond (redirect), not 401
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Contain("email-change=expired");
    }
}
