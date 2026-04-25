using Api.Configuration;
using Api.Middleware;
using Api.Security;
using Api.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Api.Endpoints.Admin;

public static class SettingsAdminEndpoints
{
    public static void MapSettingsAdminEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/admin")
            .RequireAuthorization()
            .RequireRateLimiting("admin-operations")
            .WithTags("Admin")
            .WithMetadata(new SkipTenantResolutionAttribute());

        group.MapGet("/settings", GetSettings)
            .RequireSiteAdmin()
            .WithName("AdminGetSettings")
            .WithSummary("Get platform settings: editable runtime config and read-only deployment info");

        group.MapPut("/settings", UpdateSettings)
            .RequireSiteAdmin()
            .WithName("AdminUpdateSettings")
            .WithSummary("Update runtime settings");
    }

    private static async Task<IResult> GetSettings(
        ISiteSettingsService siteSettingsService,
        DeploymentConfig deploymentConfig,
        IDbConnectionFactory connectionFactory,
        ILogger<EndpointLoggerCategory> logger)
    {
        var runtime = await siteSettingsService.GetRuntimeConfigAsync();

        // Build redacted deployment info
        var deployment = deploymentConfig.Redacted();

        // Probe DB status (lightweight — reuses existing connection factory)
        string dbStatus;
        try
        {
            await using var conn = connectionFactory.CreateControlPlaneConnection();
            await conn.OpenAsync();
            await using var cmd = new Npgsql.NpgsqlCommand("SELECT 1", conn);
            await cmd.ExecuteScalarAsync();
            dbStatus = "healthy";
        }
        catch
        {
            dbStatus = "unreachable";
        }

        return Results.Ok(new AdminSettingsResponse
        {
            Runtime = new RuntimeSettings
            {
                DefaultTimezone = runtime.DefaultTimezone,
                WorkingHoursStart = runtime.WorkingHoursStart,
                WorkingHoursEnd = runtime.WorkingHoursEnd,
                HolidayProviderEnabled = runtime.HolidayProviderEnabled,
                BrandingName = runtime.BrandingName,
                BrandingLogoUrl = runtime.BrandingLogoUrl,
            },
            Deployment = new DeploymentSettings
            {
                PublicUrl = deployment.GetValueOrDefault(nameof(DeploymentConfig.PublicUrl)) ?? "",
                AuthPublicUrl = deployment.GetValueOrDefault(nameof(DeploymentConfig.AuthPublicUrl)) ?? "",
                SmtpHost = deployment.GetValueOrDefault(nameof(DeploymentConfig.SmtpHost)) ?? "",
                SmtpPort = deploymentConfig.SmtpPort,
                KeycloakRealm = deployment.GetValueOrDefault(nameof(DeploymentConfig.KeycloakRealm)) ?? "",
                FileStoragePath = deployment.GetValueOrDefault(nameof(DeploymentConfig.FileStoragePath)) ?? "",
                LogLevel = deployment.GetValueOrDefault(nameof(DeploymentConfig.LogLevel)) ?? "Information",
            },
            SystemInfo = new SystemInfo
            {
                Version = deploymentConfig.Version ?? "unknown",
                DatabaseStatus = dbStatus,
                SmtpConfigured = !string.IsNullOrWhiteSpace(deploymentConfig.SmtpHost),
                AuthProvider = "keycloak",
                AuthRealm = deploymentConfig.KeycloakRealm,
            },
        });
    }

    private static async Task<IResult> UpdateSettings(
        UpdateSettingsRequest request,
        ISiteSettingsService siteSettingsService,
        IAdminAuditService auditService,
        ICurrentPrincipal principal,
        ILogger<EndpointLoggerCategory> logger)
    {
        if (request.Settings == null || request.Settings.Count == 0)
            return Results.BadRequest(new { error = "No settings provided" });

        // Map property-style keys to DB keys
        var dbUpdates = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in request.Settings)
        {
            // Accept both DB keys ("general.default_timezone") and property names ("DefaultTimezone")
            if (RuntimeConfig.KeyMap.ContainsKey(key))
            {
                dbUpdates[key] = value;
            }
            else if (RuntimeConfig.PropertyToKeyMap.TryGetValue(key, out var dbKey))
            {
                dbUpdates[dbKey] = value;
            }
            else
            {
                return Results.BadRequest(new { error = $"Unknown setting: '{key}'" });
            }
        }

        try
        {
            var updated = await siteSettingsService.UpdateRuntimeConfigAsync(dbUpdates, principal.UserId);

            // Audit each changed setting
            foreach (var (key, value) in dbUpdates)
            {
                await auditService.RecordEventAsync(
                    principal.UserId,
                    "settings.updated",
                    "site_setting",
                    key,
                    new { key, newValue = value });
            }

            return Results.Ok(new
            {
                runtime = new RuntimeSettings
                {
                    DefaultTimezone = updated.DefaultTimezone,
                    WorkingHoursStart = updated.WorkingHoursStart,
                    WorkingHoursEnd = updated.WorkingHoursEnd,
                    HolidayProviderEnabled = updated.HolidayProviderEnabled,
                    BrandingName = updated.BrandingName,
                    BrandingLogoUrl = updated.BrandingLogoUrl,
                },
                updatedKeys = dbUpdates.Keys.ToList(),
            });
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}

// ── Response / Request DTOs ──────────────────────────────────────────────

public sealed class AdminSettingsResponse
{
    public required RuntimeSettings Runtime { get; init; }
    public required DeploymentSettings Deployment { get; init; }
    public required SystemInfo SystemInfo { get; init; }
}

public sealed class RuntimeSettings
{
    public required string DefaultTimezone { get; init; }
    public required string WorkingHoursStart { get; init; }
    public required string WorkingHoursEnd { get; init; }
    public required bool HolidayProviderEnabled { get; init; }
    public required string BrandingName { get; init; }
    public required string BrandingLogoUrl { get; init; }
}

public sealed class DeploymentSettings
{
    public required string PublicUrl { get; init; }
    public required string AuthPublicUrl { get; init; }
    public required string SmtpHost { get; init; }
    public required int SmtpPort { get; init; }
    public required string KeycloakRealm { get; init; }
    public required string FileStoragePath { get; init; }
    public required string LogLevel { get; init; }
}

public sealed class SystemInfo
{
    public required string Version { get; init; }
    public required string DatabaseStatus { get; init; }
    public required bool SmtpConfigured { get; init; }
    public required string AuthProvider { get; init; }
    public required string AuthRealm { get; init; }
}

public sealed class UpdateSettingsRequest
{
    public required Dictionary<string, string> Settings { get; init; }
}
