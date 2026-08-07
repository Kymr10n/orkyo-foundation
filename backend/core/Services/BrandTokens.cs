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

    /// <summary>
    /// Primary ACTION color (buttons, link fallbacks) — the marketing/app indigo
    /// (site.css .cta-button / .nav-signin). Deliberately distinct from the header chrome
    /// gradient: using the chrome color for buttons made every CTA near-black, which
    /// disappeared entirely when email clients auto-darkened the light card. A saturated
    /// mid-tone also survives client dark-mode inversion, which remaps near-white and
    /// near-black but leaves colors like this alone.
    /// </summary>
    public const string Accent = "#3B5BDB";

    /// <summary>Primary body text color.</summary>
    public const string Text = "#020817";

    /// <summary>Secondary / muted text color.</summary>
    public const string MutedText = "#64748b";

    /// <summary>Light panel background (email body card, marketing section tint).</summary>
    public const string PanelBg = "#f1f5f9";

    /// <summary>Hairline border color.</summary>
    public const string Border = "#e2e8f0";

    // ── Dark-mode counterparts ────────────────────────────────────────────────
    // Used only inside the emails' `@media (prefers-color-scheme: dark)` block, so a
    // client that honours the switch (Apple Mail, Thunderbird, Outlook macOS) renders a
    // designed dark email instead of an auto-inverted light one. Values are the app's own
    // dark surfaces, identical to the marketing shell's dark identity.

    /// <summary>Dark-mode page background (app dark ground).</summary>
    public const string DarkBodyBg = "#0c0d0f";

    /// <summary>Dark-mode card background (elevated dark surface).</summary>
    public const string DarkPanelBg = "#14171c";

    /// <summary>Dark-mode primary body text.</summary>
    public const string DarkText = "#c8ccd4";

    /// <summary>Dark-mode secondary / muted text.</summary>
    public const string DarkMutedText = "#8a8f99";

    /// <summary>Dark-mode hairline border.</summary>
    public const string DarkBorder = "#2a2e36";

    /// <summary>Dark-mode heading text.</summary>
    public const string DarkHeading = "#eceef2";

    /// <summary>
    /// Font stack, identical to the marketing body font (site.css uses double quotes around
    /// Segoe UI; single quotes here because the value is embedded in double-quoted HTML style
    /// attributes — the sync test compares quote-normalized).
    /// </summary>
    public const string FontStack = "Inter, ui-sans-serif, system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif";
}
