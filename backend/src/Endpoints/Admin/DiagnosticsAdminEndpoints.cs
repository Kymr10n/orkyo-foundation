using Api.Configuration;
using Api.Helpers;
using Api.Middleware;
using Api.Security;
using Api.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Api.Endpoints.Admin;

public static class DiagnosticsAdminEndpoints
{
    public static void MapDiagnosticsAdminEndpoints(this WebApplication app)
    {
        // Public version endpoint �� no auth required
        app.MapGet("/api/version", (DeploymentConfig deploymentConfig) =>
            Results.Ok(new
            {
                version = deploymentConfig.Version ?? "unknown",
                build = Environment.GetEnvironmentVariable("ORKYO_BUILD_SHA") ?? "unknown",
            }))
            .AsInfrastructureEndpoint()
            .WithTags("Infrastructure")
            .WithName("GetVersion")
            .WithSummary("Returns application version and build info (public, no auth)");

        // Legacy API info endpoint (v1 compat)
        app.MapGet("/api/v1/info", (DeploymentConfig deploymentConfig) =>
            Results.Ok(new
            {
                name = "Orkyo API",
                version = deploymentConfig.Version ?? "unknown",
            }))
            .AsInfrastructureEndpoint()
            .WithTags("Infrastructure")
            .WithName("GetApiInfo")
            .WithSummary("Returns API name and version (public, no auth)");

        // Admin diagnostics endpoint
        var group = app.MapGroup("/api/admin")
            .RequireAuthorization()
            .RequireRateLimiting(FoundationRateLimitPolicies.AdminOperations)
            .WithTags("Admin")
            .WithMetadata(new SkipTenantResolutionAttribute());

        group.MapGet("/diagnostics", GetDiagnostics)
            .RequireSiteAdmin()
            .WithName("AdminGetDiagnostics")
            .WithSummary("Returns full platform diagnostics: DB, SMTP, auth, migration, worker, and module status");
    }

    private static async Task<IResult> GetDiagnostics(
        DeploymentConfig deploymentConfig,
        IDbConnectionFactory connectionFactory,
        ILogger<EndpointLoggerCategory> logger,
        CancellationToken ct = default)
    {
        // ── Database status ─────────────────────────────────────────────
        // Reachability uses the shared probe; on success the same block also reports the
        // migration/tenant counts (a count failure downgrades the status to unreachable).
        var dbStatus = await DbHealthProbe.ProbeControlPlaneAsync(connectionFactory, ct);
        int migrationsApplied = 0;
        int tenantCount = 0;

        if (dbStatus == DbHealthProbe.HealthyStatus)
        {
            try
            {
                await using var conn = connectionFactory.CreateControlPlaneConnection();
                await conn.OpenAsync();

                // Count applied migrations
                await using var migCmd = new Npgsql.NpgsqlCommand("SELECT COUNT(*) FROM orkyo_schema_migrations", conn);
                var migResult = await migCmd.ExecuteScalarAsync();
                migrationsApplied = migResult is null or DBNull ? 0 : Convert.ToInt32(migResult);

                // Count active tenants
                await using var tenantCmd = new Npgsql.NpgsqlCommand(
                    "SELECT COUNT(*) FROM tenants WHERE status != 'deleting'", conn);
                var tenantResult = await tenantCmd.ExecuteScalarAsync();
                tenantCount = Convert.ToInt32(tenantResult);
            }
            catch (Exception ex)
            {
                dbStatus = DbHealthProbe.UnreachableStatus;
                logger.LogWarning(ex, "Diagnostics: database health check failed");
            }
        }

        // ── SMTP status ─────────────────────────────────────────────────
        var smtpConfigured = !string.IsNullOrWhiteSpace(deploymentConfig.SmtpHost);
        var smtpHost = smtpConfigured
            ? DiagnosticsDisplayHelpers.MaskHost(deploymentConfig.SmtpHost)
            : "";

        // ── Auth status ─────────────────────────────────────────────────
        string authStatus;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var probeAuthority = deploymentConfig.OidcInternalAuthority ?? deploymentConfig.OidcAuthority;
            var discoveryUrl = OidcDiscoveryUrlPolicy.BuildDiscoveryUrl(probeAuthority);
            if (discoveryUrl is not null)
            {
                var response = await http.GetAsync(discoveryUrl);
                authStatus = OidcDiscoveryUrlPolicy.InterpretProbeOutcome(response.IsSuccessStatusCode);
            }
            else
            {
                authStatus = OidcDiscoveryUrlPolicy.NotConfiguredStatus;
            }
        }
        catch
        {
            authStatus = OidcDiscoveryUrlPolicy.UnreachableStatus;
        }

        // ── Worker status ───────────────────────────────────────────────
        // Worker runs as a separate container; we check if its dependent
        // background jobs have left evidence in the DB.
        string workerStatus;
        DateTime? lastWorkerActivity = null;

        try
        {
            await using var conn = connectionFactory.CreateControlPlaneConnection();
            await conn.OpenAsync();

            // Check for recent audit events or tenant lifecycle activity
            await using var cmd = new Npgsql.NpgsqlCommand(
                $"SELECT MAX(created_at) FROM audit_events WHERE created_at > NOW() - INTERVAL '2 hours'", conn);
            var recent = (await cmd.ExecuteScalarAsync()) as DateTime?;
            if (recent.HasValue)
            {
                workerStatus = "running";
                lastWorkerActivity = recent.Value;
            }
            else
            {
                // No recent activity — could be idle or stopped
                workerStatus = "idle";
            }
        }
        catch
        {
            workerStatus = "unknown";
        }

        // ── Modules ─────────────────────────────────────────────────────
        var observabilityEnabled = !string.IsNullOrWhiteSpace(
            Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT"));
        var lokiEnabled = !string.IsNullOrWhiteSpace(
            Environment.GetEnvironmentVariable("LOKI_URL"));

        return Results.Ok(new DiagnosticsResponse
        {
            Version = deploymentConfig.Version ?? "unknown",
            Build = Environment.GetEnvironmentVariable("ORKYO_BUILD_SHA") ?? "unknown",
            DeploymentMode = "self-hosted",
            LogLevel = deploymentConfig.LogLevel,
            Database = new DatabaseStatus
            {
                Status = dbStatus,
                MigrationsApplied = migrationsApplied,
                TenantCount = tenantCount,
            },
            Smtp = new SmtpStatus
            {
                Status = smtpConfigured ? "configured" : "not-configured",
                Host = smtpHost,
            },
            Auth = new AuthStatus
            {
                Status = authStatus,
                Provider = "keycloak",
                Realm = deploymentConfig.KeycloakRealm,
            },
            Worker = new WorkerStatus
            {
                Status = workerStatus,
                LastActivity = lastWorkerActivity,
            },
            Modules = new ModuleStatus
            {
                Observability = observabilityEnabled,
                LogAggregation = lokiEnabled,
            },
        });
    }
}

// ── DTOs ─────────────────────────────────────────────────────────────

public sealed class DiagnosticsResponse
{
    public required string Version { get; init; }
    public required string Build { get; init; }
    public required string DeploymentMode { get; init; }
    public required string LogLevel { get; init; }
    public required DatabaseStatus Database { get; init; }
    public required SmtpStatus Smtp { get; init; }
    public required AuthStatus Auth { get; init; }
    public required WorkerStatus Worker { get; init; }
    public required ModuleStatus Modules { get; init; }
}

public sealed class DatabaseStatus
{
    public required string Status { get; init; }
    public required int MigrationsApplied { get; init; }
    public required int TenantCount { get; init; }
}

public sealed class SmtpStatus
{
    public required string Status { get; init; }
    public required string Host { get; init; }
}

public sealed class AuthStatus
{
    public required string Status { get; init; }
    public required string Provider { get; init; }
    public required string Realm { get; init; }
}

public sealed class WorkerStatus
{
    public required string Status { get; init; }
    public DateTime? LastActivity { get; init; }
}

public sealed class ModuleStatus
{
    public required bool Observability { get; init; }
    public required bool LogAggregation { get; init; }
}
