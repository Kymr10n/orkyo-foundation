namespace Api.Models.Preset;

/// <summary>
/// Metadata describing a starter template option shown to users during tenant creation.
/// This is a cross-product contract: product compositions may present these templates,
/// but the catalog definition itself belongs to the shared preset domain.
/// </summary>
public record StarterTemplateInfo
{
    public required string Key { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string Icon { get; init; }
    public bool IncludesDemoData { get; init; }
}
