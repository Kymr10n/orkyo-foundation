using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Api.Services.Reporting;
using Npgsql;
using NpgsqlTypes;
using Xunit;

namespace Orkyo.Foundation.Tests.Endpoints;

/// <summary>
/// Tests for reporting token CRUD, auth security, parameter validation, tenant isolation,
/// CSV export coverage, audit logging, and role enforcement.
/// </summary>
[Collection("Database collection")]
public class ReportingEndpointsTests
{
    private readonly DatabaseFixture _fixture;
    private readonly HttpClient _adminClient;
    private readonly string _cpConnStr;

    private static readonly string TestPepper = "test-reporting-pepper-do-not-use-in-prod";

    public ReportingEndpointsTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
        _adminClient = fixture.CreateAuthorizedClient();
        _cpConnStr = $"Host=localhost;Port={fixture.DatabasePort};Database=control_plane;Username=postgres;Password=postgres";
    }

    // ── Token CRUD ────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateToken_AsAdmin_Returns201WithRawToken()
    {
        var response = await _adminClient.PostAsJsonAsync(
            "/api/reporting/v1/tokens",
            new { name = "Test BI Token" });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<CreatedReportingToken>();
        created.Should().NotBeNull();
        created!.Summary.Name.Should().Be("Test BI Token");
        created.Summary.IsActive.Should().BeTrue();
        created.RawToken.Should().StartWith("orkyo_rpt_");
        created.Summary.TokenPrefix.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task CreateToken_EmptyName_Returns400()
    {
        var response = await _adminClient.PostAsJsonAsync(
            "/api/reporting/v1/tokens",
            new { name = "" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // NOTE: the Free-tier 402 path (CreateToken tier gate) is not integration-tested here.
    // FoundationWebApplicationFactory pins a fixed TenantContext at ServiceTier.Enterprise in DI,
    // so every request in this harness resolves as Enterprise and the gate never fires. The gate
    // is exercised in real deployments where TenantContext is resolved from the tenant's DB tier.

    [Fact]
    public async Task ListTokens_AsAdmin_ReturnsList()
    {
        await _adminClient.PostAsJsonAsync("/api/reporting/v1/tokens", new { name = "List-Test-Token" });

        var response = await _adminClient.GetAsync("/api/reporting/v1/tokens");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var tokens = await response.Content.ReadFromJsonAsync<List<ReportingTokenSummary>>();
        tokens.Should().NotBeNull();
        tokens.Should().Contain(t => t.Name == "List-Test-Token");
    }

    [Fact]
    public async Task RevokeToken_AsAdmin_Returns204AndMakesTokenInactive()
    {
        var created = await CreateTokenAsync("Revoke-Me");
        var revokeResponse = await _adminClient.DeleteAsync($"/api/reporting/v1/tokens/{created.Summary.Id}");
        revokeResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var listResponse = await _adminClient.GetAsync("/api/reporting/v1/tokens");
        var tokens = await listResponse.Content.ReadFromJsonAsync<List<ReportingTokenSummary>>();
        var revoked = tokens!.FirstOrDefault(t => t.Id == created.Summary.Id);
        revoked.Should().NotBeNull();
        revoked!.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task RevokeToken_UnknownId_Returns404()
    {
        var response = await _adminClient.DeleteAsync($"/api/reporting/v1/tokens/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CreateToken_SecretNotReturnedOnList()
    {
        var created = await CreateTokenAsync("No-Secret-In-List");
        var listResponse = await _adminClient.GetAsync("/api/reporting/v1/tokens");
        var body = await listResponse.Content.ReadAsStringAsync();

        body.Should().NotContain(created.RawToken,
            because: "the raw token secret must never appear in list responses");
    }

    // ── Auth security ─────────────────────────────────────────────────────────

    [Fact]
    public async Task AllReportingEndpoints_WithValidToken_Return200()
    {
        var created = await CreateTokenAsync("All-Endpoints-Test");
        using var client = MakeReportingClient(created.RawToken);

        string[] endpoints =
        [
            "/api/reporting/v1/allocations",
            "/api/reporting/v1/spaces/utilization",
            "/api/reporting/v1/resources/utilization",
            "/api/reporting/v1/requests/throughput",
            "/api/reporting/v1/conflicts",
        ];

        foreach (var path in endpoints)
        {
            var response = await client.GetAsync(path);
            response.StatusCode.Should().Be(HttpStatusCode.OK,
                because: $"{path} should return 200 with a valid token");
        }
    }

    [Fact]
    public async Task ReportingEndpoint_WithNoAuth_Returns401()
    {
        var anonClient = _fixture.Factory.CreateClient();
        var response = await anonClient.GetAsync("/api/reporting/v1/allocations");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ReportingEndpoint_WithMalformedToken_Returns401()
    {
        using var client = MakeReportingClient("orkyo_rpt_notvalid");
        var response = await client.GetAsync("/api/reporting/v1/allocations");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ReportingEndpoint_WithRevokedToken_Returns401()
    {
        var created = await CreateTokenAsync("Revoke-Test-Token");
        await _adminClient.DeleteAsync($"/api/reporting/v1/tokens/{created.Summary.Id}");

        using var client = MakeReportingClient(created.RawToken);
        var response = await client.GetAsync("/api/reporting/v1/allocations");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ReportingEndpoint_WithJwtBearerToken_Returns401()
    {
        // Standard Keycloak-style JWT tokens must NOT be accepted by reporting endpoints
        using var client = MakeReportingClient(TestConstants.TestBearerToken);
        var response = await client.GetAsync("/api/reporting/v1/allocations");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Parameter abuse ───────────────────────────────────────────────────────

    [Fact]
    public async Task ReportingEndpoint_WithTenantIdParam_Returns400()
    {
        var created = await CreateTokenAsync("Param-Abuse-TenantId");
        using var client = MakeReportingClient(created.RawToken);

        var response = await client.GetAsync(
            "/api/reporting/v1/allocations?tenantId=00000000-0000-0000-0000-999999999999");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ReportingEndpoint_WithTenantSlugParam_Returns400()
    {
        var created = await CreateTokenAsync("Param-Abuse-TenantSlug");
        using var client = MakeReportingClient(created.RawToken);

        var response = await client.GetAsync("/api/reporting/v1/allocations?tenantSlug=other-tenant");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ReportingEndpoint_FromAfterTo_Returns400()
    {
        var created = await CreateTokenAsync("Date-Range-Bad");
        using var client = MakeReportingClient(created.RawToken);

        var response = await client.GetAsync(
            "/api/reporting/v1/allocations?from=2025-12-01&to=2025-01-01");
        ((int)response.StatusCode).Should().BeOneOf(400, 422);
    }

    [Fact]
    public async Task ReportingEndpoint_ExcessivePageSize_Returns400()
    {
        var created = await CreateTokenAsync("PageSize-Abuse");
        using var client = MakeReportingClient(created.RawToken);

        var response = await client.GetAsync("/api/reporting/v1/allocations?pageSize=99999");
        ((int)response.StatusCode).Should().BeOneOf(400, 422);
    }

    // ── CSV content negotiation ───────────────────────────────────────────────

    [Fact]
    public async Task ReportingEndpoint_WithCsvFormat_ReturnsCsv()
    {
        var created = await CreateTokenAsync("Csv-Format-Test");
        using var client = MakeReportingClient(created.RawToken);

        var response = await client.GetAsync("/api/reporting/v1/allocations?format=csv");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/csv");
    }

    [Fact]
    public async Task AllReportingEndpoints_CsvFormat_ReturnsCsvOnEachEndpoint()
    {
        var created = await CreateTokenAsync("Csv-All-Endpoints");
        using var client = MakeReportingClient(created.RawToken);

        string[] endpoints =
        [
            "/api/reporting/v1/allocations",
            "/api/reporting/v1/spaces/utilization",
            "/api/reporting/v1/resources/utilization",
            "/api/reporting/v1/requests/throughput",
            "/api/reporting/v1/conflicts",
        ];

        foreach (var path in endpoints)
        {
            var response = await client.GetAsync($"{path}?format=csv");
            response.StatusCode.Should().Be(HttpStatusCode.OK,
                because: $"{path}?format=csv should return 200");
            response.Content.Headers.ContentType?.MediaType.Should().Be("text/csv",
                because: $"{path}?format=csv should return text/csv content type");
        }
    }

    // ── Tenant isolation ──────────────────────────────────────────────────────

    [Fact]
    public async Task ReportingEndpoint_WithTokenFromDifferentTenant_Returns403()
    {
        // Create a valid token that belongs to a different tenant ID.
        // The endpoint filter verifies record.TenantId == currentTenant.TenantId and
        // returns 403 when they don't match.
        var foreignTenantId = await SeedForeignTenantAsync();
        var rawToken = await InsertRawTokenForTenantAsync(foreignTenantId, "foreign-tenant-token");
        using var client = MakeReportingClient(rawToken);

        var response = await client.GetAsync("/api/reporting/v1/allocations");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            because: "a token issued for a different tenant must be rejected with 403");
    }

    // ── Token expiry ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ReportingEndpoint_WithExpiredToken_Returns401()
    {
        var testTenantId = new Guid("00000000-0000-0000-0000-000000000001");
        var rawToken = await InsertRawTokenForTenantAsync(
            testTenantId, "expired-token", expiresAt: DateTime.UtcNow.AddHours(-1));
        using var client = MakeReportingClient(rawToken);

        var response = await client.GetAsync("/api/reporting/v1/allocations");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            because: "an expired token must be rejected with 401");
    }

    // ── Role enforcement on token CRUD ────────────────────────────────────────

    [Fact]
    public async Task CreateToken_AsViewer_Returns403()
    {
        using var viewerClient = MakeViewerClient();
        var response = await viewerClient.PostAsJsonAsync(
            "/api/reporting/v1/tokens", new { name = "Should-Fail" });
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ListTokens_AsViewer_Returns403()
    {
        using var viewerClient = MakeViewerClient();
        var response = await viewerClient.GetAsync("/api/reporting/v1/tokens");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task RevokeToken_AsViewer_Returns403()
    {
        var created = await CreateTokenAsync("Viewer-Revoke-Target");
        using var viewerClient = MakeViewerClient();
        var response = await viewerClient.DeleteAsync($"/api/reporting/v1/tokens/{created.Summary.Id}");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── Audit logging ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ReportingEndpoint_OnSuccess_WritesAuditEvent()
    {
        var created = await CreateTokenAsync("Audit-Test-Token");
        using var client = MakeReportingClient(created.RawToken);

        var before = DateTime.UtcNow.AddSeconds(-1);
        await client.GetAsync("/api/reporting/v1/allocations");

        // Audit write is fire-and-forget — wait briefly for it to land
        await Task.Delay(200);

        await using var conn = new NpgsqlConnection(_cpConnStr);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(@"
            SELECT COUNT(*) FROM audit_events
            WHERE action = 'reporting.read'
              AND created_at >= @before
              AND metadata::text LIKE @tokenPrefix", conn);
        cmd.Parameters.AddWithValue("before", before);
        cmd.Parameters.AddWithValue("tokenPrefix", $"%{created.Summary.TokenPrefix}%");

        var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        count.Should().BeGreaterThan(0, because: "a successful reporting request must write an audit event");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<CreatedReportingToken> CreateTokenAsync(string name)
    {
        var response = await _adminClient.PostAsJsonAsync(
            "/api/reporting/v1/tokens", new { name });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CreatedReportingToken>())!;
    }

    private HttpClient MakeReportingClient(string rawToken)
    {
        var client = _fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Add(HeaderConstants.TenantSlug, TestConstants.TenantSlug);
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {rawToken}");
        return client;
    }


    /// <summary>
    /// Creates a viewer (non-admin) HTTP client using the test auth scheme.
    /// </summary>
    private HttpClient MakeViewerClient()
    {
        var tokenData = new
        {
            UserId = "11111111-1111-1111-1111-111111111111",
            Email = "test@orkyo.example",
            DisplayName = "Test User",
            TenantId = "00000000-0000-0000-0000-000000000001",
            TenantSlug = TestConstants.TenantSlug,
            IsTenantAdmin = false,
            Role = "viewer",
        };
        var bearerToken = Convert.ToBase64String(
            Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(tokenData)));

        var client = _fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Add(HeaderConstants.TenantSlug, TestConstants.TenantSlug);
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {bearerToken}");
        return client;
    }

    /// <summary>
    /// Seeds a minimal tenant row in the control plane for cross-tenant isolation tests.
    /// Returns the new tenant's ID.
    /// </summary>
    private async Task<Guid> SeedForeignTenantAsync()
    {
        var id = Guid.NewGuid();
        var slug = $"foreign-{id.ToString()[..8]}";
        await using var conn = new NpgsqlConnection(_cpConnStr);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(@"
            INSERT INTO tenants (id, slug, display_name, status, db_identifier, tier, created_at, updated_at)
            VALUES (@id, @slug, 'Foreign Tenant', 'active', @db, 2, NOW(), NOW())", conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("slug", slug);
        cmd.Parameters.AddWithValue("db", $"tenant_{slug}");
        await cmd.ExecuteNonQueryAsync();
        return id;
    }

    /// <summary>
    /// Inserts a fully valid reporting token directly into the control plane DB,
    /// bypassing the HTTP endpoint. Used to manufacture cross-tenant or expired tokens
    /// that the normal token-creation endpoint wouldn't allow.
    /// </summary>
    private async Task<string> InsertRawTokenForTenantAsync(
        Guid tenantId, string name, DateTime? expiresAt = null)
    {
        const string scheme = "orkyo_rpt";
        const int prefixLen = 8;
        const int secretBytes = 32;

        // Generate prefix from lowercase alphanumeric pool (mirrors ReportingTokenService)
        const string chars = "abcdefghijklmnopqrstuvwxyz0123456789";
        var prefixBuf = RandomNumberGenerator.GetBytes(prefixLen);
        var prefix = new string(prefixBuf.Select(b => chars[b % chars.Length]).ToArray());

        var secret = RandomNumberGenerator.GetBytes(secretBytes);
        var secretB64 = Convert.ToBase64String(secret).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var rawToken = $"{scheme}_{prefix}_{secretB64}";

        // HMAC-SHA256 with the test pepper (must match FoundationWebApplicationFactory config)
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(TestPepper));
        var hash = Convert.ToHexString(hmac.ComputeHash(secret)).ToLowerInvariant();

        await using var conn = new NpgsqlConnection(_cpConnStr);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(@"
            INSERT INTO reporting_api_tokens
                (tenant_id, name, token_prefix, token_hash, scopes, expires_at)
            VALUES (@tenantId, @name, @prefix, @hash, 'reporting:read', @expires)", conn);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("name", name);
        cmd.Parameters.AddWithValue("prefix", prefix);
        cmd.Parameters.AddWithValue("hash", hash);
        cmd.Parameters.Add(new NpgsqlParameter("expires", NpgsqlDbType.TimestampTz)
        {
            Value = expiresAt.HasValue ? (object)expiresAt.Value : DBNull.Value,
        });
        await cmd.ExecuteNonQueryAsync();

        return rawToken;
    }
}
