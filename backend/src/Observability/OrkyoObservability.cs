using Microsoft.AspNetCore.Builder;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.Grafana.Loki;

namespace Orkyo.Foundation.Observability;

/// <summary>
/// Shared Serilog + Loki bootstrap consumed by both Orkyo product editions.
/// Products call <see cref="UseOrkyoLogging"/> once during builder configuration.
///
/// Behaviour:
///  - Always writes to Console.
///  - If the LOKI_URL environment variable is set (or appsettings provides a
///    Loki override via <c>Serilog:WriteTo</c>), also writes to that Loki
///    endpoint with consistent labels {service, env}.
///  - Reads Serilog overrides from configuration (e.g. log levels per
///    namespace), so callers can tune verbosity without touching this helper.
/// </summary>
public static class OrkyoObservability
{
    /// <summary>
    /// Wire Serilog as the host logger with the standard Orkyo pipeline.
    /// Call exactly once on the WebApplicationBuilder, before <c>builder.Build()</c>.
    /// </summary>
    /// <param name="builder">The application builder.</param>
    /// <param name="serviceName">
    /// Stable identifier used as the {service} label on Loki and the
    /// "service" log property. Examples: "orkyo-saas-api", "orkyo-community-api",
    /// "orkyo-saas-worker".
    /// </param>
    public static WebApplicationBuilder UseOrkyoLogging(this WebApplicationBuilder builder, string serviceName)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        builder.Host.UseSerilog((ctx, _, cfg) =>
        {
            cfg.ReadFrom.Configuration(ctx.Configuration)
               .Enrich.FromLogContext()
               .Enrich.WithProperty("service", serviceName)
               .WriteTo.Console();

            var lokiUrl = Environment.GetEnvironmentVariable("LOKI_URL");
            if (!string.IsNullOrWhiteSpace(lokiUrl))
            {
                cfg.WriteTo.GrafanaLoki(
                    lokiUrl,
                    labels:
                    [
                        new LokiLabel { Key = "service", Value = serviceName },
                        new LokiLabel { Key = "env",     Value = ctx.HostingEnvironment.EnvironmentName }
                    ],
                    propertiesAsLabels: ["level"]);
            }
        });

        return builder;
    }

    /// <summary>
    /// Bootstrap logger used before the host is built. Call once at the very
    /// top of Program.cs so startup errors are captured. Replaced by
    /// <see cref="UseOrkyoLogging"/> when the host starts.
    /// </summary>
    public static void InitBootstrapLogger()
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .CreateBootstrapLogger();
    }
}
