using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Orkyo.Foundation.Tests.Mocks;
using Orkyo.Shared;

namespace Orkyo.Foundation.Tests.Endpoints;

/// <summary>
/// Integration tests for POST /api/contact.
/// This is a public endpoint (no API key, no tenant) used by the marketing site.
/// </summary>
[Collection("Database collection")]
public class ContactEndpointsTests : IAsyncLifetime
{
    private readonly HttpClient _client;
    private readonly DatabaseFixture _databaseFixture;

    private string ControlPlaneConnectionString =>
        _databaseFixture.ControlPlaneConnectionString;

    public ContactEndpointsTests(DatabaseFixture databaseFixture)
    {
        _databaseFixture = databaseFixture;
        // No API key or tenant header — this is a public endpoint
        _client = databaseFixture.Factory.CreateClient();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        // Clean up any test submissions
        await using var conn = new NpgsqlConnection(ControlPlaneConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "DELETE FROM contact_submissions WHERE email LIKE '%@test.local'", conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private static object ValidPayload(string? emailOverride = null) => new
    {
        name = "Test User",
        email = emailOverride ?? $"contact-{Guid.NewGuid():N}@test.local",
        company = "Acme Corp",
        subject = "demo",
        message = "I'd like to learn more about Orkyo."
    };

    // ── Happy path ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ValidSubmission_Returns200AndStoresRow()
    {
        var email = $"happy-{Guid.NewGuid():N}@test.local";
        var payload = new
        {
            name = "Jane Doe",
            email,
            company = "Acme",
            subject = "sales",
            message = "Interested in Enterprise plan."
        };

        var response = await _client.PostAsJsonAsync("/api/contact", payload);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("message").GetString().Should().NotBeNullOrEmpty();

        // Verify row was persisted
        await using var conn = new NpgsqlConnection(ControlPlaneConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM contact_submissions WHERE email = @email", conn);
        cmd.Parameters.AddWithValue("email", email);
        var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        count.Should().Be(1);
    }

    [Fact]
    public async Task ValidSubmission_WithoutCompany_Returns200()
    {
        var payload = new
        {
            name = "No Company",
            email = $"nocompany-{Guid.NewGuid():N}@test.local",
            subject = "other",
            message = "Just a question."
        };

        var response = await _client.PostAsJsonAsync("/api/contact", payload);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("demo")]
    [InlineData("sales")]
    [InlineData("support")]
    [InlineData("security")]
    [InlineData("other")]
    public async Task ValidSubmission_AllSubjects_Returns200(string subject)
    {
        var payload = new
        {
            name = "Subject Test",
            email = $"subj-{Guid.NewGuid():N}@test.local",
            subject,
            message = "Testing subject acceptance."
        };

        var response = await _client.PostAsJsonAsync("/api/contact", payload);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── Validation: missing / empty fields ──────────────────────────────────

    [Fact]
    public async Task NameTooLong_Returns400()
    {
        var payload = new
        {
            name = new string('a', 201),
            email = "valid@test.local",
            subject = "demo",
            message = "Hello"
        };

        var response = await _client.PostAsJsonAsync("/api/contact", payload);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SubmissionWithoutChallengeToken_NoTurnstileKeyConfigured_Succeeds()
    {
        // The test factory has no TURNSTILE_SECRET_KEY, so the NoOp provider is
        // registered and token-less submissions must pass (fail-open contract).
        var response = await _client.PostAsJsonAsync("/api/contact", ValidPayload());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── Public access (no auth, no tenant) ────────────────────────────────

    [Fact]
    public async Task Endpoint_WorksWithoutAuthOrTenantHeader()
    {
        // Client was created without any auth or tenant headers
        var response = await _client.PostAsJsonAsync("/api/contact", ValidPayload());

        // Should NOT be 401 or 404 (tenant not found)
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── Notification email ──────────────────────────────────────────────────

    // The endpoint reads the notification address from the host's configuration per request, so
    // a test sets it on the shared configuration and clears it again afterwards.
    private async Task<HttpResponseMessage> SubmitWithNotificationAddressAsync(string? notifyEmail)
    {
        var configuration = _databaseFixture.Factory.Services.GetRequiredService<IConfiguration>();
        configuration[ConfigKeys.ContactNotificationEmail] = notifyEmail;
        try
        {
            return await _client.PostAsJsonAsync("/api/contact", ValidPayload());
        }
        finally
        {
            configuration[ConfigKeys.ContactNotificationEmail] = null;
        }
    }

    private MockEmailService Email => _databaseFixture.Factory.MockEmailService;

    [Fact]
    public async Task WhenNotificationEmailConfigured_SendsEmail()
    {
        var notifyEmail = $"ops-{Guid.NewGuid():N}@test.local";

        var response = await SubmitWithNotificationAddressAsync(notifyEmail);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        Email.Calls.Should().Contain((nameof(IEmailService.SendEmailAsync), notifyEmail));
    }

    [Fact]
    public async Task WhenNotificationEmailNotConfigured_DoesNotSendEmail()
    {
        var before = Email.CallCount(nameof(IEmailService.SendEmailAsync));

        var response = await SubmitWithNotificationAddressAsync(null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        Email.CallCount(nameof(IEmailService.SendEmailAsync)).Should().Be(before);
    }

    [Fact]
    public async Task WhenEmailSendingFails_SubmissionStillSucceeds()
    {
        Email.ThrowOnSendEmail = true;
        try
        {
            var response = await SubmitWithNotificationAddressAsync($"ops-{Guid.NewGuid():N}@test.local");

            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }
        finally
        {
            Email.ThrowOnSendEmail = false;
        }
    }
}
