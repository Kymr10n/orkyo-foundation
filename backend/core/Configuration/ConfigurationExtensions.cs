namespace Api.Configuration;

/// <summary>
/// Extension methods for IConfiguration.
/// All configuration must come from .env - no hardcoded defaults.
/// </summary>
public static class ConfigurationExtensions
{
    /// <summary>
    /// Gets a required configuration value. Throws if not set.
    /// </summary>
    public static string GetRequired(this IConfiguration configuration, string key)
    {
        var value = configuration[key];
        if (string.IsNullOrEmpty(value))
        {
            throw new InvalidOperationException($"Required configuration '{key}' is not set");
        }
        return value;
    }

    /// <summary>
    /// Gets a required configuration value as int. Throws if not set or invalid.
    /// </summary>
    public static int GetRequiredInt(this IConfiguration configuration, string key)
    {
        var value = configuration.GetRequired(key);
        if (!int.TryParse(value, out var result))
        {
            throw new InvalidOperationException($"Configuration '{key}' must be a valid integer, got: '{value}'");
        }
        return result;
    }

    /// <summary>
    /// Gets a required configuration value as bool. Throws if not set or invalid.
    /// </summary>
    public static bool GetRequiredBool(this IConfiguration configuration, string key)
    {
        var value = configuration.GetRequired(key);
        if (!bool.TryParse(value, out var result))
        {
            throw new InvalidOperationException($"Configuration '{key}' must be 'true' or 'false', got: '{value}'");
        }
        return result;
    }

    /// <summary>
    /// Gets an optional string value (returns empty string if not set).
    /// Only use for legitimately optional values like credentials that may be empty.
    /// </summary>
    public static string GetOptionalString(this IConfiguration configuration, string key)
    {
        return configuration[key] ?? "";
    }

    /// <summary>
    /// Checks if a configuration value is set (non-empty).
    /// </summary>
    public static bool IsSet(this IConfiguration configuration, string key)
    {
        return !string.IsNullOrEmpty(configuration[key]);
    }
}
