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

    /// <summary>Maximum length for site names</summary>
    public const int SiteNameMaxLength = 200;

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

    /// <summary>Maximum length for resource group names</summary>
    public const int ResourceGroupNameMaxLength = 255;

    /// <summary>Maximum length for resource group descriptions</summary>
    public const int ResourceGroupDescriptionMaxLength = 1000;

    /// <summary>Maximum length for preset IDs</summary>
    public const int PresetIdMaxLength = 100;

    /// <summary>Maximum length for preset descriptions</summary>
    public const int PresetDescriptionMaxLength = 1000;

    /// <summary>Maximum length for preset vendor field</summary>
    public const int PresetVendorMaxLength = 100;

    /// <summary>Maximum length for preset industry field</summary>
    public const int PresetIndustryMaxLength = 100;

    /// <summary>Maximum length for feedback titles</summary>
    public const int FeedbackTitleMaxLength = 200;
}
