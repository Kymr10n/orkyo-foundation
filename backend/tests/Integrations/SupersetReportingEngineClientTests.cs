using System.Net;
using System.Text.Json;
using Api.Integrations.Reporting;
using Api.Reporting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Orkyo.Foundation.Tests.Integrations;

public class SupersetReportingEngineClientTests
{
    private static readonly Guid DashboardUuid = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private const string FakeToken = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.fake";
    private const string AdminToken = "admin-access-token";

    // ── Guest token ───────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateGuestTokenAsync_CallsCorrectEndpointAndReturnsToken()
    {
        var handler = new FakeMessageHandler(req =>
        {
            if (req.RequestUri!.PathAndQuery == "/api/v1/security/login")
                return Json(new { access_token = AdminToken });

            if (req.RequestUri.PathAndQuery == "/api/v1/security/csrf_token/")
                return Json(new { result = "fake-csrf-token" });

            if (req.RequestUri.PathAndQuery == "/api/v1/security/guest_token/")
            {
                req.Headers.Authorization!.Parameter.Should().Be(AdminToken);

                var body = ParseBody(req);
                body.GetProperty("resources")[0].GetProperty("id").GetString()
                    .Should().Be(DashboardUuid.ToString());

                return Json(new { token = FakeToken });
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var client = BuildClient(handler);
        var token = await client.CreateGuestTokenAsync(DashboardUuid);
        token.Should().Be(FakeToken);
    }

    [Fact]
    public async Task CreateGuestTokenAsync_CachesAccessToken_CallsLoginOnce()
    {
        var loginCount = 0;
        var handler = new FakeMessageHandler(req =>
        {
            if (req.RequestUri!.PathAndQuery == "/api/v1/security/login")
            {
                loginCount++;
                return Json(new { access_token = AdminToken });
            }
            return Json(new { token = FakeToken });
        });

        var client = BuildClient(handler);
        await client.CreateGuestTokenAsync(DashboardUuid);
        await client.CreateGuestTokenAsync(DashboardUuid);

        loginCount.Should().Be(1);
    }

    [Fact]
    public async Task CreateGuestTokenAsync_WhenSupersetFails_Throws()
    {
        var handler = new FakeMessageHandler(req =>
        {
            if (req.RequestUri!.PathAndQuery == "/api/v1/security/login")
                return Json(new { access_token = AdminToken });
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent("Superset is down"),
            };
        });

        var client = BuildClient(handler);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.CreateGuestTokenAsync(DashboardUuid));
    }

    // ── Ensure datasource ─────────────────────────────────────────────────────

    [Fact]
    public async Task EnsureDatabaseAsync_WhenNameExists_ReturnsExistingUuid()
    {
        var existingUuid = Guid.NewGuid();
        var handler = new FakeMessageHandler(req =>
        {
            if (req.RequestUri!.PathAndQuery == "/api/v1/security/login")
                return Json(new { access_token = AdminToken });
            if (req.RequestUri.PathAndQuery == "/api/v1/security/csrf_token/")
                return Json(new { result = "fake-csrf-token" });
            if (req.RequestUri.PathAndQuery == "/api/v1/database/")
            {
                if (req.Method == HttpMethod.Get)
                    return Json(new
                    {
                        result = new[] { new { database_name = "tenant_foo_reporting", uuid = existingUuid.ToString() } }
                    });
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var client = BuildClient(handler);
        var uuid = await client.EnsureDatabaseAsync("tenant_foo_reporting", "postgresql+psycopg2://...");
        uuid.Should().Be(existingUuid);
    }

    [Fact]
    public async Task EnsureDatabaseAsync_WhenNameAbsent_CreatesAndReturnsNewUuid()
    {
        var newUuid = Guid.NewGuid();
        var handler = new FakeMessageHandler(req =>
        {
            if (req.RequestUri!.PathAndQuery == "/api/v1/security/login")
                return Json(new { access_token = AdminToken });
            if (req.RequestUri.PathAndQuery == "/api/v1/security/csrf_token/")
                return Json(new { result = "fake-csrf-token" });
            if (req.RequestUri.PathAndQuery == "/api/v1/database/")
            {
                if (req.Method == HttpMethod.Get)
                    return Json(new { result = Array.Empty<object>() });
                if (req.Method == HttpMethod.Post)
                    return Json(new { result = new { uuid = newUuid.ToString() } });
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var client = BuildClient(handler);
        var uuid = await client.EnsureDatabaseAsync("tenant_bar_reporting", "postgresql+psycopg2://...");
        uuid.Should().Be(newUuid);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static SupersetReportingEngineClient BuildClient(FakeMessageHandler handler) =>
        new(
            http: new HttpClient(handler) { BaseAddress = null },
            opts: Options.Create(new ReportingOptions
            {
                BaseUrl = "https://superset.test",
                AdminUsername = "admin",
                AdminPassword = "admin",
                EmbedTokenTtlSeconds = 300,
            }),
            logger: NullLogger<SupersetReportingEngineClient>.Instance);

    private static HttpResponseMessage Json(object body) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(body, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }),
                System.Text.Encoding.UTF8,
                "application/json"),
        };

    private static JsonElement ParseBody(HttpRequestMessage req)
    {
        var json = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
        return JsonDocument.Parse(json).RootElement;
    }

    private sealed class FakeMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;
        public FakeMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) => _handler = handler;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct) =>
            Task.FromResult(_handler(req));
    }
}
