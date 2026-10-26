using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Orkyo.Shared;

namespace Orkyo.Foundation.Tests.Configuration;

/// <summary>
/// Tests for <see cref="FoundationApiHostExtensions"/> — the shared API-host wiring
/// (CORS, reporting Swagger, health endpoints). This code lives in foundation but is
/// only composed into a running host by each product's <c>Program.cs</c>
/// (saas / community), so it is exercised here, in the repo that owns it, rather than
/// transitively via a product's integration suite.
///
/// The CORS cases mirror saas' <c>CorsOriginValidationTests</c> but assert against the
/// built <see cref="CorsPolicy"/> directly — no host needed — and pin that only the
/// explicit allow-list grants an origin: the tenant base domain is never a wildcard.
/// </summary>
public class FoundationApiHostCorsTests
{
    private static ServiceProvider BuildProvider(
        Dictionary<string, string?> settings,
        string environmentName)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        var env = new Mock<IWebHostEnvironment>();
        env.SetupGet(e => e.EnvironmentName).Returns(environmentName);

        var services = new ServiceCollection();
        services.AddOrkyoApiCors(configuration, env.Object);
        return services.BuildServiceProvider();
    }

    private static CorsPolicy ResolvePolicy(
        Dictionary<string, string?> settings,
        string environmentName = EnvironmentNames.Production)
    {
        var provider = BuildProvider(settings, environmentName);
        var options = provider.GetRequiredService<IOptions<CorsOptions>>().Value;
        return options.GetPolicy(options.DefaultPolicyName)!;
    }

    // ── Return value ──────────────────────────────────────────────────────────

    [Fact]
    public void AddOrkyoApiCors_ReturnsSameServiceCollection()
    {
        var configuration = new ConfigurationBuilder().Build();
        var env = new Mock<IWebHostEnvironment>();
        env.SetupGet(e => e.EnvironmentName).Returns(EnvironmentNames.Development);
        var services = new ServiceCollection();

        var returned = services.AddOrkyoApiCors(configuration, env.Object);

        returned.Should().BeSameAs(services);
    }

    // ── Explicit allow-list (no base domain) ───────────────────────────────────

    [Fact]
    public void ExplicitAllowlist_AllowsListedOrigin_AndRejectsOthers()
    {
        var policy = ResolvePolicy(
            new Dictionary<string, string?>
            {
                [ConfigKeys.CorsAllowedOrigins] = "https://app.orkyo.com, https://admin.orkyo.com",
            });

        policy.IsOriginAllowed("https://app.orkyo.com").Should().BeTrue();
        policy.IsOriginAllowed("https://admin.orkyo.com").Should().BeTrue();
        policy.IsOriginAllowed("https://evil.com").Should().BeFalse();
    }

    [Fact]
    public void ExplicitAllowlist_SetsCredentialsMethodsAndHeaders()
    {
        var policy = ResolvePolicy(
            new Dictionary<string, string?>
            {
                [ConfigKeys.CorsAllowedOrigins] = "https://app.orkyo.com",
            });

        policy.SupportsCredentials.Should().BeTrue();
        policy.Methods.Should().Contain(["GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS"]);
        policy.Headers.Should().Contain(HeaderConstants.Authorization);
    }

    // ── The base domain is never an origin rule ────────────────────────────────

    [Fact]
    public void BaseDomain_GrantsNoSubdomain_OnlyTheAllowlistApplies()
    {
        // The wildcard that used to accept any single-label https subdomain of the tenant base
        // domain is gone: under the BFF the SPA and the API share an origin, and the wildcard
        // made every host on the base domain (staging, Grafana, a dangling record) a
        // credentialed caller.
        var policy = ResolvePolicy(new Dictionary<string, string?>
        {
            [ConfigKeys.CorsAllowedOrigins] = "https://partner.example.com",
            [ConfigKeys.TenantResolutionBaseDomain] = "orkyo.test",
        });

        policy.IsOriginAllowed("https://partner.example.com").Should().BeTrue();
        policy.IsOriginAllowed("https://acme.orkyo.test").Should().BeFalse();
        policy.IsOriginAllowed("https://orkyo.test").Should().BeFalse();
    }

    // ── Empty configuration fallbacks ──────────────────────────────────────────

    [Fact]
    public void Production_NoOrigins_ThrowsAtStartup()
    {
        var provider = BuildProvider(new Dictionary<string, string?>(), EnvironmentNames.Production);

        Action act = () => _ = provider.GetRequiredService<IOptions<CorsOptions>>().Value;

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*CORS_ALLOWED_ORIGINS*");
    }

    [Fact]
    public void NonProduction_NoOrigins_AllowsAnyOrigin()
    {
        var policy = ResolvePolicy(new Dictionary<string, string?>(), environmentName: EnvironmentNames.Development);

        policy.AllowAnyOrigin.Should().BeTrue();
    }
}

/// <summary>
/// Exercises the host-side helpers — <see cref="FoundationApiHostExtensions.MapOrkyoHealthEndpoints"/>,
/// <see cref="FoundationApiHostExtensions.AddOrkyoReportingSwagger"/>, and
/// <see cref="FoundationApiHostExtensions.UseOrkyoReportingSwaggerUI"/> — through a minimal
/// in-memory <see cref="TestServer"/> host so no database or product Program is required.
/// </summary>
public class FoundationApiHostEndpointsTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Host.UseDefaultServiceProvider(o => { o.ValidateOnBuild = true; o.ValidateScopes = true; });
        builder.WebHost.UseTestServer();
        builder.Services.AddRouting();
        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["ready"]);
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddOrkyoReportingSwagger();

        _app = builder.Build();
        _app.MapOrkyoHealthEndpoints();
        _app.UseOrkyoReportingSwaggerUI();

        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    [Fact]
    public async Task HealthLive_ReturnsOkStatus()
    {
        var response = await _client.GetAsync("/health/live");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("ok");
    }

    [Fact]
    public async Task Health_ReturnsReportWithChecks()
    {
        var response = await _client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("Healthy");
        body.GetProperty("checks").EnumerateArray()
            .Select(c => c.GetProperty("name").GetString())
            .Should().Contain("self");
    }

    [Fact]
    public async Task HealthReady_ReturnsOk_WhenReadyCheckPasses()
    {
        var response = await _client.GetAsync("/health/ready");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ReportingSwaggerDocument_IsServed()
    {
        var response = await _client.GetAsync("/swagger/reporting-v1/swagger.json");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Orkyo Reporting API");
    }
}
