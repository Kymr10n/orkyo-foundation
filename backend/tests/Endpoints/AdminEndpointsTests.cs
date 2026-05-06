using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Orkyo.Foundation.Tests.Mocks;
using Npgsql;

namespace Orkyo.Foundation.Tests.Endpoints;

/// <summary>
/// Integration tests for the admin endpoints (tenants, users, memberships, break-glass).
/// These endpoints require site-admin realm role.
/// Tests validate auth gating, CRUD operations, and key business logic.
/// </summary>
[Collection("Database collection")]
public class AdminEndpointsTests
{
    private readonly HttpClient _client;
    private readonly DatabaseFixture _fixture;
    private readonly string _connectionString;

    public AdminEndpointsTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.Factory.CreateClient();
        _connectionString = $"Host=localhost;Port={_fixture.DatabasePort};Database=control_plane;Username=postgres;Password=postgres";
    }

    private MockKeycloakAdminService MockKeycloak => _fixture.Factory.MockKeycloakAdminService;

    private async Task<(Guid UserId, string Token)> CreateSiteAdminAsync()
    {
        var userId = Guid.NewGuid();
        var email = $"siteadmin-{userId}@test.com";
        var keycloakSub = $"kc-{userId}";

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO users (id, email, display_name, status) VALUES (@id, @email, 'Site Admin', 'active')",
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

    // ── Tenant Admin Endpoints ──────────────────────────────────────

    [Fact]
    public async Task GetTenants_NoAuth_Returns401()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/tenants");
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetTenants_NonSiteAdmin_Returns403()
    {
        var token = await CreateRegularUserTokenAsync();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/tenants");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetTenants_SiteAdmin_Returns200()
    {
        var (_, token) = await CreateSiteAdminAsync();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/tenants");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.TryGetProperty("tenants", out var tenants));
        Assert.Equal(JsonValueKind.Array, tenants.ValueKind);
    }

    [Fact]
    public async Task CreateTenant_SiteAdmin_Returns201()
    {
        var (_, token) = await CreateSiteAdminAsync();
        var slug = $"test-{Guid.NewGuid():N}"[..20];
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/admin/tenants")
        {
            Content = JsonContent.Create(new { slug, displayName = "Test Tenant" })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(slug, body.GetProperty("slug").GetString());
        Assert.Equal("active", body.GetProperty("status").GetString());
        Assert.Equal("Free", body.GetProperty("tier").GetString());
    }

    [Fact]
    public async Task CreateTenant_DuplicateSlug_Returns409()
    {
        var (_, token) = await CreateSiteAdminAsync();
        var slug = $"dup-{Guid.NewGuid():N}"[..20];

        // Create first
        var req1 = new HttpRequestMessage(HttpMethod.Post, "/api/admin/tenants")
        {
            Content = JsonContent.Create(new { slug, displayName = "First" })
        };
        req1.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        await _client.SendAsync(req1);

        // Create duplicate
        var req2 = new HttpRequestMessage(HttpMethod.Post, "/api/admin/tenants")
        {
            Content = JsonContent.Create(new { slug, displayName = "Second" })
        };
        req2.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(req2);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task CreateTenant_InvalidSlug_Returns400()
    {
        var (_, token) = await CreateSiteAdminAsync();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/admin/tenants")
        {
            Content = JsonContent.Create(new { slug = "INVALID SLUG!", displayName = "Test" })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateTenant_EmptySlug_Returns400()
    {
        var (_, token) = await CreateSiteAdminAsync();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/admin/tenants")
        {
            Content = JsonContent.Create(new { slug = "", displayName = "Test" })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateTenant_SiteAdmin_Returns200()
    {
        var (_, token) = await CreateSiteAdminAsync();
        var slug = $"upd-{Guid.NewGuid():N}"[..20];

        // Create tenant first
        var createReq = new HttpRequestMessage(HttpMethod.Post, "/api/admin/tenants")
        {
            Content = JsonContent.Create(new { slug, displayName = "Original" })
        };
        createReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var createResp = await _client.SendAsync(createReq);
        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var tenantId = created.GetProperty("id").GetString();

        // Update
        var updateReq = new HttpRequestMessage(HttpMethod.Patch, $"/api/admin/tenants/{tenantId}")
        {
            Content = JsonContent.Create(new { displayName = "Updated Name" })
        };
        updateReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(updateReq);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task UpdateTenant_InvalidStatus_Returns400()
    {
        var (_, token) = await CreateSiteAdminAsync();
        var slug = $"bad-{Guid.NewGuid():N}"[..20];

        var createReq = new HttpRequestMessage(HttpMethod.Post, "/api/admin/tenants")
        {
            Content = JsonContent.Create(new { slug, displayName = "Test" })
        };
        createReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var createResp = await _client.SendAsync(createReq);
        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var tenantId = created.GetProperty("id").GetString();

        var updateReq = new HttpRequestMessage(HttpMethod.Patch, $"/api/admin/tenants/{tenantId}")
        {
            Content = JsonContent.Create(new { status = "invalid-status" })
        };
        updateReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(updateReq);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateTenant_NotFound_Returns404()
    {
        var (_, token) = await CreateSiteAdminAsync();
        var updateReq = new HttpRequestMessage(HttpMethod.Patch, $"/api/admin/tenants/{Guid.NewGuid()}")
        {
            Content = JsonContent.Create(new { displayName = "Ghost" })
        };
        updateReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(updateReq);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteTenant_SiteAdmin_Returns204()
    {
        var (_, token) = await CreateSiteAdminAsync();
        var slug = $"del-{Guid.NewGuid():N}"[..20];

        var createReq = new HttpRequestMessage(HttpMethod.Post, "/api/admin/tenants")
        {
            Content = JsonContent.Create(new { slug, displayName = "To Delete" })
        };
        createReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var createResp = await _client.SendAsync(createReq);
        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var tenantId = created.GetProperty("id").GetString();

        var deleteReq = new HttpRequestMessage(HttpMethod.Delete, $"/api/admin/tenants/{tenantId}");
        deleteReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(deleteReq);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task DeleteTenant_SiteAdmin_SetsStatusToDeleting()
    {
        var (_, token) = await CreateSiteAdminAsync();
        var slug = $"del2-{Guid.NewGuid():N}"[..20];

        var createReq = new HttpRequestMessage(HttpMethod.Post, "/api/admin/tenants")
        {
            Content = JsonContent.Create(new { slug, displayName = "To Soft Delete" })
        };
        createReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var createResp = await _client.SendAsync(createReq);
        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var tenantId = Guid.Parse(created.GetProperty("id").GetString()!);

        var deleteReq = new HttpRequestMessage(HttpMethod.Delete, $"/api/admin/tenants/{tenantId}");
        deleteReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        await _client.SendAsync(deleteReq);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT status FROM tenants WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", tenantId);
        Assert.Equal("deleting", await cmd.ExecuteScalarAsync());
    }

    [Fact]
    public async Task DeleteTenant_SiteAdmin_ClearsOwnerReference()
    {
        var (userId, token) = await CreateSiteAdminAsync();

        // Create a tenant owned by the site admin
        var tenantId = await CreateTenantWithOwnerAsync(userId);

        var deleteReq = new HttpRequestMessage(HttpMethod.Delete, $"/api/admin/tenants/{tenantId}");
        deleteReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        await _client.SendAsync(deleteReq);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT owner_user_id FROM tenants WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", tenantId);
        Assert.True(await cmd.ExecuteScalarAsync() is DBNull or null);
    }

    [Fact]
    public async Task DeleteTenant_NonSiteAdmin_Returns403()
    {
        var token = await CreateRegularUserTokenAsync();
        var deleteReq = new HttpRequestMessage(HttpMethod.Delete, $"/api/admin/tenants/{Guid.NewGuid()}");
        deleteReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(deleteReq);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task DeleteTenant_NotFound_Returns404()
    {
        var (_, token) = await CreateSiteAdminAsync();
        var deleteReq = new HttpRequestMessage(HttpMethod.Delete, $"/api/admin/tenants/{Guid.NewGuid()}");
        deleteReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(deleteReq);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── Tenant Tier Endpoints ────────────────────────────────────────

    [Fact]
    public async Task UpdateTenantTier_SiteAdmin_Returns200()
    {
        var (_, token) = await CreateSiteAdminAsync();
        var slug = $"tier-{Guid.NewGuid():N}"[..20];

        var createReq = new HttpRequestMessage(HttpMethod.Post, "/api/admin/tenants")
        {
            Content = JsonContent.Create(new { slug, displayName = "Tier Test" })
        };
        createReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var createResp = await _client.SendAsync(createReq);
        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var tenantId = created.GetProperty("id").GetString();

        var updateReq = new HttpRequestMessage(HttpMethod.Patch, $"/api/admin/tenants/{tenantId}/tier")
        {
            Content = JsonContent.Create(new { tier = "Professional" })
        };
        updateReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(updateReq);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Professional", body.GetProperty("tier").GetString());
    }

    [Fact]
    public async Task UpdateTenantTier_InvalidTier_Returns400()
    {
        var (_, token) = await CreateSiteAdminAsync();
        var slug = $"tier-{Guid.NewGuid():N}"[..20];

        var createReq = new HttpRequestMessage(HttpMethod.Post, "/api/admin/tenants")
        {
            Content = JsonContent.Create(new { slug, displayName = "Tier Test" })
        };
        createReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var createResp = await _client.SendAsync(createReq);
        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var tenantId = created.GetProperty("id").GetString();

        var updateReq = new HttpRequestMessage(HttpMethod.Patch, $"/api/admin/tenants/{tenantId}/tier")
        {
            Content = JsonContent.Create(new { tier = "InvalidTier" })
        };
        updateReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(updateReq);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateTenantTier_NotFound_Returns404()
    {
        var (_, token) = await CreateSiteAdminAsync();
        var updateReq = new HttpRequestMessage(HttpMethod.Patch, $"/api/admin/tenants/{Guid.NewGuid()}/tier")
        {
            Content = JsonContent.Create(new { tier = "Professional" })
        };
        updateReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(updateReq);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateTenantTier_SameTier_ReturnsOkWithAlreadyMessage()
    {
        var (_, token) = await CreateSiteAdminAsync();
        var slug = $"tier-{Guid.NewGuid():N}"[..20];

        var createReq = new HttpRequestMessage(HttpMethod.Post, "/api/admin/tenants")
        {
            Content = JsonContent.Create(new { slug, displayName = "Same Tier" })
        };
        createReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var createResp = await _client.SendAsync(createReq);
        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var tenantId = created.GetProperty("id").GetString();

        // Default tier is Free, try setting it to Free again
        var updateReq = new HttpRequestMessage(HttpMethod.Patch, $"/api/admin/tenants/{tenantId}/tier")
        {
            Content = JsonContent.Create(new { tier = "Free" })
        };
        updateReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(updateReq);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("already", body.GetProperty("message").GetString()!);
    }

    [Fact]
    public async Task UpdateTenantTier_NonSiteAdmin_Returns403()
    {
        var token = await CreateRegularUserTokenAsync();
        var updateReq = new HttpRequestMessage(HttpMethod.Patch, $"/api/admin/tenants/{Guid.NewGuid()}/tier")
        {
            Content = JsonContent.Create(new { tier = "Professional" })
        };
        updateReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(updateReq);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UpdateTenantTier_PersistsTierInDatabase()
    {
        var (_, token) = await CreateSiteAdminAsync();
        var slug = $"tier-{Guid.NewGuid():N}"[..20];

        var createReq = new HttpRequestMessage(HttpMethod.Post, "/api/admin/tenants")
        {
            Content = JsonContent.Create(new { slug, displayName = "Persist Tier" })
        };
        createReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var createResp = await _client.SendAsync(createReq);
        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var tenantId = Guid.Parse(created.GetProperty("id").GetString()!);

        var updateReq = new HttpRequestMessage(HttpMethod.Patch, $"/api/admin/tenants/{tenantId}/tier")
        {
            Content = JsonContent.Create(new { tier = "Enterprise" })
        };
        updateReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        await _client.SendAsync(updateReq);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT tier FROM tenants WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", tenantId);
        Assert.Equal(2, await cmd.ExecuteScalarAsync()); // Enterprise = 2
    }

    [Fact]
    public async Task UpdateTenantTier_RecordsAuditEvent()
    {
        var (_, token) = await CreateSiteAdminAsync();
        var slug = $"tier-{Guid.NewGuid():N}"[..20];

        var createReq = new HttpRequestMessage(HttpMethod.Post, "/api/admin/tenants")
        {
            Content = JsonContent.Create(new { slug, displayName = "Audit Tier" })
        };
        createReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var createResp = await _client.SendAsync(createReq);
        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var tenantId = created.GetProperty("id").GetString();

        var updateReq = new HttpRequestMessage(HttpMethod.Patch, $"/api/admin/tenants/{tenantId}/tier")
        {
            Content = JsonContent.Create(new { tier = "Professional" })
        };
        updateReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        await _client.SendAsync(updateReq);

        var auditReq = new HttpRequestMessage(HttpMethod.Get, $"/api/admin/audit?action=tenant.tier_changed&targetId={tenantId}");
        auditReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var auditResp = await _client.SendAsync(auditReq);
        var body = await auditResp.Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(body.GetProperty("events").GetArrayLength() >= 1, "tenant.tier_changed audit event should exist");
    }

    [Fact]
    public async Task UpdateTenantTier_DowngradeWithTooManyMembers_Returns409()
    {
        var (adminId, token) = await CreateSiteAdminAsync();
        var slug = $"tier-{Guid.NewGuid():N}"[..20];

        // Create tenant and upgrade to Professional
        var createReq = new HttpRequestMessage(HttpMethod.Post, "/api/admin/tenants")
        {
            Content = JsonContent.Create(new { slug, displayName = "Downgrade Test" })
        };
        createReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var createResp = await _client.SendAsync(createReq);
        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var tenantId = Guid.Parse(created.GetProperty("id").GetString()!);

        // Upgrade to Professional first
        var upgradeReq = new HttpRequestMessage(HttpMethod.Patch, $"/api/admin/tenants/{tenantId}/tier")
        {
            Content = JsonContent.Create(new { tier = "Professional" })
        };
        upgradeReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        await _client.SendAsync(upgradeReq);

        // Add 6 active members (Free tier allows max 5)
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        for (var i = 0; i < 6; i++)
        {
            var userId = Guid.NewGuid();
            await using var userCmd = new NpgsqlCommand(
                "INSERT INTO users (id, email, display_name, status) VALUES (@id, @email, 'Member', 'active')", conn);
            userCmd.Parameters.AddWithValue("id", userId);
            userCmd.Parameters.AddWithValue("email", $"member-{userId}@test.com");
            await userCmd.ExecuteNonQueryAsync();

            await using var memberCmd = new NpgsqlCommand(
                "INSERT INTO tenant_memberships (id, user_id, tenant_id, role, status) VALUES (@id, @userId, @tenantId, 'viewer', 'active')", conn);
            memberCmd.Parameters.AddWithValue("id", Guid.NewGuid());
            memberCmd.Parameters.AddWithValue("userId", userId);
            memberCmd.Parameters.AddWithValue("tenantId", tenantId);
            await memberCmd.ExecuteNonQueryAsync();
        }

        // Try to downgrade to Free — should fail
        var downgradeReq = new HttpRequestMessage(HttpMethod.Patch, $"/api/admin/tenants/{tenantId}/tier")
        {
            Content = JsonContent.Create(new { tier = "Free" })
        };
        downgradeReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(downgradeReq);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("Cannot downgrade", body.GetProperty("error").GetString()!);
    }

    // ── User Admin Endpoints ────────────────────────────────────────

    [Fact]
    public async Task GetUsers_SiteAdmin_Returns200()
    {
        var (_, token) = await CreateSiteAdminAsync();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/users");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.TryGetProperty("users", out var users));
        Assert.Equal(JsonValueKind.Array, users.ValueKind);
    }

    [Fact]
    public async Task GetUsers_NonSiteAdmin_Returns403()
    {
        var token = await CreateRegularUserTokenAsync();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/users");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetUser_SiteAdmin_Returns200()
    {
        var (userId, token) = await CreateSiteAdminAsync();
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/admin/users/{userId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(userId.ToString(), body.GetProperty("id").GetString());
    }

    [Fact]
    public async Task GetUser_NotFound_Returns404()
    {
        var (_, token) = await CreateSiteAdminAsync();
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/admin/users/{Guid.NewGuid()}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── Break Glass Endpoints ───────────────────────────────────────

    [Fact]
    public async Task BreakGlassEntry_SiteAdmin_Returns200()
    {
        var (_, token) = await CreateSiteAdminAsync();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/admin/break-glass/entry")
        {
            Content = JsonContent.Create(new { tenantSlug = "test-tenant", reason = "Emergency access" })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.TryGetProperty("sessionId", out var sessionIdProp));
        Assert.False(string.IsNullOrEmpty(sessionIdProp.GetString()));
        Assert.True(body.TryGetProperty("createdAt", out var createdAtProp));
        Assert.True(DateTimeOffset.TryParse(createdAtProp.GetString(), out _));
        Assert.True(body.TryGetProperty("expiresAt", out var expiresAtProp));
        Assert.True(DateTimeOffset.TryParse(expiresAtProp.GetString(), out _));
        Assert.True(body.TryGetProperty("absoluteExpiresAt", out var absoluteExpiresAtProp));
        Assert.True(DateTimeOffset.TryParse(absoluteExpiresAtProp.GetString(), out _));
    }

    [Fact]
    public async Task BreakGlassEntry_MissingReason_Returns400()
    {
        var (_, token) = await CreateSiteAdminAsync();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/admin/break-glass/entry")
        {
            Content = JsonContent.Create(new { tenantSlug = "test-tenant" })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task BreakGlassEntry_NonSiteAdmin_Returns403()
    {
        var token = await CreateRegularUserTokenAsync();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/admin/break-glass/entry")
        {
            Content = JsonContent.Create(new { tenantSlug = "test-tenant", reason = "Attempting unauthorized access" })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task BreakGlassExit_SiteAdmin_Returns200()
    {
        var (_, token) = await CreateSiteAdminAsync();

        // Create a session first so we have a valid sessionId to revoke
        var entryRequest = new HttpRequestMessage(HttpMethod.Post, "/api/admin/break-glass/entry")
        {
            Content = JsonContent.Create(new { tenantSlug = "test-tenant", reason = "Emergency access for incident investigation" })
        };
        entryRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var entryResponse = await _client.SendAsync(entryRequest);
        Assert.Equal(HttpStatusCode.OK, entryResponse.StatusCode);
        var entryBody = await entryResponse.Content.ReadFromJsonAsync<JsonElement>();
        var sessionId = entryBody.GetProperty("sessionId").GetString();

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/admin/break-glass/exit")
        {
            Content = JsonContent.Create(new { sessionId })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("success").GetBoolean());
        Assert.True(body.GetProperty("revoked").GetBoolean());
    }

    [Fact]
    public async Task BreakGlassExit_MissingSessionId_Returns400()
    {
        var (_, token) = await CreateSiteAdminAsync();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/admin/break-glass/exit")
        {
            Content = JsonContent.Create(new { })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task BreakGlassEntry_ShortReason_Returns400()
    {
        var (_, token) = await CreateSiteAdminAsync();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/admin/break-glass/entry")
        {
            Content = JsonContent.Create(new { tenantSlug = "test-tenant", reason = "too short" })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task BreakGlassExit_WrongAdmin_ReturnsRevokedFalse()
    {
        var (_, tokenA) = await CreateSiteAdminAsync();
        var (_, tokenB) = await CreateSiteAdminAsync();

        // Admin A opens a session
        var entryRequest = new HttpRequestMessage(HttpMethod.Post, "/api/admin/break-glass/entry")
        {
            Content = JsonContent.Create(new { tenantSlug = "test-tenant", reason = "Admin A emergency access" })
        };
        entryRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenA);
        var entryResponse = await _client.SendAsync(entryRequest);
        Assert.Equal(HttpStatusCode.OK, entryResponse.StatusCode);
        var sessionId = (await entryResponse.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("sessionId").GetString();

        // Admin B tries to revoke Admin A's session — must be rejected
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/admin/break-glass/exit")
        {
            Content = JsonContent.Create(new { sessionId })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenB);
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("success").GetBoolean());
        Assert.False(body.GetProperty("revoked").GetBoolean());
    }

    [Fact]
    public async Task BreakGlassRenew_SiteAdmin_Returns200WithExtendedExpiry()
    {
        var (_, token) = await CreateSiteAdminAsync();

        // Create a session
        var entryRequest = new HttpRequestMessage(HttpMethod.Post, "/api/admin/break-glass/entry")
        {
            Content = JsonContent.Create(new { tenantSlug = "test-tenant", reason = "Emergency access for renewal test" })
        };
        entryRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var entryResponse = await _client.SendAsync(entryRequest);
        Assert.Equal(HttpStatusCode.OK, entryResponse.StatusCode);
        var entryBody = await entryResponse.Content.ReadFromJsonAsync<JsonElement>();
        var sessionId = entryBody.GetProperty("sessionId").GetString();
        var originalExpiry = DateTimeOffset.Parse(entryBody.GetProperty("expiresAt").GetString()!);

        // Renew
        var renewRequest = new HttpRequestMessage(HttpMethod.Post, "/api/admin/break-glass/renew")
        {
            Content = JsonContent.Create(new { sessionId })
        };
        renewRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var renewResponse = await _client.SendAsync(renewRequest);
        Assert.Equal(HttpStatusCode.OK, renewResponse.StatusCode);

        var renewBody = await renewResponse.Content.ReadFromJsonAsync<JsonElement>();
        var newExpiry = DateTimeOffset.Parse(renewBody.GetProperty("expiresAt").GetString()!);
        Assert.True(newExpiry > originalExpiry);
        Assert.True(renewBody.TryGetProperty("absoluteExpiresAt", out _));
    }

    [Fact]
    public async Task BreakGlassRenew_UnknownSession_Returns404()
    {
        var (_, token) = await CreateSiteAdminAsync();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/admin/break-glass/renew")
        {
            Content = JsonContent.Create(new { sessionId = "nonexistent00000000000000000000" })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task BreakGlassRenew_NonSiteAdmin_Returns403()
    {
        var token = await CreateRegularUserTokenAsync();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/admin/break-glass/renew")
        {
            Content = JsonContent.Create(new { sessionId = "doesnotmatter0000000000000000000" })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task BreakGlassStatus_ReturnsActiveSession()
    {
        var (_, token) = await CreateSiteAdminAsync();

        // Create a session
        var entryRequest = new HttpRequestMessage(HttpMethod.Post, "/api/admin/break-glass/entry")
        {
            Content = JsonContent.Create(new { tenantSlug = "status-tenant", reason = "Emergency access for status test" })
        };
        entryRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var entryResponse = await _client.SendAsync(entryRequest);
        Assert.Equal(HttpStatusCode.OK, entryResponse.StatusCode);

        // Fetch status
        var statusRequest = new HttpRequestMessage(HttpMethod.Get, "/api/admin/break-glass/session/status-tenant");
        statusRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var statusResponse = await _client.SendAsync(statusRequest);
        Assert.Equal(HttpStatusCode.OK, statusResponse.StatusCode);

        var body = await statusResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("status-tenant", body.GetProperty("tenantSlug").GetString());
        Assert.True(body.TryGetProperty("createdAt", out _));
        Assert.True(body.TryGetProperty("expiresAt", out _));
        Assert.True(body.TryGetProperty("absoluteExpiresAt", out _));
    }

    [Fact]
    public async Task BreakGlassStatus_NoSession_Returns404()
    {
        var (_, token) = await CreateSiteAdminAsync();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/break-glass/session/nonexistent");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── User Lifecycle Admin Endpoints ─────────────────────────────

    private async Task<Guid> CreateTenantWithOwnerAsync(Guid ownerId)
    {
        var tenantId = Guid.NewGuid();
        var slug = $"owned-{tenantId:N}"[..20];

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(@"
            INSERT INTO tenants (id, slug, display_name, db_identifier, status, owner_user_id, created_at, updated_at)
            VALUES (@id, @slug, 'Owned Tenant', @dbId, 'active', @ownerId, NOW(), NOW())",
            conn);
        cmd.Parameters.AddWithValue("id", tenantId);
        cmd.Parameters.AddWithValue("slug", slug);
        cmd.Parameters.AddWithValue("dbId", $"tenant_{slug}");
        cmd.Parameters.AddWithValue("ownerId", ownerId);
        await cmd.ExecuteNonQueryAsync();

        return tenantId;
    }

    [Fact]
    public async Task DeactivateUser_NonSiteAdmin_Returns403()
    {
        var token = await CreateRegularUserTokenAsync();
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/users/{Guid.NewGuid()}/deactivate");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task DeactivateUser_SiteAdmin_Returns204_AndCallsKeycloakDisable()
    {
        var (userId, token) = await CreateSiteAdminAsync();
        MockKeycloak.Reset();

        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/users/{userId}/deactivate");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(1, MockKeycloak.DisableUserCallCount);
        Assert.Equal($"kc-{userId}", MockKeycloak.LastDisabledKeycloakId);
    }

    [Fact]
    public async Task DeactivateUser_SiteAdmin_SetsDbStatusToDisabled()
    {
        var (userId, token) = await CreateSiteAdminAsync();

        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/users/{userId}/deactivate");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        await _client.SendAsync(request);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT status FROM users WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", userId);
        var status = await cmd.ExecuteScalarAsync();
        Assert.Equal("disabled", status);
    }

    [Fact]
    public async Task DeactivateUser_OwnsActiveTenant_Returns204()
    {
        var (userId, token) = await CreateSiteAdminAsync();
        await CreateTenantWithOwnerAsync(userId);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/users/{userId}/deactivate");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task ReactivateUser_NonSiteAdmin_Returns403()
    {
        var token = await CreateRegularUserTokenAsync();
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/users/{Guid.NewGuid()}/reactivate");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ReactivateUser_SiteAdmin_Returns204_AndCallsKeycloakEnable()
    {
        var (userId, token) = await CreateSiteAdminAsync();

        // Deactivate first
        var deactivateReq = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/users/{userId}/deactivate");
        deactivateReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        await _client.SendAsync(deactivateReq);

        MockKeycloak.Reset();

        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/users/{userId}/reactivate");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(1, MockKeycloak.EnableUserCallCount);
        Assert.Equal($"kc-{userId}", MockKeycloak.LastEnabledKeycloakId);
    }

    [Fact]
    public async Task ReactivateUser_SiteAdmin_RestoresDbStatusToActive()
    {
        var (userId, token) = await CreateSiteAdminAsync();

        var deactivateReq = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/users/{userId}/deactivate");
        deactivateReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        await _client.SendAsync(deactivateReq);

        var reactivateReq = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/users/{userId}/reactivate");
        reactivateReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        await _client.SendAsync(reactivateReq);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT status FROM users WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", userId);
        var status = await cmd.ExecuteScalarAsync();
        Assert.Equal("active", status);
    }

    [Fact]
    public async Task DeleteUser_NonSiteAdmin_Returns403()
    {
        var token = await CreateRegularUserTokenAsync();
        var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/admin/users/{Guid.NewGuid()}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task DeleteUser_SiteAdmin_Returns204_AndCallsKeycloakDelete()
    {
        // Create a target user and a separate admin to issue the delete
        var (targetId, _) = await CreateSiteAdminAsync();
        var (_, adminToken) = await CreateSiteAdminAsync();
        MockKeycloak.Reset();

        var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/admin/users/{targetId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(1, MockKeycloak.DeleteUserCallCount);
        Assert.Equal($"kc-{targetId}", MockKeycloak.LastDeletedKeycloakId);
    }

    [Fact]
    public async Task DeleteUser_SiteAdmin_RemovesUserFromDatabase()
    {
        var targetId = Guid.NewGuid();
        await using (var conn = new NpgsqlConnection(_connectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                "INSERT INTO users (id, email, display_name, status) VALUES (@id, @email, 'Target', 'active')",
                conn);
            cmd.Parameters.AddWithValue("id", targetId);
            cmd.Parameters.AddWithValue("email", $"target-{targetId}@test.com");
            await cmd.ExecuteNonQueryAsync();
        }

        var (_, adminToken) = await CreateSiteAdminAsync();
        var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/admin/users/{targetId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        await _client.SendAsync(request);

        await using var checkConn = new NpgsqlConnection(_connectionString);
        await checkConn.OpenAsync();
        await using var checkCmd = new NpgsqlCommand("SELECT COUNT(*) FROM users WHERE id = @id", checkConn);
        checkCmd.Parameters.AddWithValue("id", targetId);
        Assert.Equal(0, Convert.ToInt32(await checkCmd.ExecuteScalarAsync()));
    }

    [Fact]
    public async Task DeleteUser_OwnsActiveTenant_Returns204_AndTenantBecomesOwnerless()
    {
        var (userId, _) = await CreateSiteAdminAsync();
        var tenantId = await CreateTenantWithOwnerAsync(userId);
        var (_, adminToken) = await CreateSiteAdminAsync();

        var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/admin/users/{userId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // Verify the tenant still exists but has no owner
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT owner_user_id FROM tenants WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", tenantId);
        var ownerUserId = await cmd.ExecuteScalarAsync();
        Assert.Equal(DBNull.Value, ownerUserId);
    }

    [Fact]
    public async Task DeactivateUser_OwnsSuspendedTenant_Returns204()
    {
        var (userId, token) = await CreateSiteAdminAsync();
        var tenantId = await CreateTenantWithOwnerAsync(userId);

        var suspendReq = new HttpRequestMessage(HttpMethod.Patch, $"/api/admin/tenants/{tenantId}")
        {
            Content = JsonContent.Create(new { status = "suspended" })
        };
        suspendReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        await _client.SendAsync(suspendReq);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/users/{userId}/deactivate");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    // ── Site-Admin Role Management ──────────────────────────────────────

    [Fact]
    public async Task PromoteSiteAdmin_NonSiteAdmin_Returns403()
    {
        var token = await CreateRegularUserTokenAsync();
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/users/{Guid.NewGuid()}/promote-site-admin");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PromoteSiteAdmin_SiteAdmin_Returns204_AndCallsKeycloakAssign()
    {
        var (adminId, adminToken) = await CreateSiteAdminAsync();
        var (targetId, _) = await CreateSiteAdminAsync(); // creates user with keycloak identity
        MockKeycloak.Reset();

        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/users/{targetId}/promote-site-admin");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(1, MockKeycloak.AssignRealmRoleCallCount);
        Assert.Equal("site-admin", MockKeycloak.LastAssignRealmRoleCall.roleName);
    }

    [Fact]
    public async Task PromoteSiteAdmin_AlreadySiteAdmin_Returns409()
    {
        var (adminId, adminToken) = await CreateSiteAdminAsync();
        var (targetId, _) = await CreateSiteAdminAsync();
        MockKeycloak.Reset();
        MockKeycloak.MockRealmRoles.Add("site-admin");

        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/users/{targetId}/promote-site-admin");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task PromoteSiteAdmin_NoKeycloakIdentity_Returns422()
    {
        var (adminId, adminToken) = await CreateSiteAdminAsync();

        // Create a user without Keycloak identity
        var userId = Guid.NewGuid();
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO users (id, email, display_name, status) VALUES (@id, @email, 'No KC', 'active')", conn);
        cmd.Parameters.AddWithValue("id", userId);
        cmd.Parameters.AddWithValue("email", $"nokc-{userId}@test.com");
        await cmd.ExecuteNonQueryAsync();

        MockKeycloak.Reset();

        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/users/{userId}/promote-site-admin");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task PromoteSiteAdmin_NonExistentUser_Returns404()
    {
        var (adminId, adminToken) = await CreateSiteAdminAsync();

        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/users/{Guid.NewGuid()}/promote-site-admin");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PromoteSiteAdmin_StaleKeycloakIdentity_Returns422()
    {
        var (adminId, adminToken) = await CreateSiteAdminAsync();

        // Create a user with a Keycloak identity that doesn't actually exist in Keycloak
        var userId = Guid.NewGuid();
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO users (id, email, display_name, status) VALUES (@id, @email, 'Stale KC', 'active')", conn);
        cmd.Parameters.AddWithValue("id", userId);
        cmd.Parameters.AddWithValue("email", $"stale-{userId}@test.com");
        await cmd.ExecuteNonQueryAsync();
        await using var idCmd = new NpgsqlCommand(
            "INSERT INTO user_identities (id, user_id, provider, provider_subject, provider_email) VALUES (@id, @userId, 'keycloak', @sub, @email)", conn);
        idCmd.Parameters.AddWithValue("id", Guid.NewGuid());
        idCmd.Parameters.AddWithValue("userId", userId);
        idCmd.Parameters.AddWithValue("sub", Guid.NewGuid().ToString());
        idCmd.Parameters.AddWithValue("email", $"stale-{userId}@test.com");
        await idCmd.ExecuteNonQueryAsync();

        // Simulate Keycloak returning 404 for this user
        MockKeycloak.Reset();
        MockKeycloak.HasRealmRoleError_ = true;
        MockKeycloak.HasRealmRoleErrorStatusCode = 404;

        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/users/{userId}/promote-site-admin");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task RevokeSiteAdmin_NonSiteAdmin_Returns403()
    {
        var token = await CreateRegularUserTokenAsync();
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/users/{Guid.NewGuid()}/revoke-site-admin");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RevokeSiteAdmin_SiteAdmin_Returns204_AndCallsKeycloakRevoke()
    {
        var (adminId, adminToken) = await CreateSiteAdminAsync();
        var (targetId, _) = await CreateSiteAdminAsync();
        MockKeycloak.Reset();
        MockKeycloak.MockRealmRoles.Add("site-admin");

        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/users/{targetId}/revoke-site-admin");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(1, MockKeycloak.RevokeRealmRoleCallCount);
        Assert.Equal("site-admin", MockKeycloak.LastRevokeRealmRoleCall.roleName);
    }

    [Fact]
    public async Task RevokeSiteAdmin_Self_Returns400()
    {
        var (adminId, adminToken) = await CreateSiteAdminAsync();
        MockKeycloak.Reset();

        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/users/{adminId}/revoke-site-admin");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("own", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task RevokeSiteAdmin_NotSiteAdmin_Returns409()
    {
        var (adminId, adminToken) = await CreateSiteAdminAsync();
        var (targetId, _) = await CreateSiteAdminAsync();
        MockKeycloak.Reset();
        // MockRealmRoles is empty — user does not have site-admin

        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/users/{targetId}/revoke-site-admin");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task RevokeSiteAdmin_LastSiteAdmin_Returns400()
    {
        var (adminId, adminToken) = await CreateSiteAdminAsync();
        var (targetId, _) = await CreateSiteAdminAsync();
        MockKeycloak.Reset();
        MockKeycloak.MockRealmRoles.Add("site-admin");
        MockKeycloak.CountRealmRoleMembersResult = 1;

        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/users/{targetId}/revoke-site-admin");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("last", body.GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, MockKeycloak.RevokeRealmRoleCallCount);
    }

    [Fact]
    public async Task GetUsers_IncludesIsSiteAdminField()
    {
        var (userId, token) = await CreateSiteAdminAsync();
        MockKeycloak.Reset();
        MockKeycloak.MockRealmRoles.Add("site-admin");

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/users");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var users = body.GetProperty("users");
        Assert.True(users.GetArrayLength() > 0);

        // Find our site-admin user
        var found = false;
        foreach (var user in users.EnumerateArray())
        {
            if (user.GetProperty("id").GetString() == userId.ToString())
            {
                Assert.True(user.GetProperty("isSiteAdmin").GetBoolean());
                found = true;
                break;
            }
        }
        Assert.True(found, "Site-admin user should appear in the list");
    }

    [Fact]
    public async Task GetUsers_IncludesOwnedTenantTier()
    {
        var (userId, token) = await CreateSiteAdminAsync();
        var tenantId = await CreateTenantWithOwnerAsync(userId);

        // Upgrade the tenant tier
        var tierReq = new HttpRequestMessage(HttpMethod.Patch, $"/api/admin/tenants/{tenantId}/tier")
        {
            Content = JsonContent.Create(new { tier = "Professional" })
        };
        tierReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        await _client.SendAsync(tierReq);

        MockKeycloak.Reset();

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/users");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var users = body.GetProperty("users");

        var found = false;
        foreach (var user in users.EnumerateArray())
        {
            if (user.GetProperty("id").GetString() == userId.ToString())
            {
                Assert.Equal(tenantId.ToString(), user.GetProperty("ownedTenantId").GetString());
                Assert.Equal("Professional", user.GetProperty("ownedTenantTier").GetString());
                found = true;
                break;
            }
        }
        Assert.True(found, "User with owned tenant should appear in the list with tier info");
    }

    [Fact]
    public async Task GetUser_IncludesOwnedTenantTier()
    {
        var (userId, token) = await CreateSiteAdminAsync();
        var tenantId = await CreateTenantWithOwnerAsync(userId);

        MockKeycloak.Reset();

        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/admin/users/{userId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(tenantId.ToString(), body.GetProperty("ownedTenantId").GetString());
        Assert.Equal("Free", body.GetProperty("ownedTenantTier").GetString());
    }

    [Fact]
    public async Task GetUser_WithoutOwnedTenant_ReturnsNullTier()
    {
        var (userId, token) = await CreateSiteAdminAsync();
        MockKeycloak.Reset();

        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/admin/users/{userId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, body.GetProperty("ownedTenantId").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("ownedTenantTier").ValueKind);
    }

    // ── Audit Endpoints ─────────────────────────────────────────────

    private async Task SeedAuditEventAsync(Guid? actorUserId, string action, string? targetType = null, string? targetId = null, string? metadata = null)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(@"
            INSERT INTO audit_events (id, actor_user_id, actor_type, action, target_type, target_id, metadata, created_at)
            VALUES (@id, @actorUserId, @actorType, @action, @targetType, @targetId, @metadata::jsonb, NOW())", conn);
        cmd.Parameters.AddWithValue("id", Guid.NewGuid());
        cmd.Parameters.AddWithValue("actorUserId", actorUserId.HasValue ? actorUserId.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("actorType", actorUserId.HasValue ? "user" : "system");
        cmd.Parameters.AddWithValue("action", action);
        cmd.Parameters.AddWithValue("targetType", (object?)targetType ?? DBNull.Value);
        cmd.Parameters.AddWithValue("targetId", (object?)targetId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("metadata", (object?)metadata ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task GetAuditEvents_NoAuth_Returns401()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/audit");
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetAuditEvents_NonSiteAdmin_Returns403()
    {
        var token = await CreateRegularUserTokenAsync();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/audit");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetAuditEvents_SiteAdmin_Returns200WithPagination()
    {
        var (userId, token) = await CreateSiteAdminAsync();
        await SeedAuditEventAsync(userId, "test.action", "test", "123");

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/audit");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.TryGetProperty("events", out var events));
        Assert.Equal(JsonValueKind.Array, events.ValueKind);
        Assert.True(body.GetProperty("totalCount").GetInt32() > 0);
        Assert.True(body.GetProperty("page").GetInt32() >= 1);
        Assert.True(body.GetProperty("pageSize").GetInt32() > 0);
        Assert.True(body.GetProperty("totalPages").GetInt32() >= 1);
    }

    [Fact]
    public async Task GetAuditEvents_FilterByAction_ReturnsOnlyMatching()
    {
        var (userId, token) = await CreateSiteAdminAsync();
        var uniqueAction = $"test.filter.{Guid.NewGuid():N}"[..30];
        await SeedAuditEventAsync(userId, uniqueAction, "widget", "42");
        await SeedAuditEventAsync(userId, "other.action", "widget", "99");

        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/admin/audit?action={uniqueAction}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var events = body.GetProperty("events");
        Assert.True(events.GetArrayLength() >= 1);
        foreach (var e in events.EnumerateArray())
            Assert.Equal(uniqueAction, e.GetProperty("action").GetString());
    }

    [Fact]
    public async Task GetAuditEvents_FilterByActorId_ReturnsOnlyMatching()
    {
        var (userId, token) = await CreateSiteAdminAsync();
        await SeedAuditEventAsync(userId, "actor.filter.test");

        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/admin/audit?actorId={userId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var events = body.GetProperty("events");
        Assert.True(events.GetArrayLength() >= 1);
        foreach (var e in events.EnumerateArray())
            Assert.Equal(userId.ToString(), e.GetProperty("actorUserId").GetString());
    }

    [Fact]
    public async Task GetAuditEvents_FilterByTargetType_ReturnsOnlyMatching()
    {
        var (userId, token) = await CreateSiteAdminAsync();
        var uniqueTarget = $"target-{Guid.NewGuid():N}"[..20];
        await SeedAuditEventAsync(userId, "target.type.test", uniqueTarget, "1");

        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/admin/audit?targetType={uniqueTarget}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var events = body.GetProperty("events");
        Assert.True(events.GetArrayLength() >= 1);
        foreach (var e in events.EnumerateArray())
            Assert.Equal(uniqueTarget, e.GetProperty("targetType").GetString());
    }

    [Fact]
    public async Task GetAuditEvents_Pagination_RespectsPageSize()
    {
        var (userId, token) = await CreateSiteAdminAsync();
        // Seed a few events
        for (var i = 0; i < 3; i++)
            await SeedAuditEventAsync(userId, "pagination.test");

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/audit?pageSize=1&page=1");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, body.GetProperty("events").GetArrayLength());
        Assert.Equal(1, body.GetProperty("pageSize").GetInt32());
    }

    [Fact]
    public async Task GetAuditEvents_PageSizeClamped_To200()
    {
        var (_, token) = await CreateSiteAdminAsync();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/audit?pageSize=500");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(200, body.GetProperty("pageSize").GetInt32());
    }

    [Fact]
    public async Task GetAuditEvents_EventShape_HasExpectedFields()
    {
        var (userId, token) = await CreateSiteAdminAsync();
        await SeedAuditEventAsync(userId, "shape.test", "tenant", "abc-123", "{\"key\":\"value\"}");

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/audit?action=shape.test");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(request);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var evt = body.GetProperty("events")[0];
        Assert.True(evt.TryGetProperty("id", out _));
        Assert.Equal(userId.ToString(), evt.GetProperty("actorUserId").GetString());
        Assert.Equal("user", evt.GetProperty("actorType").GetString());
        Assert.Equal("shape.test", evt.GetProperty("action").GetString());
        Assert.Equal("tenant", evt.GetProperty("targetType").GetString());
        Assert.Equal("abc-123", evt.GetProperty("targetId").GetString());
        Assert.Contains("key", evt.GetProperty("metadata").GetString());
        Assert.True(evt.TryGetProperty("createdAt", out _));
    }

    [Fact]
    public async Task GetAuditEvents_OrderedByCreatedAtDesc()
    {
        var (userId, token) = await CreateSiteAdminAsync();
        var uniqueAction = $"order.{Guid.NewGuid():N}"[..25];
        await SeedAuditEventAsync(userId, uniqueAction);
        await Task.Delay(50); // Ensure distinct timestamps
        await SeedAuditEventAsync(userId, uniqueAction);

        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/admin/audit?action={uniqueAction}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(request);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var events = body.GetProperty("events");
        if (events.GetArrayLength() >= 2)
        {
            var first = DateTime.Parse(events[0].GetProperty("createdAt").GetString()!);
            var second = DateTime.Parse(events[1].GetProperty("createdAt").GetString()!);
            Assert.True(first >= second, "Events should be ordered by createdAt DESC");
        }
    }

    // ── Audit Completeness: verify mutating admin actions record audit events ──

    [Fact]
    public async Task CreateTenant_RecordsAuditEvent()
    {
        var (_, token) = await CreateSiteAdminAsync();
        var slug = $"aud-{Guid.NewGuid():N}"[..20];

        var createReq = new HttpRequestMessage(HttpMethod.Post, "/api/admin/tenants")
        {
            Content = JsonContent.Create(new { slug, displayName = "Audit Test Tenant" })
        };
        createReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var createResp = await _client.SendAsync(createReq);
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);

        var auditReq = new HttpRequestMessage(HttpMethod.Get, "/api/admin/audit?action=tenant.created");
        auditReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var auditResp = await _client.SendAsync(auditReq);
        var body = await auditResp.Content.ReadFromJsonAsync<JsonElement>();

        var events = body.GetProperty("events");
        var found = false;
        foreach (var e in events.EnumerateArray())
        {
            if (e.GetProperty("metadata").GetString()?.Contains(slug) == true)
            {
                Assert.Equal("tenant", e.GetProperty("targetType").GetString());
                found = true;
                break;
            }
        }
        Assert.True(found, "tenant.created audit event should exist for the new tenant");
    }

    [Fact]
    public async Task UpdateTenant_RecordsAuditEvent()
    {
        var (_, token) = await CreateSiteAdminAsync();
        var slug = $"aup-{Guid.NewGuid():N}"[..20];

        var createReq = new HttpRequestMessage(HttpMethod.Post, "/api/admin/tenants")
        {
            Content = JsonContent.Create(new { slug, displayName = "Pre-Update" })
        };
        createReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var createResp = await _client.SendAsync(createReq);
        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var tenantId = created.GetProperty("id").GetString();

        var updateReq = new HttpRequestMessage(HttpMethod.Patch, $"/api/admin/tenants/{tenantId}")
        {
            Content = JsonContent.Create(new { displayName = "Post-Update" })
        };
        updateReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        await _client.SendAsync(updateReq);

        var auditReq = new HttpRequestMessage(HttpMethod.Get, $"/api/admin/audit?action=tenant.updated&targetId={tenantId}");
        auditReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var auditResp = await _client.SendAsync(auditReq);
        var body = await auditResp.Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(body.GetProperty("events").GetArrayLength() >= 1, "tenant.updated audit event should exist");
    }

    [Fact]
    public async Task UpdateTenant_Suspend_SetsSuspensionMetadata()
    {
        var (_, token) = await CreateSiteAdminAsync();
        var slug = $"sus-{Guid.NewGuid():N}"[..20];

        var createReq = new HttpRequestMessage(HttpMethod.Post, "/api/admin/tenants")
        {
            Content = JsonContent.Create(new { slug, displayName = "To Suspend" })
        };
        createReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var createResp = await _client.SendAsync(createReq);
        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var tenantId = Guid.Parse(created.GetProperty("id").GetString()!);

        var updateReq = new HttpRequestMessage(HttpMethod.Patch, $"/api/admin/tenants/{tenantId}")
        {
            Content = JsonContent.Create(new { status = "suspended" })
        };
        updateReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(updateReq);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT status, suspension_reason, suspended_at FROM tenants WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", tenantId);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("suspended", reader.GetString(0));
        Assert.Equal("manual_admin", reader.GetString(1));
        Assert.False(reader.IsDBNull(2), "suspended_at should be set");
    }

    [Fact]
    public async Task UpdateTenant_Reactivate_ClearsSuspensionMetadata()
    {
        var (_, token) = await CreateSiteAdminAsync();
        var slug = $"rea-{Guid.NewGuid():N}"[..20];

        var createReq = new HttpRequestMessage(HttpMethod.Post, "/api/admin/tenants")
        {
            Content = JsonContent.Create(new { slug, displayName = "To Reactivate" })
        };
        createReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var createResp = await _client.SendAsync(createReq);
        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var tenantId = Guid.Parse(created.GetProperty("id").GetString()!);

        // Suspend first
        var suspendReq = new HttpRequestMessage(HttpMethod.Patch, $"/api/admin/tenants/{tenantId}")
        {
            Content = JsonContent.Create(new { status = "suspended" })
        };
        suspendReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        await _client.SendAsync(suspendReq);

        // Reactivate
        var activateReq = new HttpRequestMessage(HttpMethod.Patch, $"/api/admin/tenants/{tenantId}")
        {
            Content = JsonContent.Create(new { status = "active" })
        };
        activateReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(activateReq);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT status, suspension_reason, suspended_at FROM tenants WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", tenantId);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("active", reader.GetString(0));
        Assert.True(reader.IsDBNull(1), "suspension_reason should be cleared");
        Assert.True(reader.IsDBNull(2), "suspended_at should be cleared");
    }

    [Fact]
    public async Task DeleteTenant_RecordsAuditEvent()
    {
        var (_, token) = await CreateSiteAdminAsync();
        var slug = $"aud-{Guid.NewGuid():N}"[..20];

        var createReq = new HttpRequestMessage(HttpMethod.Post, "/api/admin/tenants")
        {
            Content = JsonContent.Create(new { slug, displayName = "To Audit Delete" })
        };
        createReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var createResp = await _client.SendAsync(createReq);
        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var tenantId = created.GetProperty("id").GetString();

        var deleteReq = new HttpRequestMessage(HttpMethod.Delete, $"/api/admin/tenants/{tenantId}");
        deleteReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        await _client.SendAsync(deleteReq);

        var auditReq = new HttpRequestMessage(HttpMethod.Get, $"/api/admin/audit?action=tenant.deleted&targetId={tenantId}");
        auditReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var auditResp = await _client.SendAsync(auditReq);
        var body = await auditResp.Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(body.GetProperty("events").GetArrayLength() >= 1, "tenant.deleted audit event should exist");
    }

    // ── Membership Admin Endpoints ────────────────────────────────────
    // Routes: GET/POST /api/admin/tenants/{tenantId}/members
    //         PATCH/DELETE /api/admin/tenants/{tenantId}/members/{userId}

    private async Task<Guid> CreateBareTenantAsync()
    {
        var tenantId = Guid.NewGuid();
        var slug = $"mem-{tenantId:N}"[..20];
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(@"
            INSERT INTO tenants (id, slug, display_name, db_identifier, status, created_at, updated_at)
            VALUES (@id, @slug, 'Mem Tenant', @dbId, 'active', NOW(), NOW())", conn);
        cmd.Parameters.AddWithValue("id", tenantId);
        cmd.Parameters.AddWithValue("slug", slug);
        cmd.Parameters.AddWithValue("dbId", $"tenant_{slug}");
        await cmd.ExecuteNonQueryAsync();
        return tenantId;
    }

    private async Task<Guid> CreateBareUserAsync()
    {
        var userId = Guid.NewGuid();
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO users (id, email, display_name, status) VALUES (@id, @email, 'Mem User', 'active')", conn);
        cmd.Parameters.AddWithValue("id", userId);
        cmd.Parameters.AddWithValue("email", $"mem-{userId}@test.com");
        await cmd.ExecuteNonQueryAsync();
        return userId;
    }

    private async Task SeedMembershipAsync(Guid tenantId, Guid userId, string role = "viewer", string status = "active")
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(@"
            INSERT INTO tenant_memberships (id, user_id, tenant_id, role, status, created_at, updated_at)
            VALUES (@id, @userId, @tenantId, @role, @status, NOW(), NOW())", conn);
        cmd.Parameters.AddWithValue("id", Guid.NewGuid());
        cmd.Parameters.AddWithValue("userId", userId);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("role", role);
        cmd.Parameters.AddWithValue("status", status);
        await cmd.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task GetTenantMembers_NonSiteAdmin_Returns403()
    {
        var token = await CreateRegularUserTokenAsync();
        var req = new HttpRequestMessage(HttpMethod.Get, $"/api/admin/tenants/{Guid.NewGuid()}/members");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var resp = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task GetTenantMembers_UnknownTenant_Returns404()
    {
        var (_, token) = await CreateSiteAdminAsync();
        var req = new HttpRequestMessage(HttpMethod.Get, $"/api/admin/tenants/{Guid.NewGuid()}/members");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var resp = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task GetTenantMembers_Returns200AndFiltersByStatus()
    {
        var (_, token) = await CreateSiteAdminAsync();
        var tenantId = await CreateBareTenantAsync();
        var activeUser = await CreateBareUserAsync();
        var pendingUser = await CreateBareUserAsync();
        await SeedMembershipAsync(tenantId, activeUser, "editor", "active");
        await SeedMembershipAsync(tenantId, pendingUser, "viewer", "pending");

        var req = new HttpRequestMessage(HttpMethod.Get, $"/api/admin/tenants/{tenantId}/members?status=active");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var resp = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var members = body.GetProperty("members");
        Assert.Equal(1, members.GetArrayLength());
        Assert.Equal(activeUser.ToString(), members[0].GetProperty("userId").GetString());
    }

    [Fact]
    public async Task AddTenantMember_InvalidRole_Returns400()
    {
        var (_, token) = await CreateSiteAdminAsync();
        var tenantId = await CreateBareTenantAsync();
        var userId = await CreateBareUserAsync();

        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/tenants/{tenantId}/members")
        {
            Content = JsonContent.Create(new { userId, role = "superhero" })
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var resp = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task AddTenantMember_UnknownTenant_Returns404()
    {
        var (_, token) = await CreateSiteAdminAsync();
        var userId = await CreateBareUserAsync();

        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/tenants/{Guid.NewGuid()}/members")
        {
            Content = JsonContent.Create(new { userId, role = "viewer" })
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var resp = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task AddTenantMember_UnknownUser_Returns404()
    {
        var (_, token) = await CreateSiteAdminAsync();
        var tenantId = await CreateBareTenantAsync();

        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/tenants/{tenantId}/members")
        {
            Content = JsonContent.Create(new { userId = Guid.NewGuid(), role = "viewer" })
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var resp = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task AddTenantMember_NewMember_Returns201()
    {
        var (_, token) = await CreateSiteAdminAsync();
        var tenantId = await CreateBareTenantAsync();
        var userId = await CreateBareUserAsync();

        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/tenants/{tenantId}/members")
        {
            Content = JsonContent.Create(new { userId, role = "editor" })
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var resp = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("editor", body.GetProperty("role").GetString());
        Assert.Equal("active", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task AddTenantMember_ReactivatesInactiveMembership()
    {
        var (_, token) = await CreateSiteAdminAsync();
        var tenantId = await CreateBareTenantAsync();
        var userId = await CreateBareUserAsync();
        await SeedMembershipAsync(tenantId, userId, "viewer", "disabled");

        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/tenants/{tenantId}/members")
        {
            Content = JsonContent.Create(new { userId, role = "admin" })
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var resp = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT role, status FROM tenant_memberships WHERE user_id = @userId AND tenant_id = @tenantId", conn);
        cmd.Parameters.AddWithValue("userId", userId);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("admin", reader.GetString(0));
        Assert.Equal("active", reader.GetString(1));
    }

    [Fact]
    public async Task AddTenantMember_AlreadyActive_Returns409()
    {
        var (_, token) = await CreateSiteAdminAsync();
        var tenantId = await CreateBareTenantAsync();
        var userId = await CreateBareUserAsync();
        await SeedMembershipAsync(tenantId, userId, "viewer", "active");

        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/tenants/{tenantId}/members")
        {
            Content = JsonContent.Create(new { userId, role = "viewer" })
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var resp = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
    }

    [Fact]
    public async Task UpdateTenantMember_NoFields_Returns400()
    {
        var (_, token) = await CreateSiteAdminAsync();
        var tenantId = await CreateBareTenantAsync();
        var userId = await CreateBareUserAsync();
        await SeedMembershipAsync(tenantId, userId);

        var req = new HttpRequestMessage(HttpMethod.Patch, $"/api/admin/tenants/{tenantId}/members/{userId}")
        {
            Content = JsonContent.Create(new { })
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var resp = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task UpdateTenantMember_InvalidStatus_Returns400()
    {
        var (_, token) = await CreateSiteAdminAsync();
        var tenantId = await CreateBareTenantAsync();
        var userId = await CreateBareUserAsync();
        await SeedMembershipAsync(tenantId, userId);

        var req = new HttpRequestMessage(HttpMethod.Patch, $"/api/admin/tenants/{tenantId}/members/{userId}")
        {
            Content = JsonContent.Create(new { status = "banished" })
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var resp = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task UpdateTenantMember_Missing_Returns404()
    {
        var (_, token) = await CreateSiteAdminAsync();
        var tenantId = await CreateBareTenantAsync();

        var req = new HttpRequestMessage(HttpMethod.Patch, $"/api/admin/tenants/{tenantId}/members/{Guid.NewGuid()}")
        {
            Content = JsonContent.Create(new { role = "editor" })
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var resp = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task UpdateTenantMember_ChangesRoleAndStatus()
    {
        var (_, token) = await CreateSiteAdminAsync();
        var tenantId = await CreateBareTenantAsync();
        var userId = await CreateBareUserAsync();
        await SeedMembershipAsync(tenantId, userId, "viewer", "active");

        var req = new HttpRequestMessage(HttpMethod.Patch, $"/api/admin/tenants/{tenantId}/members/{userId}")
        {
            Content = JsonContent.Create(new { role = "editor", status = "disabled" })
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var resp = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("editor", body.GetProperty("role").GetString());
        Assert.Equal("disabled", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task RemoveTenantMember_Missing_Returns404()
    {
        var (_, token) = await CreateSiteAdminAsync();
        var tenantId = await CreateBareTenantAsync();

        var req = new HttpRequestMessage(HttpMethod.Delete, $"/api/admin/tenants/{tenantId}/members/{Guid.NewGuid()}");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var resp = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task RemoveTenantMember_SoftDeletesMembership()
    {
        var (_, token) = await CreateSiteAdminAsync();
        var tenantId = await CreateBareTenantAsync();
        var userId = await CreateBareUserAsync();
        await SeedMembershipAsync(tenantId, userId, "editor", "active");

        var req = new HttpRequestMessage(HttpMethod.Delete, $"/api/admin/tenants/{tenantId}/members/{userId}");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var resp = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT status FROM tenant_memberships WHERE user_id = @userId AND tenant_id = @tenantId", conn);
        cmd.Parameters.AddWithValue("userId", userId);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        Assert.Equal("disabled", (string?)await cmd.ExecuteScalarAsync());

        // Second delete hits the "already disabled" branch
        var req2 = new HttpRequestMessage(HttpMethod.Delete, $"/api/admin/tenants/{tenantId}/members/{userId}");
        req2.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var resp2 = await _client.SendAsync(req2);
        Assert.Equal(HttpStatusCode.NotFound, resp2.StatusCode);
    }
}
