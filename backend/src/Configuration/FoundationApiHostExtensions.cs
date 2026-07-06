using Api.Constants;
using Api.Middleware;
using Api.Reporting.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi;
using Orkyo.Shared;

namespace Api.Configuration;

/// <summary>
/// Shared API-host wiring used identically by every product's <c>Program.cs</c>
/// (CORS, the reporting-v1 Swagger document + UI, and the infrastructure health
/// endpoints). Product-specific concerns — health-check <em>registration</em>
/// (the connection string differs), rate limiting, Prometheus metrics, and the
/// middleware ordering itself — stay in each product's Program.cs.
/// </summary>
public static class FoundationApiHostExtensions
{
    private static readonly string[] CorsMethods = ["GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS"];

    private static readonly string[] CorsHeaders =
    [
        "Content-Type",
        "Authorization",
        HeaderConstants.TenantSlug,
        HeaderConstants.CorrelationId,
        HeaderConstants.CsrfToken,
    ];

    /// <summary>
    /// Adds the default CORS policy shared by all products: an explicit allow-list
    /// from <c>CORS_ALLOWED_ORIGINS</c>, the standard methods/headers, and credentials.
    /// In non-production with no configured origins it falls back to an open policy;
    /// in production an empty policy is a hard startup failure.
    /// </summary>
    /// <param name="allowBaseDomainSubdomains">
    /// When true (SaaS), origins that are https subdomains of
    /// <c>TenantResolution:BaseDomain</c> are also allowed, supporting per-tenant
    /// subdomains without listing each one. Community leaves this false.
    /// </param>
    public static IServiceCollection AddOrkyoApiCors(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment,
        bool allowBaseDomainSubdomains = false)
    {
        services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
            {
                var rawOrigins = configuration[ConfigKeys.CorsAllowedOrigins] ?? "";
                var allowedOrigins = rawOrigins
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var baseDomain = allowBaseDomainSubdomains
                    ? configuration[ConfigKeys.TenantResolutionBaseDomain]
                    : null;

                if (allowedOrigins.Count > 0 || !string.IsNullOrEmpty(baseDomain))
                {
                    if (!string.IsNullOrEmpty(baseDomain))
                    {
                        var subdomainSuffix = $".{baseDomain}";
                        policy.SetIsOriginAllowed(origin =>
                        {
                            if (allowedOrigins.Contains(origin))
                                return true;
                            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) ||
                                uri.Scheme != "https" ||
                                !uri.Host.EndsWith(subdomainSuffix, StringComparison.OrdinalIgnoreCase))
                                return false;
                            // Only a single tenant subdomain label (slug.baseDomain) is trusted —
                            // reject nested hosts like a.b.baseDomain to shrink the credentialed-CORS
                            // surface exposed to dangling/takeover subdomains.
                            var label = uri.Host[..^subdomainSuffix.Length];
                            return label.Length > 0 && !label.Contains('.');
                        });
                    }
                    else
                    {
                        policy.WithOrigins([.. allowedOrigins]);
                    }

                    policy.WithMethods(CorsMethods)
                        .WithHeaders(CorsHeaders)
                        .AllowCredentials();
                }
                else if (environment.IsProduction())
                {
                    throw new InvalidOperationException(
                        "CORS_ALLOWED_ORIGINS must be set in production. Refusing to start with open CORS policy.");
                }
                else
                {
                    policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
                }
            });
        });

        return services;
    }

    /// <summary>
    /// Registers the public, read-only <c>reporting-v1</c> Swagger document and its
    /// reporting-token security scheme. Only <c>api/reporting*</c> routes are included.
    /// </summary>
    public static IServiceCollection AddOrkyoReportingSwagger(this IServiceCollection services)
    {
        services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("reporting-v1", new OpenApiInfo
            {
                Title = "Orkyo Reporting API",
                Version = "v1",
                Description = "Read-only, tenant-scoped reporting endpoints for Power BI, Excel, Metabase, Superset, and custom integrations. " +
                              "Authenticate with an orkyo_rpt_* reporting token in the Authorization header. " +
                              "Tenant isolation is enforced server-side — no tenantId parameter accepted.",
            });
            c.AddSecurityDefinition(ReportingTokenAuthHandler.SchemeName, new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "orkyo_rpt_<prefix>_<secret>",
                In = ParameterLocation.Header,
                Description = "Reporting API token in the format: Bearer orkyo_rpt_<prefix>_<secret>",
            });
            c.AddSecurityRequirement(_ => new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecuritySchemeReference(ReportingTokenAuthHandler.SchemeName),
                    []
                }
            });
            c.DocInclusionPredicate((docName, apiDesc) =>
                docName == "reporting-v1" &&
                (apiDesc.RelativePath?.StartsWith("api/reporting", StringComparison.OrdinalIgnoreCase) ?? false));
        });

        return services;
    }

    /// <summary>
    /// Serves the reporting-v1 Swagger document and UI at <c>/swagger/reporting</c>.
    /// </summary>
    public static WebApplication UseOrkyoReportingSwaggerUI(this WebApplication app)
    {
        app.UseSwagger();
        app.UseSwaggerUI(c =>
        {
            c.SwaggerEndpoint("/swagger/reporting-v1/swagger.json", "Orkyo Reporting API v1");
            c.RoutePrefix = "swagger/reporting";
        });
        return app;
    }

    /// <summary>
    /// Maps the infrastructure health endpoints shared by all products:
    /// <c>/health</c> (full report), <c>/health/live</c> (liveness), and
    /// <c>/health/ready</c> (checks tagged <c>ready</c>). The product registers the
    /// checks themselves (e.g. <c>AddNpgSql(...)</c>) since the connection string differs.
    /// </summary>
    public static WebApplication MapOrkyoHealthEndpoints(this WebApplication app)
    {
        app.MapHealthChecks("/health", new HealthCheckOptions
        {
            ResponseWriter = WriteHealthCheckResponse
        }).AsInfrastructureEndpoint();

        app.MapGet("/health/live", () => Results.Ok(new { status = "ok" })).AsInfrastructureEndpoint();

        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("ready")
        }).AsInfrastructureEndpoint();

        return app;
    }

    private static async Task WriteHealthCheckResponse(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";
        var result = System.Text.Json.JsonSerializer.Serialize(new
        {
            status = report.Status.ToString(),
            utc = DateTime.UtcNow.ToString("O"),
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                description = e.Value.Description,
                duration = e.Value.Duration.TotalMilliseconds
            })
        });
        await context.Response.WriteAsync(result);
    }
}
