namespace Api.Services;

/// <summary>
/// Canonical Orkyo brand tokens, shared between the email templates (which must inline all
/// styling — email clients strip external CSS) and the marketing site
/// (orkyo-saas/frontend/marketing/site.css, deliberately zero-build). The two sides are kept
/// in sync by MarketingBrandSyncTests in orkyo-saas rather than a build step: update both
/// together or that test fails.
///
/// These are the PRODUCT defaults. Emails stay per-tenant brandable via
/// TenantSettings.Branding_* overrides; only the defaults and the fixed chrome come from here.
/// </summary>
public static class BrandTokens
{
    /// <summary>Header gradient start — matches the marketing header gradient (elevated dark surface).</summary>
    public const string GradientFrom = "#14171c";

    /// <summary>Header gradient end — matches the marketing header gradient (app dark background).</summary>
    public const string GradientTo = "#0c0d0f";

    /// <summary>Primary body text color.</summary>
    public const string Text = "#020817";

    /// <summary>Secondary / muted text color.</summary>
    public const string MutedText = "#64748b";

    /// <summary>Light panel background (email body card, marketing section tint).</summary>
    public const string PanelBg = "#f1f5f9";

    /// <summary>Hairline border color.</summary>
    public const string Border = "#e2e8f0";

    /// <summary>
    /// Font stack, identical to the marketing body font (site.css uses double quotes around
    /// Segoe UI; single quotes here because the value is embedded in double-quoted HTML style
    /// attributes — the sync test compares quote-normalized).
    /// </summary>
    public const string FontStack = "Inter, ui-sans-serif, system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif";
}
