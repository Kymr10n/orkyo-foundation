using System.Security.Cryptography;
using System.Text;
using Api.Middleware;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Prometheus;

namespace Api.Configuration;

/// <summary>
/// Opt-in Prometheus metrics helpers. Foundation ships no runtime wiring — a product's
/// <c>Program.cs</c> calls <see cref="UseOrkyoMetrics"/> (HTTP request metrics) and
/// <see cref="MapOrkyoMetricsEndpoint"/> (the token-gated <c>/metrics</c> scrape endpoint)
/// explicitly. Neither needs a DI registration (prometheus-net keeps its registry in
/// process-wide statics), so there is no matching <c>AddOrkyoMetrics()</c>.
/// </summary>
public static class OrkyoMetricsExtensions
{
    /// <summary>
    /// Records HTTP request metrics (request count/duration by route, in-progress gauge)
    /// into the default Prometheus registry via prometheus-net's <c>UseHttpMetrics</c>.
    /// Place after <c>UseRouting()</c> so route labels resolve.
    /// </summary>
    public static IApplicationBuilder UseOrkyoMetrics(this IApplicationBuilder app)
        => app.UseHttpMetrics();

    /// <summary>
    /// Maps <c>GET /metrics</c>, gated by HTTP Basic auth: the caller must send
    /// <c>Authorization: Basic base64("prometheus:{metricsToken}")</c>; the comparison is
    /// fixed-time. Wrong or missing credentials → 401.
    ///
    /// <para><b>Fail-secure:</b> when <paramref name="metricsToken"/> is null or empty the
    /// endpoint is NOT mapped at all — <c>/metrics</c> returns 404. An unset token must never
    /// expose the registry anonymously (in SaaS it would leak per-tenant metric cardinality),
    /// so metrics scraping is simply OFF until a token is configured
    /// (<c>ConfigKeys.MetricsToken</c> / <c>METRICS_TOKEN</c>).</para>
    /// </summary>
    public static WebApplication MapOrkyoMetricsEndpoint(this WebApplication app, string? metricsToken)
    {
        if (string.IsNullOrEmpty(metricsToken))
            return app;

        var expected = "Basic " + Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"prometheus:{metricsToken}"));
        var expectedBytes = Encoding.UTF8.GetBytes(expected);

        app.MapGet("/metrics", async (HttpContext context) =>
        {
            var auth = context.Request.Headers.Authorization.FirstOrDefault() ?? "";
            var authBytes = Encoding.UTF8.GetBytes(auth);
            if (authBytes.Length != expectedBytes.Length
                || !CryptographicOperations.FixedTimeEquals(authBytes, expectedBytes))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            context.Response.ContentType = "text/plain; version=0.0.4; charset=utf-8";
            await Metrics.DefaultRegistry.CollectAndExportAsTextAsync(context.Response.Body, context.RequestAborted);
        }).AsInfrastructureEndpoint();

        return app;
    }
}
