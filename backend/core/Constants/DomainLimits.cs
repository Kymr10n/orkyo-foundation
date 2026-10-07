namespace Api.Constants;

/// <summary>
/// Maximum length constraints for string fields across the domain.
/// Centralized to ensure consistency and prevent magic numbers.
/// </summary>
public static class DomainLimits
{
    /// <summary>Maximum length for site codes. Stricter than sites.code VARCHAR(63) on purpose;
    /// the column keeps headroom for imported data.</summary>
    public const int SiteCodeMaxLength = 50;

    /// <summary>Maximum length for a placeable resource's code — matches resources.code VARCHAR(63).</summary>
    public const int ResourceCodeMaxLength = 63;

    /// <summary>Maximum length for a QR sticker code — matches the CHECK on resource_scan_codes.code.</summary>
    public const int ResourceScanCodeMaxLength = 512;

    /// <summary>Maximum length for site names</summary>
    public const int SiteNameMaxLength = 200;

    /// <summary>Maximum length for resource names</summary>
    public const int ResourceNameMaxLength = 200;

    /// <summary>Maximum length for request names</summary>
    public const int RequestNameMaxLength = 200;

    /// <summary>Maximum length for request icon IDs (lucide-react icon name).</summary>
    public const int RequestIconMaxLength = 64;

    /// <summary>Maximum length for criterion names (identifier format)</summary>
    public const int CriterionNameMaxLength = 100;

    /// <summary>Maximum length for criterion units</summary>
    public const int CriterionUnitMaxLength = 20;

    /// <summary>Maximum length for criterion descriptions</summary>
    public const int CriterionDescriptionMaxLength = 500;

    /// <summary>Maximum length for template names</summary>
    public const int TemplateNameMaxLength = 255;

    /// <summary>Maximum length for template descriptions</summary>
    public const int TemplateDescriptionMaxLength = 1000;

    /// <summary>routings.name varchar(255).</summary>
    public const int RoutingNameMaxLength = 255;

    /// <summary>routings.description; text in the schema, capped like a template's.</summary>
    public const int RoutingDescriptionMaxLength = 1000;

    /// <summary>Maximum length for resource group names</summary>
    public const int ResourceGroupNameMaxLength = 255;

    /// <summary>Maximum length for resource group descriptions</summary>
    public const int ResourceGroupDescriptionMaxLength = 1000;

    /// <summary>Maximum length for preset IDs</summary>
    public const int PresetIdMaxLength = 100;

    /// <summary>Maximum length for preset names</summary>
    public const int PresetNameMaxLength = 255;

    /// <summary>Maximum length for preset descriptions</summary>
    public const int PresetDescriptionMaxLength = 1000;

    /// <summary>Maximum length for preset vendor field</summary>
    public const int PresetVendorMaxLength = 100;

    /// <summary>Maximum length for preset industry field</summary>
    public const int PresetIndustryMaxLength = 100;

    /// <summary>Maximum length for feedback titles</summary>
    public const int FeedbackTitleMaxLength = 200;

    /// <summary>Maximum length for an email address — matches users.email VARCHAR(320).</summary>
    public const int EmailMaxLength = 320;

    /// <summary>A browser error message: one line of an exception, with room for a long URL in it.</summary>
    public const int ClientReportMessageMaxLength = 2000;

    /// <summary>A browser stack trace or React component stack, truncated by the frontend before sending.</summary>
    public const int ClientReportStackMaxLength = 8000;

    /// <summary>A route path as the frontend reports it (pathname only, never query or host).</summary>
    public const int ClientReportRouteMaxLength = 500;

    /// <summary>A package version string such as <c>1.4.0</c> or a nightly prerelease.</summary>
    public const int ClientReportReleaseMaxLength = 100;

    /// <summary>
    /// Maximum length for a first or a last name. The display name is "first last" in
    /// users.display_name VARCHAR(255), so each half gets 127.
    /// </summary>
    public const int PersonNamePartMaxLength = 127;

    /// <summary>Maximum length for an API access or reporting token name — matches both name columns, VARCHAR(255).</summary>
    public const int TokenNameMaxLength = 255;
}
