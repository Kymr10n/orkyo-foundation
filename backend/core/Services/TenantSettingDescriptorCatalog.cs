using Api.Models;

namespace Api.Services;

/// <summary>
/// Canonical catalog of <see cref="TenantSettingDescriptor"/> entries describing
/// every known tenant/site setting. The catalog is platform metadata and is
/// shared across product compositions.
///
/// Composition layers filter this catalog by <see cref="TenantSettingDescriptor.Scope"/>
/// (e.g. "site" vs "tenant") according to their context.
/// </summary>
public static class TenantSettingDescriptorCatalog
{
    private static readonly IReadOnlyList<TenantSettingDescriptor> _all = BuildDescriptors();
    private static readonly HashSet<string> _siteKeys = _all
        .Where(d => d.Scope == "site")
        .Select(d => d.Key)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>All descriptors regardless of scope.</summary>
    public static IReadOnlyList<TenantSettingDescriptor> All => _all;

    /// <summary>Set of keys that belong to the "site" scope.</summary>
    public static IReadOnlySet<string> SiteKeys => _siteKeys;

    /// <summary>Descriptors filtered to only those with <c>Scope == "site"</c>.</summary>
    public static IReadOnlyList<TenantSettingDescriptor> SiteScope { get; } =
        _all.Where(d => d.Scope == "site").ToList();

    /// <summary>Descriptors filtered to only those with <c>Scope == "tenant"</c>.</summary>
    public static IReadOnlyList<TenantSettingDescriptor> TenantScope { get; } =
        _all.Where(d => d.Scope == "tenant").ToList();

    /// <summary>Case-insensitive lookup map by descriptor key.</summary>
    public static IReadOnlyDictionary<string, TenantSettingDescriptor> ByKey { get; } =
        _all.ToDictionary(d => d.Key, StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyList<TenantSettingDescriptor> BuildDescriptors()
    {
        var d = TenantSettingsKeyPolicy.DefaultsMap;

        return new List<TenantSettingDescriptor>
        {
            // Security — Password  (site-admin: platform security baseline)
            new("security.password_min_length", "security", "Minimum Password Length",
                "Minimum number of characters required for passwords.", "int",
                d["security.password_min_length"], Scope: "site", MinValue: "6", MaxValue: "128"),

            // Security — Brute Force  (site-admin: platform security baseline)
            new("security.brute_force_lockout_threshold", "security", "Lockout Threshold",
                "Number of failed login attempts before temporary lockout.", "int",
                d["security.brute_force_lockout_threshold"], Scope: "site", MinValue: "3", MaxValue: "50"),
            new("security.brute_force_base_lockout_minutes", "security", "Base Lockout Duration (min)",
                "Initial lockout duration in minutes. Doubles on each consecutive lockout.", "int",
                d["security.brute_force_base_lockout_minutes"], Scope: "site", MinValue: "1", MaxValue: "60"),
            new("security.brute_force_max_lockout_minutes", "security", "Max Lockout Duration (min)",
                "Maximum lockout duration cap in minutes.", "int",
                d["security.brute_force_max_lockout_minutes"], Scope: "site", MinValue: "15", MaxValue: "1440"),
            new("security.brute_force_failure_window_minutes", "security", "Failure Window (min)",
                "Rolling window in minutes within which failed attempts are counted.", "int",
                d["security.brute_force_failure_window_minutes"], Scope: "site", MinValue: "5", MaxValue: "120"),

            // Security — Rate Limiting  (site-admin: infrastructure concern)
            new("security.rate_limit_login_per_minute", "security", "Login Rate Limit (/min)",
                "Maximum login attempts per minute per IP address.", "int",
                d["security.rate_limit_login_per_minute"], Scope: "site", MinValue: "2", MaxValue: "30"),
            new("security.rate_limit_register_per_hour", "security", "Register Rate Limit (/hr)",
                "Maximum registration attempts per hour per IP address.", "int",
                d["security.rate_limit_register_per_hour"], Scope: "site", MinValue: "1", MaxValue: "20"),
            new("security.rate_limit_default_per_minute", "security", "API Rate Limit (/min)",
                "Default API requests per minute per authenticated user.", "int",
                d["security.rate_limit_default_per_minute"], Scope: "site", MinValue: "10", MaxValue: "600"),
            new("security.rate_limit_write_per_minute", "security", "Write Rate Limit (/min)",
                "Write-heavy operations per minute per user.", "int",
                d["security.rate_limit_write_per_minute"], Scope: "site", MinValue: "2", MaxValue: "100"),

            // Invitations  (site-admin: onboarding policy)
            new("invitations.invitation_expiry_days", "invitations", "Invitation Expiry (days)",
                "Number of days before an invitation link expires.", "int",
                d["invitations.invitation_expiry_days"], Scope: "site", MinValue: "1", MaxValue: "90"),

            // Uploads  (site-admin: infrastructure / storage limits)
            new("uploads.upload_max_file_size_mb", "uploads", "Max Upload Size (MB)",
                "Maximum floorplan upload file size in megabytes.", "int",
                d["uploads.upload_max_file_size_mb"], Scope: "site", MinValue: "1", MaxValue: "50"),
            new("uploads.upload_allowed_mime_types", "uploads", "Allowed File Types",
                "Comma-separated MIME types allowed for floorplan uploads.", "string",
                d["uploads.upload_allowed_mime_types"], Scope: "site"),

            // Search
            new("search.search_default_page_size", "search", "Default Results Per Page",
                "Default number of search results returned.", "int",
                d["search.search_default_page_size"], MinValue: "5", MaxValue: "100"),
            new("search.search_primary_similarity_threshold", "search", "Primary Similarity Threshold",
                "Trigram similarity threshold (0.0–1.0) for primary search matches.", "double",
                d["search.search_primary_similarity_threshold"], MinValue: "0.05", MaxValue: "0.8"),
            new("search.search_secondary_similarity_threshold", "search", "Secondary Similarity Threshold",
                "Trigram similarity threshold (0.0–1.0) for broader matches.", "double",
                d["search.search_secondary_similarity_threshold"], MinValue: "0.05", MaxValue: "0.8"),

            // Branding
            new("branding.branding_product_name", "branding", "Product Name",
                "Product or organization name used in email templates.", "string",
                d["branding.branding_product_name"]),
            new("branding.branding_primary_color", "branding", "Primary Brand Color",
                "Primary brand color (hex) used in email templates.", "string",
                d["branding.branding_primary_color"]),
            new("branding.branding_secondary_color", "branding", "Secondary Brand Color",
                "Secondary gradient color (hex) used in email templates.", "string",
                d["branding.branding_secondary_color"]),

            // Scheduling  (tenant-admin: feature toggle, requires Professional tier)
            new("scheduling.auto_schedule_enabled", "scheduling", "Auto-Schedule",
                "Enable the auto-schedule feature on the utilization page. Requires Professional tier or above.",
                "bool", d["scheduling.auto_schedule_enabled"]),
        };
    }
}
