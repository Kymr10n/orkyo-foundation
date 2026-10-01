using System.Text.Json.Serialization;

namespace Api.Models.Preset;

/// <summary>
/// A Preset is a portable, versioned bundle of tenant configuration that can be
/// imported/exported and applied to pre-configure a tenant with resource types, criteria,
/// groups, templates and, for a starter setup, a few named sample resources.
/// </summary>
public record Preset
{
    /// <summary>
    /// Unique identifier for this preset (e.g., "manufacturing-workshop-v1").
    /// Used for idempotent application tracking.
    /// </summary>
    public required string PresetId { get; init; }

    /// <summary>
    /// Human-readable name (e.g., "Manufacturing workshop").
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
    /// Schema version of this preset file format (e.g., "1.1.0").
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
/// The contents of a preset, containing all entities to be created. Applied in dependency
/// order: resource types, criteria (with their applicability), groups, templates, resources.
/// Every section is optional, so a 1.0.0 file (criteria, groups, templates only) still loads.
/// </summary>
public record PresetContents
{
    /// <summary>
    /// Resource types the preset needs. Applied first; everything below refers to them by key.
    /// </summary>
    public List<PresetResourceType> ResourceTypes { get; init; } = new();

    /// <summary>
    /// Criteria definitions to create.
    /// </summary>
    public List<PresetCriterion> Criteria { get; init; } = new();

    /// <summary>
    /// Resource groups to create. Named "space groups" for compatibility with 1.0.0 files, where
    /// every group held a placeable type; with <see cref="PresetSpaceGroup.ResourceTypeKey"/> a
    /// group can hold any type in <see cref="ResourceTypes"/>.
    /// </summary>
    public List<PresetSpaceGroup> SpaceGroups { get; init; } = new();

    /// <summary>
    /// Templates organized by entity type.
    /// </summary>
    public PresetTemplates Templates { get; init; } = new();

    /// <summary>
    /// Named sample resources — a starter setup's rooms, people, machines and tools. Applied
    /// last, because each one refers to a type, groups and criteria above. Not part of an
    /// export: resources are data, and a preset export is configuration.
    /// </summary>
    public List<PresetResource> Resources { get; init; } = new();
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
/// A resource type the preset needs. A key that exists in the product's resource type catalog
/// is activated from the catalog spec, exactly as Configuration → Type catalog does, and the
/// flags and names in the file are ignored in its favour. Any other key is an ad-hoc type built
/// from the fields below, and then the display names are required.
/// </summary>
public record PresetResourceType
{
    /// <summary>
    /// Type key, in the catalog's spelling: lowercase, digits and underscores (e.g., "room",
    /// "assembly_station"). This is the durable identity — a tenant that already has the key
    /// adopts its own row.
    /// </summary>
    public required string Key { get; init; }

    /// <summary>Display name (singular). Required for a non-catalog key.</summary>
    public string? DisplayName { get; init; }

    /// <summary>Display name (plural). Required for a non-catalog key.</summary>
    public string? DisplayNamePlural { get; init; }

    /// <summary>Optional description.</summary>
    public string? Description { get; init; }

    /// <summary>Lucide icon name; "Box" when unset.</summary>
    public string? Icon { get; init; }

    /// <summary>Whether resources of this type are placed on a floorplan.</summary>
    public bool HasGeometry { get; init; }

    /// <summary>Whether resources of this type carry a directory profile (people).</summary>
    public bool HasDirectoryProfile { get; init; }

    /// <summary>Whether a resource of this type sits in at most one group.</summary>
    public bool SingleGroupMembership { get; init; }
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

    /// <summary>
    /// Keys of the resource types (from <see cref="PresetContents.ResourceTypes"/>) this
    /// criterion applies to. A criterion with no applicability is defined but appears on no
    /// resource form. Additive on re-apply: never removes an applicability the tenant added.
    /// </summary>
    public List<string> ResourceTypeKeys { get; init; } = new();
}

/// <summary>
/// A resource group definition within a preset.
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

    /// <summary>
    /// Key of the resource type (from <see cref="PresetContents.ResourceTypes"/>) the group
    /// holds. Null keeps the 1.0.0 behaviour: the tenant's placeable type, space first.
    /// </summary>
    public string? ResourceTypeKey { get; init; }
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

/// <summary>
/// A named sample resource within a preset — one room, person, machine or tool.
/// </summary>
public record PresetResource
{
    /// <summary>
    /// Logical key within the preset (e.g., "machine-hall"). Idempotent application maps it
    /// to the created row, so a re-apply updates rather than duplicates.
    /// </summary>
    public required string Key { get; init; }

    /// <summary>Display name. The tenant's after the first apply: a re-apply never renames.</summary>
    public required string Name { get; init; }

    /// <summary>Optional short code (e.g., "HALL-1"); unique per type within the preset.</summary>
    public string? Code { get; init; }

    /// <summary>Optional description.</summary>
    public string? Description { get; init; }

    /// <summary>Key of the resource type (from <see cref="PresetContents.ResourceTypes"/>).</summary>
    public required string TypeKey { get; init; }

    /// <summary>
    /// "Exclusive" or "Fractional". Unset means Fractional for a directory type (a person
    /// splits their time) and Exclusive for everything else.
    /// </summary>
    public string? AllocationMode { get; init; }

    /// <summary>
    /// Keys of the groups (from <see cref="PresetContents.SpaceGroups"/>) this resource belongs
    /// to. Each group must hold the same type as the resource.
    /// </summary>
    public List<string> GroupKeys { get; init; } = new();

    /// <summary>Capabilities: criterion key → value.</summary>
    public List<PresetCapability> Capabilities { get; init; } = new();
}

/// <summary>
/// A capability binding a criterion to a value on a sample resource.
/// </summary>
public record PresetCapability
{
    /// <summary>Reference to a criterion by its logical key.</summary>
    public required string CriterionKey { get; init; }

    /// <summary>The value as a JSON string, the same rule as <see cref="PresetTemplateItem.Value"/>.</summary>
    public required string Value { get; init; }
}
