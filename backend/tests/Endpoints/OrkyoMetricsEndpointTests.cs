using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Api.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Orkyo.Foundation.Tests.Endpoints;

/// <summary>
/// Token-gate contract for the opt-in <see cref="OrkyoMetricsExtensions"/> helpers:
/// no configured token → the endpoint is not mapped (fail-secure 404); a configured
/// token requires <c>Authorization: Basic base64(prometheus:{token})</c> (fixed-time
/// compare, 401 otherwise). Runs a minimal standalone host — no database needed.
/// </summary>
public sealed class OrkyoMetricsEndpointTests : IAsyncLifetime
{
    private WebApplication? _app;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_app is not null)
            await _app.DisposeAsync();
    }

    private async Task<HttpClient> StartAppAsync(string? metricsToken)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        _app = builder.Build();
        _app.UseRouting();
        _app.UseOrkyoMetrics();
        _app.MapOrkyoMetricsEndpoint(metricsToken);

        await _app.StartAsync();
        return _app.GetTestClient();
    }

    private static AuthenticationHeaderValue BasicAuth(string user, string token) =>
        new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{token}")));

    [Fact]
    public async Task NoTokenConfigured_EndpointNotMapped_Returns404()
    {
        var client = await StartAppAsync(metricsToken: null);

        var response = await client.GetAsync("/metrics");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, "fail-secure: unconfigured token must not expose the registry");
    }

    [Fact]
    public async Task EmptyTokenConfigured_EndpointNotMapped_Returns404()
    {
        var client = await StartAppAsync(metricsToken: "");

        var response = await client.GetAsync("/metrics");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task MissingHeader_Returns401()
    {
        var client = await StartAppAsync(metricsToken: "supersecrettoken");

        var response = await client.GetAsync("/metrics");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task WrongToken_Returns401()
    {
        var client = await StartAppAsync(metricsToken: "supersecrettoken");
        var request = new HttpRequestMessage(HttpMethod.Get, "/metrics");
        request.Headers.Authorization = BasicAuth("prometheus", "wrongtoken");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task WrongUsername_Returns401()
    {
        var client = await StartAppAsync(metricsToken: "supersecrettoken");
        var request = new HttpRequestMessage(HttpMethod.Get, "/metrics");
        request.Headers.Authorization = BasicAuth("admin", "supersecrettoken");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task BearerHeader_Returns401()
    {
        var client = await StartAppAsync(metricsToken: "supersecrettoken");
        var request = new HttpRequestMessage(HttpMethod.Get, "/metrics");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "supersecrettoken");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CorrectCredentials_Returns200_WithPrometheusContentType()
    {
        var client = await StartAppAsync(metricsToken: "supersecrettoken");
        var request = new HttpRequestMessage(HttpMethod.Get, "/metrics");
        request.Headers.Authorization = BasicAuth("prometheus", "supersecrettoken");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/plain");
    }

    [Fact]
    public async Task MetricsEndpoint_CarriesInfrastructureMetadata()
    {
        var client = await StartAppAsync(metricsToken: "supersecrettoken");
        var request = new HttpRequestMessage(HttpMethod.Get, "/metrics");
        request.Headers.Authorization = BasicAuth("prometheus", "supersecrettoken");
        await client.SendAsync(request);

        var endpointSources = ((IEndpointRouteBuilder)_app!).DataSources;
        var metricsEndpoint = endpointSources
            .SelectMany(s => s.Endpoints)
            .OfType<Microsoft.AspNetCore.Routing.RouteEndpoint>()
            .Single(e => e.RoutePattern.RawText == "/metrics");

        metricsEndpoint.Metadata.GetMetadata<Api.Middleware.SkipTenantResolutionAttribute>()
            .Should().NotBeNull("scrapers carry no tenant context — the endpoint must skip tenant resolution");
    }
}
