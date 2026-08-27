using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Services;
using Microsoft.Extensions.DependencyInjection;
using Orkyo.Foundation.Tests.Mocks;
using Xunit;

namespace Orkyo.Foundation.Tests.Endpoints.Ai;

/// <summary>
/// Integration tests for the assistant's two admin surfaces — the workspace API key
/// (/api/ai/credentials) and the per-user grants and daily limits (/api/ai/allowances).
///
/// The key surface has one rule that outranks the rest: no response ever carries the key
/// back, whatever the caller does. The allowance surface is an authorization boundary —
/// only an admin sets who may spend tokens — so its rejection paths matter more than its
/// happy path.
/// </summary>
[Collection("Database collection")]
public class AiAdminEndpointsTests
{
    private readonly HttpClient _client;
    private readonly DatabaseFixture _fixture;
    private const string TenantSlug = TestConstants.TenantSlug;

    public AiAdminEndpointsTests(DatabaseFixture databaseFixture)
    {
        _fixture = databaseFixture;
        _client = databaseFixture.Factory.CreateClient();
        _client.DefaultRequestHeaders.Add(HeaderConstants.TenantSlug, TenantSlug);
    }

    private string? _cachedAdminToken;

    private async Task<string> GetAdminAuthTokenAsync()
    {
        if (_cachedAdminToken != null) return _cachedAdminToken;

        var email = $"ai_admin_{Guid.NewGuid()}@example.com";
        var userId = await DatabaseTestUtils.CreateTestUserAsync(
            email, "AI Admin", TenantSlug, "admin", active: true);

        var tokenData = new
        {
            UserId = userId.ToString(),
            Email = email,
            DisplayName = "AI Admin",
            TenantId = "00000000-0000-0000-0000-000000000001",
            TenantSlug,
            IsTenantAdmin = true,
            Role = "admin"
        };

        var json = JsonSerializer.Serialize(tokenData);
        _cachedAdminToken = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json));
        return _cachedAdminToken;
    }

    private async Task<HttpRequestMessage> AuthRequest(
        HttpMethod method, string url, object? content = null)
    {
        var msg = new HttpRequestMessage(method, url);
        msg.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", await GetAdminAuthTokenAsync());
        if (content != null) msg.Content = JsonContent.Create(content);
        return msg;
    }

    private static async Task<JsonElement> BodyOf(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    // ─── PUT /api/ai/credentials ─────────────────────────────────────────────────

    [Theory]
    [InlineData("", "An API key is required.")]
    [InlineData("sk-ant-short", "That key is too short to be an Anthropic API key.")]
    [InlineData("not-an-anthropic-key-but-long-enough", "An Anthropic API key starts with 'sk-ant-'.")]
    public async Task SaveCredential_WithAKeyThatFailsShapeChecks_Returns400(
        string apiKey, string expectedMessage)
    {
        var response = await _client.SendAsync(
            await AuthRequest(HttpMethod.Put, "/api/ai/credentials", new { apiKey }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var problem = await BodyOf(response);
        problem.GetProperty("code").GetString()
            .Should().Be(Api.Constants.ErrorCodes.ValidationError);
        problem.GetProperty("errors").GetProperty("ApiKey")
            .EnumerateArray().Select(e => e.GetString())
            .Should().Contain(expectedMessage);

        // The rejected key must not come back in the error body.
        if (apiKey.Length > 0)
            (await response.Content.ReadAsStringAsync()).Should().NotContain(apiKey);
    }

    [Fact]
    public async Task SaveCredential_ThenGet_ReportsConfiguredWithoutReturningTheKey()
    {
        const string apiKey = "sk-ant-api03-integration-test-key-value";

        var saved = await _client.SendAsync(
            await AuthRequest(HttpMethod.Put, "/api/ai/credentials", new { apiKey }));
        saved.StatusCode.Should().Be(HttpStatusCode.OK);
        (await saved.Content.ReadAsStringAsync()).Should().NotContain(apiKey);

        var read = await _client.SendAsync(await AuthRequest(HttpMethod.Get, "/api/ai/credentials"));
        read.StatusCode.Should().Be(HttpStatusCode.OK);

        var status = await BodyOf(read);
        status.GetProperty("configured").GetBoolean().Should().BeTrue();
        (await read.Content.ReadAsStringAsync()).Should().NotContain(apiKey);
    }

    [Fact]
    public async Task DeleteCredential_RemovesTheKeyAndTheProbeReportsNotConfigured()
    {
        await _client.SendAsync(await AuthRequest(HttpMethod.Put, "/api/ai/credentials",
            new { apiKey = "sk-ant-api03-integration-test-key-value" }));

        var deleted = await _client.SendAsync(
            await AuthRequest(HttpMethod.Delete, "/api/ai/credentials"));
        deleted.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var read = await _client.SendAsync(await AuthRequest(HttpMethod.Get, "/api/ai/credentials"));
        (await BodyOf(read)).GetProperty("configured").GetBoolean().Should().BeFalse();

        // With no key stored the endpoint answers from the database and never calls out.
        var probe = await _client.SendAsync(
            await AuthRequest(HttpMethod.Post, "/api/ai/credentials/test"));
        probe.StatusCode.Should().Be(HttpStatusCode.OK);
        (await BodyOf(probe)).GetProperty("reason").GetString().Should().Be("not_configured");
    }

    // ─── POST /api/ai/credentials/test ───────────────────────────────────────────

    [Fact]
    public async Task TestCredential_RecordsTheProbeWhicheverWayItGoes()
    {
        await _client.SendAsync(await AuthRequest(HttpMethod.Put, "/api/ai/credentials",
            new { apiKey = "sk-ant-api03-integration-test-key-value" }));

        var gateway = _fixture.Factory.Services.GetRequiredService<StubAnthropicGateway>();
        gateway.TestResult = new Api.Models.AiCredentialTestResult
        {
            Ok = false,
            Reason = "invalid_key"
        };

        var response = await _client.SendAsync(
            await AuthRequest(HttpMethod.Post, "/api/ai/credentials/test"));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await BodyOf(response);
        result.GetProperty("ok").GetBoolean().Should().BeFalse();
        result.GetProperty("reason").GetString().Should().Be("invalid_key");
        gateway.TestCallCount.Should().BeGreaterThan(0);

        // A failed probe is audited too — that is the point of recording it. Saves and
        // removals were always audited and probes were not, so the audit row is what
        // proves the endpoint reached RecordTestedAsync.
        var audit = await BodyOf(await _client.SendAsync(await AuthRequest(
            HttpMethod.Get,
            $"/api/audit?action={Api.Constants.TenantAuditActions.AiCredentialTested}")));
        audit.GetProperty("totalCount").GetInt32().Should().BeGreaterThan(0);

        await _client.SendAsync(await AuthRequest(HttpMethod.Delete, "/api/ai/credentials"));
    }

    // ─── PUT /api/ai/allowances/daily-limits ─────────────────────────────────────

    [Theory]
    [InlineData(0, null)]
    [InlineData(null, 0)]
    [InlineData(20_000, null)]
    public async Task SaveDailyLimits_OutsideThePlausibleRange_Returns400(
        int? userDailyTurns, int? tenantDailyTurns)
    {
        var response = await _client.SendAsync(
            await AuthRequest(HttpMethod.Put, "/api/ai/allowances/daily-limits",
                new { userDailyTurns, tenantDailyTurns }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await BodyOf(response)).GetProperty("code").GetString()
            .Should().Be(Api.Constants.ErrorCodes.ValidationError);
    }

    [Fact]
    public async Task SaveDailyLimits_WithPlausibleValues_PersistsThem()
    {
        var response = await _client.SendAsync(
            await AuthRequest(HttpMethod.Put, "/api/ai/allowances/daily-limits",
                new { userDailyTurns = 25, tenantDailyTurns = 200 }));
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var limits = await BodyOf(await _client.SendAsync(
            await AuthRequest(HttpMethod.Get, "/api/ai/allowances/daily-limits")));
        limits.GetProperty("userDailyTurns").GetInt32().Should().Be(25);
        limits.GetProperty("tenantDailyTurns").GetInt32().Should().Be(200);
    }

    // ─── PUT and DELETE /api/ai/allowances/{userId} ──────────────────────────────

    [Fact]
    public async Task SaveAllowance_WithANegativeLimit_Returns400()
    {
        var response = await _client.SendAsync(
            await AuthRequest(HttpMethod.Put, $"/api/ai/allowances/{Guid.NewGuid()}",
                new { monthlyTokenLimit = -1 }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await BodyOf(response)).GetProperty("code").GetString()
            .Should().Be(Api.Constants.ErrorCodes.ValidationError);
    }

    [Fact]
    public async Task SaveAllowance_ThenRevoke_GrantsAndRemovesAccess()
    {
        var email = $"ai_member_{Guid.NewGuid()}@example.com";
        var memberId = await DatabaseTestUtils.CreateTestUserAsync(
            email, "AI Member", TenantSlug, "editor", active: true);

        var granted = await _client.SendAsync(
            await AuthRequest(HttpMethod.Put, $"/api/ai/allowances/{memberId}",
                new { monthlyTokenLimit = 50_000 }));
        granted.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var revoked = await _client.SendAsync(
            await AuthRequest(HttpMethod.Delete, $"/api/ai/allowances/{memberId}"));
        revoked.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task RevokeAllowance_ForSomeoneWhoNeverHadOne_Returns404()
    {
        var response = await _client.SendAsync(
            await AuthRequest(HttpMethod.Delete, $"/api/ai/allowances/{Guid.NewGuid()}"));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
