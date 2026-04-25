using Orkyo.Shared;

namespace Api.Configuration;

public static class ConfigurationValidator
{
    public static List<string> Validate(IConfiguration configuration, string? environmentName = null)
    {
        var errors = new List<string>();

        var env = environmentName ?? configuration[ConfigKeys.AspNetCoreEnvironment] ?? EnvironmentNames.Production;
        var allowTenantHeader = configuration.GetValue<bool>(ConfigKeys.TenantResolutionAllowTenantHeader);
        if (allowTenantHeader && string.Equals(env, EnvironmentNames.Production, StringComparison.OrdinalIgnoreCase))
            errors.Add("TenantResolution:AllowTenantHeader must NOT be true in Production (tenant impersonation risk)");

        foreach (var key in DeploymentConfig.RequiredKeys)
        {
            if (string.IsNullOrEmpty(configuration[key]))
                errors.Add($"Required configuration '{key}' is not set");
        }

        if (errors.Count == 0)
        {
            try { DeploymentConfig.FromConfiguration(configuration); }
            catch (Exception ex) when (ex is not OutOfMemoryException) { errors.Add(ex.Message); }
        }

        return errors;
    }

    public static void ValidateOrThrow(IConfiguration configuration, string? environmentName = null)
    {
        var errors = Validate(configuration, environmentName);
        if (errors.Count > 0)
            throw new InvalidOperationException(
                "Configuration validation failed:\n" + string.Join("\n", errors.Select(e => $"  - {e}")));
    }

    public static void LogConfigurationStatus(IConfiguration configuration, ILogger logger)
    {
        var environment = configuration[ConfigKeys.AspNetCoreEnvironment] ?? EnvironmentNames.Production;
        logger.LogInformation("Configuration Status (Environment: {Environment})", environment);

        foreach (var key in DeploymentConfig.RequiredKeys)
            logger.LogDebug("  {Key}: {Status}", key, !string.IsNullOrEmpty(configuration[key]) ? "✓" : "✗ MISSING");

        var sensitiveKeys = new[] { ConfigKeys.SmtpPassword, ConfigKeys.KeycloakBackendClientSecret };
        foreach (var key in sensitiveKeys)
            logger.LogDebug("  {Key}: {Status}", key, !string.IsNullOrEmpty(configuration[key]) ? "configured" : "not set");
    }
}
