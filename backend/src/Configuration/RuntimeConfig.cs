using System.Text.RegularExpressions;

namespace Api.Configuration;

/// <summary>
/// Runtime-configurable settings sourced from a key/value <c>site_settings</c>
/// store.  These can be changed by site-admins without restarting the
/// application.  Defaults are compiled here and overridden by stored values at
/// load time.  The DB keys follow the pattern <c>category.snake_case_name</c>.
///
/// The shape and key vocabulary are identical for both multi-tenant SaaS and
/// single-tenant Community deployments, so the type lives in foundation.
/// </summary>
public sealed record RuntimeConfig
{
    // ── General ──────────────────────────────────────────────────────────
    public string DefaultTimezone { get; init; } = "UTC";

    // ── Scheduling ───────────────────────────────────────────────────────
    public string WorkingHoursStart { get; init; } = "08:00";
    public string WorkingHoursEnd { get; init; } = "18:00";
    public bool HolidayProviderEnabled { get; init; } = false;

    // ── Branding ─────────────────────────────────────────────────────────
    public string BrandingName { get; init; } = "Orkyo";
    public string BrandingLogoUrl { get; init; } = "";

    // ── DB key mapping ───────────────────────────────────────────────────

    /// <summary>
    /// Map of DB key → property name.  Used by site-settings services to
    /// apply overrides from the database.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> KeyMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["general.default_timezone"] = nameof(DefaultTimezone),
            ["scheduling.working_hours_start"] = nameof(WorkingHoursStart),
            ["scheduling.working_hours_end"] = nameof(WorkingHoursEnd),
            ["scheduling.holiday_provider_enabled"] = nameof(HolidayProviderEnabled),
            ["branding.branding_name"] = nameof(BrandingName),
            ["branding.branding_logo_url"] = nameof(BrandingLogoUrl),
        };

    /// <summary>
    /// Reverse map: property name → DB key.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> PropertyToKeyMap =
        KeyMap.ToDictionary(kvp => kvp.Value, kvp => kvp.Key, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Map of DB key → category (the part before the first dot).
    /// </summary>
    public static string CategoryForKey(string dbKey) =>
        dbKey.Contains('.') ? dbKey[..dbKey.IndexOf('.')] : "general";

    /// <summary>
    /// Apply database overrides on top of compiled defaults.
    /// Unknown keys are silently ignored (forward-compatible).
    /// </summary>
    public static RuntimeConfig ApplyOverrides(Dictionary<string, string> overrides)
    {
        if (overrides.Count == 0) return Defaults;

        var config = new RuntimeConfig();
        foreach (var (dbKey, value) in overrides)
        {
            if (!KeyMap.TryGetValue(dbKey, out var propName)) continue;

            config = propName switch
            {
                nameof(DefaultTimezone) => config with { DefaultTimezone = value },
                nameof(WorkingHoursStart) => config with { WorkingHoursStart = value },
                nameof(WorkingHoursEnd) => config with { WorkingHoursEnd = value },
                nameof(HolidayProviderEnabled) => config with { HolidayProviderEnabled = bool.TryParse(value, out var b) && b },
                nameof(BrandingName) => config with { BrandingName = value },
                nameof(BrandingLogoUrl) => config with { BrandingLogoUrl = value },
                _ => config,
            };
        }

        return config;
    }

    /// <summary>Singleton compiled defaults (never mutated).</summary>
    public static readonly RuntimeConfig Defaults = new();

    /// <summary>Maximum length (chars) for any single setting value.</summary>
    public const int MaxValueLength = 500;

    /// <summary>Maximum length (chars) for the branding name.</summary>
    public const int MaxBrandingNameLength = 100;

    private static readonly Regex HtmlTagPattern = new(@"<[^>]+>", RegexOptions.Compiled);

    /// <summary>
    /// Validates a single setting key/value pair against the per-property
    /// rules.  Throws <see cref="ArgumentException"/> on failure.
    /// </summary>
    public static void ValidateValue(string key, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"Setting '{key}' cannot be empty");

        if (value.Length > MaxValueLength)
            throw new ArgumentException($"Setting '{key}' value exceeds maximum length of {MaxValueLength} characters");

        if (!KeyMap.TryGetValue(key, out var propName))
            throw new ArgumentException($"Unknown runtime config key: '{key}'");

        switch (propName)
        {
            case nameof(HolidayProviderEnabled):
                if (!bool.TryParse(value, out _))
                    throw new ArgumentException($"Setting '{key}' must be 'true' or 'false'");
                break;

            case nameof(WorkingHoursStart):
            case nameof(WorkingHoursEnd):
                if (!TimeOnly.TryParse(value, out _))
                    throw new ArgumentException($"Setting '{key}' must be a valid time (e.g. '08:00', '18:30')");
                break;

            case nameof(BrandingName):
                if (value.Length > MaxBrandingNameLength)
                    throw new ArgumentException($"Setting '{key}' must not exceed {MaxBrandingNameLength} characters");
                if (HtmlTagPattern.IsMatch(value))
                    throw new ArgumentException($"Setting '{key}' must not contain HTML tags");
                break;

            case nameof(BrandingLogoUrl):
                if (!string.IsNullOrEmpty(value) &&
                    !Uri.TryCreate(value, UriKind.Absolute, out _))
                    throw new ArgumentException($"Setting '{key}' must be a valid URL");
                break;
        }
    }
}
