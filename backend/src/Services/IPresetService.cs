using Api.Models.Preset;

namespace Api.Services;

public interface IPresetService
{
    Task<PresetValidationResult> ValidateAsync(Preset preset);
    Task<PresetApplicationResult> ApplyAsync(Preset preset, Guid userId);
    Task<Preset> ExportAsync(string presetId, string name, string? description = null);
    Task<List<PresetApplication>> GetApplicationsAsync();
}

public record PresetApplicationResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public PresetApplicationStats Stats { get; init; } = new();
}


