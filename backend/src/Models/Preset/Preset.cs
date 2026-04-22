using System.Text.Json.Serialization;

namespace Api.Models.Preset;

/// <summary>
/// A Preset is a portable, versioned bundle of tenant configuration that can be
/// imported/exported and applied to pre-configure a tenant with criteria, groups, and templates.
/// </summary>
public record Preset
{
    /// <summary>
    /// Unique identifier for this preset (e.g., "manufacturing-ch-v1").
    /// Used for idempotent application tracking.
    /// </summary>
    public required string PresetId { get; init; }

    /// <summary>
    /// Human-readable name (e.g., "Manufacturing (Switzerland)").
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Optional description explaining what this preset provides.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Optional vendor/author identifier (e.g., "internal", "partner-xyz").
    /// </summary>
    public string? Vendor { get; init; }

    /// <summary>
    /// Optional industry tag (e.g., "manufacturing", "healthcare", "education").
    /// </summary>
    public string? Industry { get; init; }

    /// <summary>
    /// Schema version of this preset file format (e.g., "1.0.0").
    /// Used for migration-on-import.
    /// </summary>
    public required string Version { get; init; }

    /// <summary>
    /// Minimum application version required to apply this preset.
    /// </summary>
    public string? MinAppVersion { get; init; }

    /// <summary>
    /// When this preset was created/exported.
    /// </summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>
    /// The actual content of the preset.
    /// </summary>
    public required PresetContents Contents { get; init; }
}

/// <summary>
/// The contents of a preset, containing all entities to be created.
/// </summary>
public record PresetContents
{
    /// <summary>
    /// Criteria definitions to create.
    /// </summary>
    public List<PresetCriterion> Criteria { get; init; } = new();

    /// <summary>
    /// Space groups to create.
    /// </summary>
    public List<PresetSpaceGroup> SpaceGroups { get; init; } = new();

    /// <summary>
    /// Templates organized by entity type.
    /// </summary>
    public PresetTemplates Templates { get; init; } = new();
}

/// <summary>
/// Templates organized by entity type (space, group, request).
/// </summary>
public record PresetTemplates
{
    public List<PresetTemplate> Space { get; init; } = new();
    public List<PresetTemplate> Group { get; init; } = new();
    public List<PresetTemplate> Request { get; init; } = new();
}

/// <summary>
/// A criterion definition within a preset.
/// Uses a logical key for referencing instead of database IDs.
/// </summary>
public record PresetCriterion
{
    /// <summary>
    /// Logical key for this criterion within the preset (e.g., "shift-model").
    /// Used for internal references and idempotent application.
    /// </summary>
    public required string Key { get; init; }

    /// <summary>
    /// Display name for the criterion.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Optional description.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Data type: Boolean, Number, String, or Enum.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public required CriterionDataType DataType { get; init; }

    /// <summary>
    /// Allowed values for Enum type criteria.
    /// </summary>
    public List<string>? EnumValues { get; init; }

    /// <summary>
    /// Unit for Number type criteria (e.g., "kg", "kW").
    /// </summary>
    public string? Unit { get; init; }
}

/// <summary>
/// A space group definition within a preset.
/// </summary>
public record PresetSpaceGroup
{
    /// <summary>
    /// Logical key for this group within the preset (e.g., "production-hall-1").
    /// </summary>
    public required string Key { get; init; }

    /// <summary>
    /// Display name for the group.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Optional description.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Optional hex color (#RRGGBB).
    /// </summary>
    public string? Color { get; init; }

    /// <summary>
    /// Display order (lower = first).
    /// </summary>
    public int DisplayOrder { get; init; } = 0;
}

/// <summary>
/// A template definition within a preset.
/// </summary>
public record PresetTemplate
{
    /// <summary>
    /// Logical key for this template within the preset (e.g., "workstation").
    /// </summary>
    public required string Key { get; init; }

    /// <summary>
    /// Display name for the template.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Optional description.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Default duration value for request templates.
    /// </summary>
    public int? DurationValue { get; init; }

    /// <summary>
    /// Duration unit (hours, days, weeks).
    /// </summary>
    public string? DurationUnit { get; init; }

    /// <summary>
    /// Whether the start time is fixed.
    /// </summary>
    public bool FixedStart { get; init; } = false;

    /// <summary>
    /// Whether the end time is fixed.
    /// </summary>
    public bool FixedEnd { get; init; } = false;

    /// <summary>
    /// Whether the duration is fixed.
    /// </summary>
    public bool FixedDuration { get; init; } = true;

    /// <summary>
    /// Template items (criteria with values).
    /// References criteria by their logical key within the preset.
    /// </summary>
    public List<PresetTemplateItem> Items { get; init; } = new();
}

/// <summary>
/// A template item binding a criterion to a value.
/// </summary>
public record PresetTemplateItem
{
    /// <summary>
    /// Reference to a criterion by its logical key (e.g., "shift-model").
    /// </summary>
    public required string CriterionKey { get; init; }

    /// <summary>
    /// The value as a JSON string (e.g., "\"2-shift\"" or "42" or "true").
    /// </summary>
    public required string Value { get; init; }
}
