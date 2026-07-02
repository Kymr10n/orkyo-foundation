using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;

namespace Orkyo.Foundation.Tests.Endpoints;

/// <summary>
/// Integration tests for GET /api/audit (tenant-admin, tenant-scoped, audit_log feature).
/// The site-admin /api/admin/audit is covered separately in <see cref="AuditEndpointsTests"/>.
/// </summary>
[Collection("Database collection")]
public class TenantAuditEndpointsTests
{
    private static readonly Guid TestTenantId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private readonly DatabaseFixture _fixture;
    private readonly string _connString;

    public TenantAuditEndpointsTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
        _connString = $"Host=localhost;Port={fixture.DatabasePort};Database=control_plane;Username=postgres;Password=postgres";
    }

    private async Task SeedEventAsync(Guid? tenantId, string action)
    {
        await using var conn = new NpgsqlConnection(_connString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            @"INSERT INTO audit_events (id, tenant_id, actor_type, action, created_at)
              VALUES (@id, @tenantId, 'system', @action, NOW())", conn);
        cmd.Parameters.AddWithValue("id", Guid.NewGuid());
        cmd.Parameters.AddWithValue("tenantId", tenantId.HasValue ? tenantId.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("action", action);
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
    public async Task Get_TenantAdmin_OnlyReturnsOwnTenantEvents_NoLeakage()
    {
        var marker = Guid.NewGuid().ToString("N");
        var mine = $"audit.mine.{marker}";
        var otherTenants = $"audit.other.{marker}";
        var platform = $"audit.platform.{marker}";

        await SeedEventAsync(TestTenantId, mine);
        await SeedEventAsync(Guid.NewGuid(), otherTenants); // different tenant
        await SeedEventAsync(null, platform);               // platform/site-admin event

        using var client = _fixture.CreateClientWithRole("admin");
        // Large page to be sure our seeded rows are in the result set.
        var response = await client.GetAsync("/api/audit/?pageSize=200");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var actions = await ActionsOf(response);
        Assert.Contains(mine, actions);
        Assert.DoesNotContain(otherTenants, actions); // no cross-tenant leakage
        Assert.DoesNotContain(platform, actions);     // untagged platform events excluded
    }

    [Fact]
    public async Task Get_TenantAdmin_OmitsSensitiveFields()
    {
        var action = $"audit.fields.{Guid.NewGuid():N}";
        await SeedEventAsync(TestTenantId, action);

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
