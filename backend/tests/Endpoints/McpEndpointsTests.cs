using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Api.Security;
using Api.Services.PlatformApi;
using Npgsql;
using Xunit;

namespace Orkyo.Foundation.Tests.Endpoints;

/// <summary>
/// The MCP server's authorization boundary, exercised over real HTTP.
///
/// MCP carries every call — reads and writes alike — over one POST, so the verb-aware write gate
/// that protects the HTTP endpoints cannot tell them apart here. The scope check therefore lives
/// per tool. These tests are what prove that substitution actually holds: a read-only token must
/// still read, and must still be unable to write.
/// </summary>
[Collection("Database collection")]
public class McpEndpointsTests
{
    private const string McpUrl = "/api/mcp";
    private const string TokensUrl = "/api/platform/v1/tokens";

    private readonly DatabaseFixture _fixture;
    private readonly HttpClient _adminClient;

    public McpEndpointsTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
        _adminClient = fixture.CreateAuthorizedClient();
    }

    /// <summary>Mints a real token through the API, so the tests use the same path a customer does.</summary>
    private async Task<string> IssueTokenAsync(params string[] scopes)
    {
        var response = await _adminClient.PostAsJsonAsync(TokensUrl, new
        {
            name = $"test-{Guid.NewGuid():N}",
            scopes,
            expiresAt = (DateTime?)null,
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<CreatedApiAccessToken>();
        return created!.RawToken;
    }

    private HttpClient ClientWithToken(string rawToken)
    {
        var client = _fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", rawToken);
        client.DefaultRequestHeaders.Host = $"{TestConstants.TenantSlug}.orkyo.com";
        return client;
    }

    /// <summary>Posts a JSON-RPC message the way an MCP client does.</summary>
    private static Task<HttpResponseMessage> RpcAsync(HttpClient client, object payload)
    {
        var content = new StringContent(
            JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var request = new HttpRequestMessage(HttpMethod.Post, McpUrl) { Content = content };
        // Streamable HTTP lets the server answer with either JSON or an SSE stream.
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        return client.SendAsync(request);
    }

    private static object CallTool(string name, object? arguments = null) => new
    {
        jsonrpc = "2.0",
        id = 1,
        method = "tools/call",
        @params = new { name, arguments = arguments ?? new { } },
    };

    private static object ListTools() => new
    {
        jsonrpc = "2.0",
        id = 1,
        method = "tools/list",
        @params = new { },
    };

    /// <summary>Must match FoundationWebApplicationFactory's API_ACCESS_TOKEN_PEPPER.</summary>
    private const string TestPepper = "test-api-access-pepper-do-not-use-in-prod";

    private string CpConnStr =>
        $"Host=localhost;Port={_fixture.DatabasePort};Database=control_plane;Username=postgres;Password=postgres";

    /// <summary>
    /// Mints a valid token that belongs to a DIFFERENT tenant, via direct SQL — the API cannot
    /// create one, which is rather the point. Mirrors ReportingEndpointsTests' foreign-tenant
    /// pattern, including the second tenant row the FK requires.
    /// </summary>
    private async Task<string> MintForeignTenantTokenAsync()
    {
        var foreignTenantId = Guid.NewGuid();
        var slug = $"foreign-{foreignTenantId:N}"[..20];

        const string chars = "abcdefghijklmnopqrstuvwxyz0123456789";
        var prefix = new string(RandomNumberGenerator.GetBytes(8).Select(b => chars[b % chars.Length]).ToArray());
        var secret = RandomNumberGenerator.GetBytes(32);
        var secretB64 = Convert.ToBase64String(secret).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var rawToken = $"orkyo_api_{prefix}_{secretB64}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(TestPepper));
        var hash = Convert.ToHexString(hmac.ComputeHash(secret)).ToLowerInvariant();

        await using var conn = new NpgsqlConnection(CpConnStr);
        await conn.OpenAsync();
        await using (var tenant = new NpgsqlCommand(@"
            INSERT INTO tenants (id, slug, display_name, status, db_identifier, tier, created_at, updated_at)
            VALUES (@id, @slug, 'Foreign Tenant', 'active', @db, 2, NOW(), NOW())", conn))
        {
            tenant.Parameters.AddWithValue("id", foreignTenantId);
            tenant.Parameters.AddWithValue("slug", slug);
            tenant.Parameters.AddWithValue("db", $"tenant_{slug}");
            await tenant.ExecuteNonQueryAsync();
        }
        await using (var token = new NpgsqlCommand(@"
            INSERT INTO api_access_tokens (tenant_id, name, token_prefix, token_hash, scopes)
            VALUES (@tenantId, 'foreign', @prefix, @hash, 'schedule:read schedule:write')", conn))
        {
            token.Parameters.AddWithValue("tenantId", foreignTenantId);
            token.Parameters.AddWithValue("prefix", prefix);
            token.Parameters.AddWithValue("hash", hash);
            await token.ExecuteNonQueryAsync();
        }
        return rawToken;
    }

    // ── Authentication ───────────────────────────────────────────────────────

    [Fact]
    public async Task WithoutAToken_TheServerIsNotReachable()
    {
        var anonymous = _fixture.Factory.CreateClient();
        anonymous.DefaultRequestHeaders.Host = $"{TestConstants.TenantSlug}.orkyo.com";

        var response = await RpcAsync(anonymous, ListTools());

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task WithAGarbageToken_TheServerIsNotReachable()
    {
        var response = await RpcAsync(ClientWithToken("orkyo_api_deadbeef_notarealsecret"), ListTools());

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AReportingTokenIsNotAcceptedByTheMcpServer()
    {
        // The two credential classes are deliberately separate: a read-only reporting token must
        // not become a schedule-writing one by being pointed at a different URL.
        var reporting = await _adminClient.PostAsJsonAsync("/api/reporting/v1/tokens", new { name = "bi" });
        var raw = JsonDocument.Parse(await reporting.Content.ReadAsStringAsync())
            .RootElement.GetProperty("rawToken").GetString()!;

        var response = await RpcAsync(ClientWithToken(raw), ListTools());

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AnotherTenantsToken_IsRefusedOnThisTenantsHost()
    {
        // The boundary the whole feature rests on. The token is VALID — right pepper, right
        // hash, active — but belongs to another tenant, so presenting it against this tenant's
        // host must be refused before any tool runs. Covers the wiring the unit tests cannot:
        // the middleware's no-role arm and the endpoint group's tenant-match filter together.
        var foreign = await MintForeignTenantTokenAsync();

        var response = await RpcAsync(ClientWithToken(foreign), ListTools());

        ((int)response.StatusCode).Should().BeOneOf(401, 403);
        var call = await RpcAsync(ClientWithToken(foreign),
            CallTool("list_requests", new { limit = 1 }));
        ((int)call.StatusCode).Should().BeOneOf(401, 403);
    }

    [Fact]
    public async Task ARevokedTokenStopsWorking()
    {
        var created = await (await _adminClient.PostAsJsonAsync(TokensUrl, new
        {
            name = "to be revoked",
            scopes = new[] { PlatformApiScopes.ScheduleRead },
            expiresAt = (DateTime?)null,
        })).Content.ReadFromJsonAsync<CreatedApiAccessToken>();
        var client = ClientWithToken(created!.RawToken);

        (await RpcAsync(client, ListTools())).StatusCode.Should().Be(HttpStatusCode.OK);
        await _adminClient.DeleteAsync($"{TokensUrl}/{created.Summary.Id}");

        // Stateless transport is what makes this immediate: there is no session to outlive the
        // credential that opened it.
        (await RpcAsync(client, ListTools())).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UsingATokenStampsLastUsed_ThroughItsOwnScope()
    {
        // The stamp is written by a fire-and-forget task AFTER the response, from a fresh DI
        // scope. Resolving it from the request scope instead would race scope disposal and
        // silently stop updating — and last_used_at is the field an admin reads to spot a
        // stale or stolen token, so "silently stops" is the worst possible failure mode.
        var created = await (await _adminClient.PostAsJsonAsync(TokensUrl, new
        {
            name = "usage-stamped",
            scopes = new[] { PlatformApiScopes.ScheduleRead },
            expiresAt = (DateTime?)null,
        })).Content.ReadFromJsonAsync<CreatedApiAccessToken>();
        created!.Summary.LastUsedAtUtc.Should().BeNull();

        (await RpcAsync(ClientWithToken(created.RawToken), ListTools()))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // Background write: poll briefly rather than assuming it landed with the response.
        DateTime? lastUsed = null;
        for (var i = 0; i < 40 && lastUsed is null; i++)
        {
            await Task.Delay(50);
            var list = await _adminClient.GetFromJsonAsync<List<ApiAccessTokenSummary>>(TokensUrl);
            lastUsed = list!.Single(t => t.Id == created.Summary.Id).LastUsedAtUtc;
        }

        lastUsed.Should().NotBeNull("the MCP call must stamp last_used_at via the background scope");
    }

    // ── Tool discovery ───────────────────────────────────────────────────────

    [Fact]
    public async Task AReadOnlyTokenCanListTools()
    {
        // The regression this guards: gating the group on the HTTP verb would make every MCP call
        // a "write", and a read-only token could not even discover the tools.
        var response = await RpcAsync(
            ClientWithToken(await IssueTokenAsync(PlatformApiScopes.ScheduleRead)), ListTools());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        foreach (var tool in ToolNames.All)
            body.Should().Contain(tool);
    }

    // ── Scope enforcement ────────────────────────────────────────────────────

    [Fact]
    public async Task AReadOnlyTokenCanRunAReadTool()
    {
        var response = await RpcAsync(
            ClientWithToken(await IssueTokenAsync(PlatformApiScopes.ScheduleRead)),
            CallTool("list_requests", new { limit = 5 }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().NotContain("schedule:write");
    }

    [Fact]
    public async Task AReadOnlyTokenIsRefusedByAWriteTool()
    {
        var response = await RpcAsync(
            ClientWithToken(await IssueTokenAsync(PlatformApiScopes.ScheduleRead)),
            CallTool("reschedule_request", new
            {
                requestId = Guid.NewGuid(),
                startTs = DateTime.UtcNow,
                endTs = DateTime.UtcNow.AddHours(1),
            }));

        // The refusal is a protocol-level error the agent can read, not a transport failure.
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain(PlatformApiScopes.ScheduleWrite);
    }

    [Fact]
    public async Task AReadOnlyTokenIsRefusedByTheAssignmentTool()
    {
        var response = await RpcAsync(
            ClientWithToken(await IssueTokenAsync(PlatformApiScopes.ScheduleRead)),
            CallTool("assign_resource", new
            {
                requestId = Guid.NewGuid(),
                resourceId = Guid.NewGuid(),
                startUtc = DateTime.UtcNow,
                endUtc = DateTime.UtcNow.AddHours(1),
            }));

        (await response.Content.ReadAsStringAsync()).Should().Contain(PlatformApiScopes.ScheduleWrite);
    }

    [Fact]
    public async Task AWriteScopedTokenReachesTheWriteToolAndIsAnsweredOnItsMerits()
    {
        // A write-scoped token gets past authorization; the request id below does not exist, so
        // the answer is "no such request" rather than "not allowed". That distinction is the test:
        // authorization passed, and the tool ran.
        var response = await RpcAsync(
            ClientWithToken(await IssueTokenAsync(PlatformApiScopes.ScheduleWrite)),
            CallTool("reschedule_request", new
            {
                requestId = Guid.NewGuid(),
                resourceId = Guid.NewGuid(),
                startTs = DateTime.UtcNow,
                endTs = DateTime.UtcNow.AddHours(1),
            }));

        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain(PlatformApiScopes.ScheduleWrite);
        body.Should().Contain("No request found");
    }

    // ── Tool metadata reaches the wire ───────────────────────────────────────

    /// <summary>Strips the Streamable HTTP SSE framing so the JSON-RPC payload can be parsed.</summary>
    private static string SsePayload(string body) =>
        string.Concat(body.Split('\n')
            .Where(l => l.StartsWith("data: ", StringComparison.Ordinal))
            .Select(l => l["data: ".Length..]));

    [Fact]
    public async Task ToolsList_MarksReadsReadOnlyAndWritesDestructive()
    {
        // Under a stateless transport the server cannot elicit a confirmation, so these hints are
        // the only signal a client has for deciding when to interpose one. v1 shipped with the
        // attribute defaults, which advertised even list_requests as destructive.
        var response = await RpcAsync(
            ClientWithToken(await IssueTokenAsync(PlatformApiScopes.ScheduleRead)), ListTools());

        using var doc = JsonDocument.Parse(SsePayload(await response.Content.ReadAsStringAsync()));
        var tools = doc.RootElement.GetProperty("result").GetProperty("tools").EnumerateArray()
            .ToDictionary(t => t.GetProperty("name").GetString()!, t => t.Clone());

        tools["list_requests"].GetProperty("annotations").GetProperty("readOnlyHint")
            .GetBoolean().Should().BeTrue();
        tools["list_sites"].GetProperty("annotations").GetProperty("readOnlyHint")
            .GetBoolean().Should().BeTrue();
        tools["reschedule_request"].GetProperty("annotations").GetProperty("destructiveHint")
            .GetBoolean().Should().BeTrue();
        // Additive, not destructive: it books a resource without overwriting an existing placement.
        tools["assign_resource"].GetProperty("annotations").GetProperty("destructiveHint")
            .GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task AToolResultCarriesStructuredContent()
    {
        // Typed records exist so clients get a schema to validate against; without this the
        // UseStructuredContent flag could be dropped and nothing would notice.
        var response = await RpcAsync(
            ClientWithToken(await IssueTokenAsync(PlatformApiScopes.ScheduleRead)),
            CallTool("list_requests", new { limit = 1 }));

        (await response.Content.ReadAsStringAsync()).Should().Contain("structuredContent");
    }

    /// <summary>
    /// The whole tool surface, and the write half of it. The write list is what the refusal theory
    /// iterates: because MCP has no verb-aware gate, an ungated write tool would be invisible
    /// unless every one of them is named here.
    /// </summary>
    private static class ToolNames
    {
        // Declared before All: static initialisers run in order, so spreading Writes into All
        // above its own declaration would spread a null.
        public static readonly string[] Writes =
        [
            "reschedule_request", "assign_resource", "auto_schedule_apply",
            "create_request", "link_requests", "unlink_requests",
            "block_resource_time", "unblock_resource_time",
        ];

        public static readonly string[] All =
        [
            "list_sites", "list_requests", "list_resources", "list_conflicts",
            "get_critical_path", "list_dependencies", "get_request_plan", "analyze_capacity",
            "auto_schedule_preview", "list_resource_absences",
            .. Writes,
        ];
    }

    public static TheoryData<string> WriteTools()
    {
        var data = new TheoryData<string>();
        foreach (var tool in ToolNames.Writes) data.Add(tool);
        return data;
    }

    /// <summary>Arguments good enough to reach the scope check; none of these calls should get past it.</summary>
    private static object ArgumentsFor(string tool) => tool switch
    {
        "reschedule_request" => new { requestId = Guid.NewGuid(), startTs = DateTime.UtcNow, endTs = DateTime.UtcNow.AddHours(1) },
        "assign_resource" => new { requestId = Guid.NewGuid(), resourceId = Guid.NewGuid(), startUtc = DateTime.UtcNow, endUtc = DateTime.UtcNow.AddHours(1) },
        "auto_schedule_apply" => new { siteId = Guid.NewGuid(), horizonStart = "2026-06-01", horizonEnd = "2026-06-30", previewFingerprint = "deadbeef" },
        "create_request" => new { name = "Sneaky", durationValue = 1, durationUnit = "hours" },
        "link_requests" => new { predecessorRequestId = Guid.NewGuid(), successorRequestId = Guid.NewGuid() },
        "unlink_requests" => new { requestId = Guid.NewGuid(), dependencyId = Guid.NewGuid() },
        "block_resource_time" => new { resourceId = Guid.NewGuid(), absenceType = "maintenance", title = "X", startTs = DateTime.UtcNow, endTs = DateTime.UtcNow.AddDays(1) },
        "unblock_resource_time" => new { resourceId = Guid.NewGuid(), absenceId = Guid.NewGuid() },
        _ => throw new InvalidOperationException($"No arguments defined for {tool}"),
    };

    [Theory]
    [MemberData(nameof(WriteTools))]
    public async Task EveryWriteToolRefusesAReadOnlyToken(string tool)
    {
        // This theory is the substitute for the verb-aware write gate the HTTP endpoints get for
        // free. It must stay exhaustive: a new write tool missing from ToolNames.Writes is a write
        // nothing checks.
        var response = await RpcAsync(
            ClientWithToken(await IssueTokenAsync(PlatformApiScopes.ScheduleRead)),
            CallTool(tool, ArgumentsFor(tool)));

        (await response.Content.ReadAsStringAsync()).Should().Contain(PlatformApiScopes.ScheduleWrite);
    }

    [Fact]
    public async Task TheWriteToolListMatchesTheToolsTheServerAdvertisesAsDestructive()
    {
        // Guards the theory above against silently going stale: if a tool is added and marked
        // Destructive without joining ToolNames.Writes, nothing would assert it refuses a reader.
        var response = await RpcAsync(
            ClientWithToken(await IssueTokenAsync(PlatformApiScopes.ScheduleRead)), ListTools());

        using var doc = JsonDocument.Parse(SsePayload(await response.Content.ReadAsStringAsync()));
        var advertised = doc.RootElement.GetProperty("result").GetProperty("tools").EnumerateArray()
            .Where(t => !t.GetProperty("annotations").TryGetProperty("readOnlyHint", out var ro) || !ro.GetBoolean())
            .Select(t => t.GetProperty("name").GetString()!)
            .ToArray();

        advertised.Should().BeEquivalentTo(ToolNames.Writes);
    }

    [Fact]
    public async Task ARawServerExceptionNeverReachesTheClient()
    {
        // create_request with a fabricated siteId trips a real FK violation in Postgres. The
        // client is a third-party LLM: it must get the pipeline's generic failure, not
        // NpgsqlException text carrying SQL state, table or column names.
        var response = await RpcAsync(
            ClientWithToken(await IssueTokenAsync(PlatformApiScopes.ScheduleRead, PlatformApiScopes.ScheduleWrite)),
            CallTool("create_request", new
            {
                name = "Orphaned",
                durationValue = 1,
                durationUnit = "hours",
                siteId = Guid.NewGuid(),
            }));

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("failed unexpectedly");
        body.Should().NotContainAny("Npgsql", "23503", "foreign key", "requests_site_id");
    }

    [Fact]
    public async Task AReadOnlyTokenCanPreviewAnAutoSchedule()
    {
        // The deliberate asymmetry: preview persists nothing, so it matches the HTTP endpoint's
        // AllowMemberWrite. A regression to "write required" here would be silent.
        var response = await RpcAsync(
            ClientWithToken(await IssueTokenAsync(PlatformApiScopes.ScheduleRead)),
            CallTool("auto_schedule_preview", new
            {
                siteId = Guid.NewGuid(),
                horizonStart = "2026-06-01",
                horizonEnd = "2026-06-30",
            }));

        // It may well fail on the unknown site — what matters is that it was not refused for scope.
        (await response.Content.ReadAsStringAsync()).Should().NotContain(PlatformApiScopes.ScheduleWrite);
    }
}
