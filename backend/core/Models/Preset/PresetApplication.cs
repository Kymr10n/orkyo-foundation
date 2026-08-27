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
