namespace Api.Models.Preset;

/// <summary>
/// Tracks which presets have been applied to a tenant and maps logical keys to database IDs.
/// This enables idempotent application - re-applying a preset updates existing entities
/// rather than creating duplicates.
/// </summary>
public record PresetApplication
{
    /// <summary>
    /// Database ID of this application record.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// The preset ID that was applied (e.g., "manufacturing-ch-v1").
    /// </summary>
    public required string PresetId { get; init; }

    /// <summary>
    /// The version of the preset that was applied.
    /// </summary>
    public required string PresetVersion { get; init; }

    /// <summary>
    /// When this preset was first applied.
    /// </summary>
    public DateTime AppliedAt { get; init; }

    /// <summary>
    /// When this preset was last re-applied (updated).
    /// </summary>
    public DateTime? UpdatedAt { get; init; }

    /// <summary>
    /// User who applied/updated this preset.
    /// </summary>
    public Guid? AppliedByUserId { get; init; }
}

/// <summary>
/// Maps a logical key from a preset to a database entity ID.
/// Enables idempotent updates on re-application.
/// </summary>
public record PresetMapping
{
    /// <summary>
    /// Database ID of this mapping record.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Reference to the preset application.
    /// </summary>
    public Guid PresetApplicationId { get; init; }

    /// <summary>
    /// The entity type (criterion, space_group, template).
    /// </summary>
    public required string EntityType { get; init; }

    /// <summary>
    /// The logical key from the preset (e.g., "shift-model").
    /// </summary>
    public required string LogicalKey { get; init; }

    /// <summary>
    /// The database ID of the created/updated entity.
    /// </summary>
    public Guid EntityId { get; init; }

    /// <summary>
    /// When this mapping was created.
    /// </summary>
    public DateTime CreatedAt { get; init; }
}

/// <summary>
/// Entity types that can be managed by presets.
/// </summary>
public static class PresetEntityType
{
    public const string Criterion = "criterion";
    public const string SpaceGroup = "space_group";
    public const string TemplateSpace = "template_space";
    public const string TemplateGroup = "template_group";
    public const string TemplateRequest = "template_request";
    public const string TemplateItem = "template_item";
}
