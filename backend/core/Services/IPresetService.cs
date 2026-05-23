using Api.Models.Preset;

namespace Api.Services;

/// <summary>
/// Manages configuration presets — bundles of sites, spaces, and settings that can be
/// applied to bootstrap a new tenant or exported from an existing one.
/// </summary>
public interface IPresetService
{
    /// <summary>Validates a preset without applying it. Returns a report of errors and warnings.</summary>
    Task<PresetValidationResult> ValidateAsync(Preset preset, CancellationToken ct = default);

    /// <summary>
    /// Applies a preset, creating or updating sites, spaces, and settings.
    /// Idempotent on named entities — re-applying a preset updates rather than duplicates.
    /// </summary>
    Task<PresetApplicationResult> ApplyAsync(Preset preset, Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Exports the current state of the given preset ID as a portable preset document.
    /// Useful for capturing a tenant's configuration to seed other deployments.
    /// </summary>
    Task<Preset> ExportAsync(string presetId, string name, string? description = null, CancellationToken ct = default);

    /// <summary>Returns the history of all preset applications for the current tenant.</summary>
    Task<List<PresetApplication>> GetApplicationsAsync(CancellationToken ct = default);
}

/// <summary>Result of a <see cref="IPresetService.ApplyAsync"/> call.</summary>
public record PresetApplicationResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public PresetApplicationStats Stats { get; init; } = new();
}
