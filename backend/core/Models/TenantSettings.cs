namespace Api.Models;

/// <summary>
/// Strongly-typed settings with compiled defaults.
/// Site-scoped values are overridden via the control_plane.site_settings table.
/// Tenant-scoped values are overridden per-tenant via the tenant_settings table.
/// The <see cref="Api.Services.ITenantSettingsService"/> applies DB overrides on top of these defaults.
/// </summary>
public sealed record TenantSettings
{
    // ── Security — Password Policy ──────────────────────────────────────
    /// <summary>Global minimum password length used when no tenant context is available (e.g. registration).</summary>
    public const int DefaultPasswordMinLength = 8;

    /// <summary>Minimum password length for account creation, invitation acceptance, and password change.</summary>
    public int PasswordMinLength { get; init; } = DefaultPasswordMinLength;

    // ── Security — Brute Force ──────────────────────────────────────────
    /// <summary>Failed login attempts before the account is temporarily locked.</summary>
    public int BruteForce_LockoutThreshold { get; init; } = 10;

    /// <summary>Initial lockout duration in minutes (doubles on each consecutive lockout).</summary>
    public int BruteForce_BaseLockoutMinutes { get; init; } = 15;

    /// <summary>Maximum lockout duration cap in minutes.</summary>
    public int BruteForce_MaxLockoutMinutes { get; init; } = 120;

    /// <summary>Rolling window in minutes within which failed attempts are counted.</summary>
    public int BruteForce_FailureWindowMinutes { get; init; } = 30;

    // ── Security — Rate Limiting ────────────────────────────────────────
    /// <summary>Login attempts per minute per IP address.</summary>
    public int RateLimit_LoginPerMinute { get; init; } = 5;

    /// <summary>Registration attempts per hour per IP address.</summary>
    public int RateLimit_RegisterPerHour { get; init; } = 3;

    /// <summary>Default API requests per minute per user.</summary>
    public int RateLimit_DefaultPerMinute { get; init; } = 60;

    /// <summary>Write-heavy operation requests per minute per user.</summary>
    public int RateLimit_WritePerMinute { get; init; } = 10;

    // ── Invitations ─────────────────────────────────────────────────────
    /// <summary>Number of days before an invitation link expires.</summary>
    public int Invitation_ExpiryDays { get; init; } = 7;

    // ── Uploads ─────────────────────────────────────────────────────────
    /// <summary>Maximum floorplan upload size in megabytes.</summary>
    public int Upload_MaxFileSizeMb { get; init; } = 10;

    /// <summary>Comma-separated list of allowed MIME types for floorplan uploads.</summary>
    public string Upload_AllowedMimeTypes { get; init; } = "image/png,image/jpeg,image/jpg";

    // ── Search ──────────────────────────────────────────────────────────
    /// <summary>Default number of search results returned.</summary>
    public int Search_DefaultPageSize { get; init; } = 20;

    /// <summary>Trigram similarity threshold (0.0–1.0) for primary search matches.</summary>
    public double Search_PrimarySimilarityThreshold { get; init; } = 0.2;

    /// <summary>Trigram similarity threshold (0.0–1.0) for secondary/broader matches.</summary>
    public double Search_SecondarySimilarityThreshold { get; init; } = 0.15;

    // ── Branding ────────────────────────────────────────────────────────
    /// <summary>Product/organization name used in email templates.</summary>
    public string Branding_ProductName { get; init; } = "Orkyo";

    /// <summary>Primary brand color (hex) used in email templates.</summary>
    public string Branding_PrimaryColor { get; init; } = "#667eea";

    /// <summary>Secondary brand color (hex) used in email gradient.</summary>
    public string Branding_SecondaryColor { get; init; } = "#764ba2";

    // ── Scheduling ───────────────────────────────────────────────────────
    /// <summary>Whether auto-scheduling is enabled for this tenant. Requires Professional tier or above.</summary>
    public bool AutoSchedule_Enabled { get; init; } = false;

    /// <summary>Project branding values into an <see cref="Api.Services.EmailBranding"/> record.</summary>
    public Services.EmailBranding ToEmailBranding() =>
        new(Branding_ProductName, Branding_PrimaryColor, Branding_SecondaryColor);
}

/// <summary>
/// Metadata for a single setting: category, display info, and validation bounds.
/// Used by the settings API to return discoverable settings to the frontend.
/// </summary>
public sealed record TenantSettingDescriptor(
    string Key,
    string Category,
    string DisplayName,
    string Description,
    string ValueType,   // "int", "double", "string"
    string DefaultValue,
    /// <summary>
    /// "site" — infrastructure / security baseline; only site-admins can modify.
    /// "tenant" — per-tenant customization; tenant-admins (and site-admins) can modify.
    /// </summary>
    string Scope = "tenant",
    string? MinValue = null,
    string? MaxValue = null
);
