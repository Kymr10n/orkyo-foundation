using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using Npgsql;
using Xunit;

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
        $"Host=localhost;Port={_databaseFixture.DatabasePort};Database=control_plane;Username=postgres;Password=postgres";

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

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task MissingOrEmptyName_Returns400(string? name)
    {
        var payload = new
        {
            name,
            email = "valid@test.local",
            subject = "demo",
            message = "Hello"
        };

        var response = await _client.PostAsJsonAsync("/api/contact", payload);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

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

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-an-email")]
    public async Task MissingOrInvalidEmail_Returns400(string? email)
    {
        var payload = new
        {
            name = "Test",
            email,
            subject = "demo",
            message = "Hello"
        };

        var response = await _client.PostAsJsonAsync("/api/contact", payload);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("invalid-subject")]
    [InlineData("DEMO")]
    public async Task MissingOrInvalidSubject_Returns400(string? subject)
    {
        var payload = new
        {
            name = "Test",
            email = "valid@test.local",
            subject,
            message = "Hello"
        };

        var response = await _client.PostAsJsonAsync("/api/contact", payload);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task MissingOrEmptyMessage_Returns400(string? message)
    {
        var payload = new
        {
            name = "Test",
            email = "valid@test.local",
            subject = "demo",
            message
        };

        var response = await _client.PostAsJsonAsync("/api/contact", payload);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task MessageTooLong_Returns400()
    {
        var payload = new
        {
            name = "Test",
            email = "valid@test.local",
            subject = "demo",
            message = new string('x', 5001)
        };

        var response = await _client.PostAsJsonAsync("/api/contact", payload);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CompanyTooLong_Returns400()
    {
        var payload = new
        {
            name = "Test",
            email = "valid@test.local",
            company = new string('c', 201),
            subject = "demo",
            message = "Hello"
        };

        var response = await _client.PostAsJsonAsync("/api/contact", payload);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
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

    [Fact(Skip = "Requires service override via WithWebHostBuilder - not supported by FoundationWebApplicationFactory")]
    public Task WhenNotificationEmailConfigured_SendsEmail() => Task.CompletedTask;

    [Fact(Skip = "Requires service override via WithWebHostBuilder - not supported by FoundationWebApplicationFactory")]
    public Task WhenNotificationEmailNotConfigured_DoesNotSendEmail() => Task.CompletedTask;

    [Fact(Skip = "Requires service override via WithWebHostBuilder - not supported by FoundationWebApplicationFactory")]
    public Task WhenEmailSendingFails_SubmissionStillSucceeds() => Task.CompletedTask;
}
