using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;

namespace Orkyo.Foundation.Tests.Endpoints;

/// <summary>
/// Integration tests for GET /api/audit (tenant-admin, audit_log feature). The endpoint reads
/// the current tenant's OWN database — <c>audit_events</c> there IS the tenant trail, isolated
/// physically (no tenant_id filter). The site-admin /api/admin/audit (control-plane, all
/// tenants) is covered separately in <see cref="AuditEndpointsTests"/>.
/// </summary>
[Collection("Database collection")]
public class TenantAuditEndpointsTests
{
    private readonly DatabaseFixture _fixture;
    private readonly string _tenantConnString;

    public TenantAuditEndpointsTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
        _tenantConnString = $"Host=localhost;Port={fixture.DatabasePort};Database={TestConstants.TenantDatabase};Username=postgres;Password=postgres";
    }

    // Seeds directly into the tenant database (the tenant audit_events has no tenant_id column).
    private async Task SeedEventAsync(string action, string actorType = "system", string? metadata = null)
    {
        await using var conn = new NpgsqlConnection(_tenantConnString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            @"INSERT INTO audit_events (actor_type, action, metadata, created_at)
              VALUES (@actorType, @action, @metadata::jsonb, NOW())", conn);
        cmd.Parameters.AddWithValue("actorType", actorType);
        cmd.Parameters.AddWithValue("action", action);
        cmd.Parameters.AddWithValue("metadata", (object?)metadata ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<List<string>> ActionsOf(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("events").EnumerateArray()
            .Select(e => e.GetProperty("action").GetString()!).ToList();
    }

    [Fact]
    public async Task Get_Unauthenticated_Returns401()
    {
        using var client = _fixture.Factory.CreateClient();
        var response = await client.GetAsync("/api/audit/");
        Assert.True(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task Get_NonAdminMember_Returns403()
    {
        using var client = _fixture.CreateClientWithRole("viewer");
        var response = await client.GetAsync("/api/audit/");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_TenantAdmin_ReturnsTenantDbEvents()
    {
        var marker = Guid.NewGuid().ToString("N");
        var member = $"audit.member.{marker}";
        var platform = $"audit.platform.{marker}";

        await SeedEventAsync(member, actorType: "user");
        await SeedEventAsync(platform, metadata: "{\"source\":\"platform\"}");

        using var client = _fixture.CreateClientWithRole("admin");
        // Large page to be sure our seeded rows are in the result set.
        var response = await client.GetAsync("/api/audit/?pageSize=200");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Both member and platform-sourced events live in the tenant DB and surface here.
        var actions = await ActionsOf(response);
        Assert.Contains(member, actions);
        Assert.Contains(platform, actions);
    }

    [Fact]
    public async Task Get_TenantAdmin_OmitsSensitiveFields()
    {
        var action = $"audit.fields.{Guid.NewGuid():N}";
        await SeedEventAsync(action);

        using var client = _fixture.CreateClientWithRole("admin");
        var response = await client.GetAsync($"/api/audit/?action={action}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var events = body.GetProperty("events");
        Assert.Equal(1, body.GetProperty("totalCount").GetInt32());
        var ev = events.EnumerateArray().Single();
        Assert.False(ev.TryGetProperty("ipAddress", out _), "ip_address must not be exposed to tenant admins");
        Assert.False(ev.TryGetProperty("requestId", out _), "request_id must not be exposed to tenant admins");
        Assert.True(ev.TryGetProperty("action", out _));
        Assert.True(ev.TryGetProperty("createdAt", out _));
    }

    [Fact]
    public async Task Get_TenantAdmin_PageSizeCappedAt200()
    {
        using var client = _fixture.CreateClientWithRole("admin");
        var response = await client.GetAsync("/api/audit/?pageSize=999");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(200, body.GetProperty("pageSize").GetInt32());
    }
}
