using Orkyo.Shared;

namespace Api.Configuration;

public static class ConfigurationValidator
{
    /// <summary>
    /// Null when the IANA time zone database is available, otherwise the error text.
    /// The Alpine runtime images carry no tzdata unless the Dockerfile installs it, and
    /// SchedulingEngine resolves every site's time zone through TimeZoneInfo — without
    /// the database each scheduled-request write throws TimeZoneNotFoundException.
    /// Called by Validate() (which runs at API startup and in the deploy pipeline's
    /// --validate gate, before cutover) and by both editions' workers at startup.
    /// </summary>
    public static string? TimeZoneDataError()
        => TimeZoneInfo.TryFindSystemTimeZoneById("Europe/Berlin", out _)
            ? null
            : "Time zone database not available: cannot resolve 'Europe/Berlin'. "
              + "The runtime image must include the tzdata package.";

    public static List<string> Validate(IConfiguration configuration, string? environmentName = null)
    {
        var errors = new List<string>();

        if (TimeZoneDataError() is { } timeZoneError)
            errors.Add(timeZoneError);

        // No fallback: an unknown environment must be an error, not a silent assumption.
        // Every deployment surface sets ASPNETCORE_ENVIRONMENT (both infra templates,
        // community release and local compose), so an empty value here is a broken config.
        var env = environmentName ?? configuration[ConfigKeys.AspNetCoreEnvironment];
        if (string.IsNullOrEmpty(env))
            errors.Add($"Required configuration '{ConfigKeys.AspNetCoreEnvironment}' is not set and no environment name was supplied");
        var allowTenantHeader = configuration.GetValue<bool>(ConfigKeys.TenantResolutionAllowTenantHeader);
        if (allowTenantHeader && string.Equals(env, EnvironmentNames.Production, StringComparison.OrdinalIgnoreCase))
            errors.Add("TenantResolution:AllowTenantHeader must NOT be true in Production (tenant impersonation risk)");

        foreach (var key in DeploymentConfig.RequiredKeys)
        {
            if (string.IsNullOrEmpty(configuration[key]))
                errors.Add($"Required configuration '{key}' is not set");
        }

        // Mail is optional as a block: no SMTP_HOST selects the log-only transport. Once the
        // host is set the rest of the block must be complete, so a deployment that means to
        // send mail fails at startup instead of falling back to a sender nobody chose.
        if (!string.IsNullOrEmpty(configuration[ConfigKeys.SmtpHost]))
        {
            foreach (var key in DeploymentConfig.SmtpKeysRequiredWithHost)
            {
                if (string.IsNullOrEmpty(configuration[key]))
                    errors.Add($"Required configuration '{key}' is not set when '{ConfigKeys.SmtpHost}' is set");
            }
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
}
