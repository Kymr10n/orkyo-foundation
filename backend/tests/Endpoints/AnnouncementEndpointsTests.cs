using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;

namespace Orkyo.Foundation.Tests.Endpoints;

/// <summary>
/// Integration tests for the announcement admin endpoints.
/// Tests GET/POST/PUT/DELETE /api/admin/announcements against the real DB.
/// All endpoints require authentication + site-admin realm role.
/// </summary>
[Collection("Database collection")]
public class AnnouncementEndpointsTests
{
    private readonly HttpClient _client;
    private readonly DatabaseFixture _fixture;
    private readonly string _connectionString;

    public AnnouncementEndpointsTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.Factory.CreateClient();
        _connectionString = $"Host=localhost;Port={_fixture.DatabasePort};Database=control_plane;Username=postgres;Password=postgres";
    }

    private async Task CleanupAsync()
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("DELETE FROM announcement_reads; DELETE FROM announcements", conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<(Guid UserId, string Token)> CreateSiteAdminAsync()
    {
        var userId = Guid.NewGuid();
        var email = $"siteadmin-{userId}@test.com";
        var keycloakSub = $"kc-{userId}";

        // Create user in DB
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO users (id, email, display_name, status) VALUES (@id, @email, 'Site Admin', 'active')",
            conn);
        cmd.Parameters.AddWithValue("id", userId);
        cmd.Parameters.AddWithValue("email", email);
        await cmd.ExecuteNonQueryAsync();

        // Link identity so ContextEnrichmentMiddleware can resolve
        await using var linkCmd = new NpgsqlCommand(
            "INSERT INTO user_identities (id, user_id, provider, provider_subject, provider_email) VALUES (@id, @userId, 'keycloak', @sub, @email)",
            conn);
        linkCmd.Parameters.AddWithValue("id", Guid.NewGuid());
        linkCmd.Parameters.AddWithValue("userId", userId);
        linkCmd.Parameters.AddWithValue("sub", keycloakSub);
        linkCmd.Parameters.AddWithValue("email", email);
        await linkCmd.ExecuteNonQueryAsync();

        // Build token with site-admin realm role
        var tokenData = new
        {
            UserId = userId.ToString(),
            Email = email,
            DisplayName = "Site Admin",
            TenantId = Guid.Empty.ToString(),
            TenantSlug = "",
            IsTenantAdmin = false,
            Role = "viewer",
            Sub = keycloakSub,
            RealmRoles = new[] { "user", "site-admin" },
        };
        var json = JsonSerializer.Serialize(tokenData);
        var token = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json));

        return (userId, token);
    }

    private async Task<string> CreateRegularUserTokenAsync()
    {
        var userId = Guid.NewGuid();
        var email = $"regular-{userId}@test.com";
        var keycloakSub = $"kc-{userId}";

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO users (id, email, display_name, status) VALUES (@id, @email, 'Regular User', 'active')",
            conn);
        cmd.Parameters.AddWithValue("id", userId);
        cmd.Parameters.AddWithValue("email", email);
        await cmd.ExecuteNonQueryAsync();

        await using var linkCmd = new NpgsqlCommand(
            "INSERT INTO user_identities (id, user_id, provider, provider_subject, provider_email) VALUES (@id, @userId, 'keycloak', @sub, @email)",
            conn);
        linkCmd.Parameters.AddWithValue("id", Guid.NewGuid());
        linkCmd.Parameters.AddWithValue("userId", userId);
        linkCmd.Parameters.AddWithValue("sub", keycloakSub);
        linkCmd.Parameters.AddWithValue("email", email);
        await linkCmd.ExecuteNonQueryAsync();

        var tokenData = new
        {
            UserId = userId.ToString(),
            Email = email,
            DisplayName = "Regular User",
            TenantId = Guid.Empty.ToString(),
            TenantSlug = "",
            IsTenantAdmin = false,
            Role = "viewer",
            Sub = keycloakSub,
            RealmRoles = new[] { "user" }, // No site-admin
        };
        var json = JsonSerializer.Serialize(tokenData);
        return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json));
    }

    private HttpRequestMessage AuthRequest(HttpMethod method, string url, string token, object? body = null)
    {
        var msg = new HttpRequestMessage(method, url);
        msg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body != null) msg.Content = JsonContent.Create(body);
        return msg;
    }

    // ========================================================================
    // Auth / Authorization
    // ========================================================================

    [Fact]
    public async Task GetAll_Unauthenticated_Returns401()
    {
        var response = await _client.GetAsync("/api/admin/announcements");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetAll_NonSiteAdmin_Returns403()
    {
        var token = await CreateRegularUserTokenAsync();
        var response = await _client.SendAsync(
            AuthRequest(HttpMethod.Get, "/api/admin/announcements", token));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Create_NonSiteAdmin_Returns403()
    {
        var token = await CreateRegularUserTokenAsync();
        var response = await _client.SendAsync(
            AuthRequest(HttpMethod.Post, "/api/admin/announcements", token,
                new { title = "Test", body = "Body" }));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ========================================================================
    // CRUD – Happy paths
    // ========================================================================

    [Fact]
    public async Task Create_ValidRequest_Returns201WithDto()
    {
        await CleanupAsync();
        var (_, token) = await CreateSiteAdminAsync();

        var response = await _client.SendAsync(
            AuthRequest(HttpMethod.Post, "/api/admin/announcements", token,
                new { title = "Maintenance Notice", body = "Servers will be down", isImportant = true, retentionDays = 30 }));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var dto = await response.Content.ReadFromJsonAsync<JsonElement>();
        dto.GetProperty("title").GetString().Should().Be("Maintenance Notice");
        dto.GetProperty("body").GetString().Should().Be("Servers will be down");
        dto.GetProperty("isImportant").GetBoolean().Should().BeTrue();
        dto.GetProperty("revision").GetInt32().Should().Be(1);
        dto.GetProperty("createdByEmail").GetString().Should().Contain("@test.com");
    }

    [Fact]
    public async Task GetAll_ReturnsList()
    {
        await CleanupAsync();
        var (_, token) = await CreateSiteAdminAsync();

        // Create two announcements
        await _client.SendAsync(
            AuthRequest(HttpMethod.Post, "/api/admin/announcements", token,
                new { title = "First", body = "Body 1" }));
        await _client.SendAsync(
            AuthRequest(HttpMethod.Post, "/api/admin/announcements", token,
                new { title = "Second", body = "Body 2" }));

        var response = await _client.SendAsync(
            AuthRequest(HttpMethod.Get, "/api/admin/announcements?includeExpired=true", token));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("announcements").GetArrayLength().Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task GetById_Existing_Returns200()
    {
        await CleanupAsync();
        var (_, token) = await CreateSiteAdminAsync();

        var createResp = await _client.SendAsync(
            AuthRequest(HttpMethod.Post, "/api/admin/announcements", token,
                new { title = "Lookup", body = "Find me" }));
        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetString();

        var response = await _client.SendAsync(
            AuthRequest(HttpMethod.Get, $"/api/admin/announcements/{id}", token));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<JsonElement>();
        dto.GetProperty("title").GetString().Should().Be("Lookup");
    }

    [Fact]
    public async Task GetById_NonExisting_Returns404()
    {
        var (_, token) = await CreateSiteAdminAsync();

        var response = await _client.SendAsync(
            AuthRequest(HttpMethod.Get, $"/api/admin/announcements/{Guid.NewGuid()}", token));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Update_Existing_Returns200WithUpdatedDto()
    {
        await CleanupAsync();
        var (_, token) = await CreateSiteAdminAsync();

        var createResp = await _client.SendAsync(
            AuthRequest(HttpMethod.Post, "/api/admin/announcements", token,
                new { title = "Original", body = "Original body" }));
        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetString();

        var updateResp = await _client.SendAsync(
            AuthRequest(HttpMethod.Put, $"/api/admin/announcements/{id}", token,
                new { title = "Updated", body = "Updated body", isImportant = true }));

        updateResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await updateResp.Content.ReadFromJsonAsync<JsonElement>();
        dto.GetProperty("title").GetString().Should().Be("Updated");
        dto.GetProperty("revision").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task Delete_Existing_Returns204()
    {
        await CleanupAsync();
        var (_, token) = await CreateSiteAdminAsync();

        var createResp = await _client.SendAsync(
            AuthRequest(HttpMethod.Post, "/api/admin/announcements", token,
                new { title = "Delete me", body = "Gone" }));
        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetString();

        var deleteResp = await _client.SendAsync(
            AuthRequest(HttpMethod.Delete, $"/api/admin/announcements/{id}", token));

        deleteResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify it's gone
        var getResp = await _client.SendAsync(
            AuthRequest(HttpMethod.Get, $"/api/admin/announcements/{id}", token));
        getResp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_NonExisting_Returns404()
    {
        var (_, token) = await CreateSiteAdminAsync();

        var response = await _client.SendAsync(
            AuthRequest(HttpMethod.Delete, $"/api/admin/announcements/{Guid.NewGuid()}", token));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ========================================================================
    // Validation
    // ========================================================================

    [Fact]
    public async Task Create_EmptyTitle_Returns400()
    {
        var (_, token) = await CreateSiteAdminAsync();

        var response = await _client.SendAsync(
            AuthRequest(HttpMethod.Post, "/api/admin/announcements", token,
                new { title = "", body = "Some body" }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_EmptyBody_Returns400()
    {
        var (_, token) = await CreateSiteAdminAsync();

        var response = await _client.SendAsync(
            AuthRequest(HttpMethod.Post, "/api/admin/announcements", token,
                new { title = "Good title", body = "" }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── Delivery channels ──────────────────────────────────────────────────

    private static string[] Channels(JsonElement dto) =>
        dto.GetProperty("channels").EnumerateArray().Select(c => c.GetString()!).ToArray();

    [Fact]
    public async Task Create_NoChannels_DefaultsToSite()
    {
        var (_, token) = await CreateSiteAdminAsync();
        var response = await _client.SendAsync(
            AuthRequest(HttpMethod.Post, "/api/admin/announcements", token,
                new { title = "Default", body = "Body" }));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        Channels(await response.Content.ReadFromJsonAsync<JsonElement>()).Should().Equal("site");
    }

    [Fact]
    public async Task Create_WithSiteAndEmail_PersistsBoth()
    {
        var (_, token) = await CreateSiteAdminAsync();
        var response = await _client.SendAsync(
            AuthRequest(HttpMethod.Post, "/api/admin/announcements", token,
                new { title = "Both", body = "Body", channels = new[] { "site", "email" } }));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        Channels(await response.Content.ReadFromJsonAsync<JsonElement>())
            .Should().BeEquivalentTo("site", "email");
    }

    [Fact]
    public async Task Create_EmailOnly_Persisted()
    {
        var (_, token) = await CreateSiteAdminAsync();
        var response = await _client.SendAsync(
            AuthRequest(HttpMethod.Post, "/api/admin/announcements", token,
                new { title = "Email", body = "Body", channels = new[] { "email" } }));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        Channels(await response.Content.ReadFromJsonAsync<JsonElement>()).Should().Equal("email");
    }

    [Fact]
    public async Task Create_UnknownChannel_Returns400()
    {
        var (_, token) = await CreateSiteAdminAsync();
        var response = await _client.SendAsync(
            AuthRequest(HttpMethod.Post, "/api/admin/announcements", token,
                new { title = "Bad", body = "Body", channels = new[] { "sms" } }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
