namespace Api.Models.Preset;

/// <summary>
/// Shared catalog of starter templates and preset-backed template mappings.
/// Product compositions may choose how to apply a template, but the available
/// template definitions and stable keys are part of the shared preset core.
/// </summary>
public static class StarterTemplateCatalog
{
    public const string Empty = "empty";
    public const string Demo = "demo";
    public const string CampingSite = "camping-site";
    public const string ConstructionSite = "construction-site";
    public const string Manufacturing = "manufacturing";

    private static readonly IReadOnlyList<StarterTemplateInfo> Templates =
    [
        new()
        {
            Key = Empty,
            Name = "Empty",
            Description = "Start with a clean slate. Configure everything from scratch.",
            Icon = "file-plus",
            IncludesDemoData = false
        },
        new()
        {
            Key = Demo,
            Name = "Demo",
            Description = "Full demo with sample sites, spaces, requests, and a floorplan image.",
            Icon = "layout-dashboard",
            IncludesDemoData = true
        },
        new()
        {
            Key = CampingSite,
            Name = "Camping Site",
            Description = "Pre-configured for camping and outdoor recreation sites.",
            Icon = "tent",
            IncludesDemoData = false
        },
        new()
        {
            Key = ConstructionSite,
            Name = "Construction Site",
            Description = "Pre-configured for construction and building projects.",
            Icon = "hard-hat",
            IncludesDemoData = false
        },
        new()
        {
            Key = Manufacturing,
            Name = "Manufacturing",
            Description = "Pre-configured for manufacturing and production facilities.",
            Icon = "factory",
            IncludesDemoData = false
        }
    ];

    public static IReadOnlyList<StarterTemplateInfo> All => Templates;

    public static bool IsKnown(string templateKey) => Templates.Any(t => t.Key == templateKey);

    public static bool IsDemoTemplate(string templateKey) => string.Equals(templateKey, Demo, StringComparison.Ordinal);

    public static bool IsPresetTemplate(string templateKey) =>
        string.Equals(templateKey, CampingSite, StringComparison.Ordinal) ||
        string.Equals(templateKey, ConstructionSite, StringComparison.Ordinal) ||
        string.Equals(templateKey, Manufacturing, StringComparison.Ordinal);

    public static bool TryGetPresetFileName(string templateKey, out string? fileName)
    {
        fileName = templateKey switch
        {
            CampingSite => "camping-site.preset.json",
            ConstructionSite => "construction-site.preset.json",
            Manufacturing => "manufacturing-ch.preset.json",
            _ => null
        };

        return fileName != null;
    }
}
