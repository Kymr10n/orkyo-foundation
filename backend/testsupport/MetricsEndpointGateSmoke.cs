using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Orkyo.Foundation.TestSupport;

/// <summary>
/// One smoke check that a product's <c>Program.cs</c> plumbs <c>METRICS_TOKEN</c> into
/// foundation's <c>MapOrkyoMetricsEndpoint</c> helper: without a token the endpoint is not
/// mapped (fail-secure 404); with one, <c>Authorization: Basic base64(prometheus:{token})</c>
/// is accepted and a missing header is refused. The full gate matrix (wrong token, wrong
/// user, Bearer header, content type) is covered by foundation's <c>OrkyoMetricsEndpointTests</c>.
/// </summary>
public static class MetricsEndpointGateSmoke
{
    /// <summary>The token the smoke check configures on the derived host.</summary>
    public const string Token = "supersecrettoken";

    /// <summary>A derived factory with <c>METRICS_TOKEN</c> set; each call boots a fresh host, so reuse it.</summary>
    public static WebApplicationFactory<TProgram> WithMetricsToken<TProgram>(
        this WebApplicationFactory<TProgram> factory, string token)
        where TProgram : class
    {
        ArgumentNullException.ThrowIfNull(factory);
        return factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["METRICS_TOKEN"] = token
                })));
    }

    /// <summary>The <c>Basic</c> header the metrics gate expects.</summary>
    public static AuthenticationHeaderValue BasicAuth(string user, string token) =>
        new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{token}")));

    /// <summary>
    /// Runs the three probes against <paramref name="factory"/> (no token configured) and a
    /// derived host with <see cref="Token"/> configured. Returns the failures; empty means pass.
    /// </summary>
    public static async Task<IReadOnlyList<string>> RunAsync<TProgram>(WebApplicationFactory<TProgram> factory)
        where TProgram : class
    {
        ArgumentNullException.ThrowIfNull(factory);
        var failures = new List<string>();

        var noToken = await factory.CreateClient().GetAsync("/metrics");
        if (noToken.StatusCode != HttpStatusCode.NotFound)
            failures.Add($"no METRICS_TOKEN configured: expected 404 (endpoint not mapped), got {(int)noToken.StatusCode}");

        using var withToken = factory.WithMetricsToken(Token);
        var client = withToken.CreateClient();

        var missingHeader = await client.GetAsync("/metrics");
        if (missingHeader.StatusCode != HttpStatusCode.Unauthorized)
            failures.Add($"token configured, no Authorization header: expected 401, got {(int)missingHeader.StatusCode}");

        var request = new HttpRequestMessage(HttpMethod.Get, "/metrics");
        request.Headers.Authorization = BasicAuth("prometheus", Token);
        var correct = await client.SendAsync(request);
        if (correct.StatusCode != HttpStatusCode.OK)
            failures.Add($"token configured, correct Basic credentials: expected 200, got {(int)correct.StatusCode}");
        else if (correct.Content.Headers.ContentType?.MediaType != "text/plain")
            failures.Add($"token configured, correct Basic credentials: expected text/plain, got '{correct.Content.Headers.ContentType?.MediaType}'");

        return failures;
    }
}
