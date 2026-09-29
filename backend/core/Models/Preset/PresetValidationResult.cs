namespace Api.Models.Preset;

/// <summary>
/// Result of preset validation.
/// </summary>
public record PresetValidationResult(bool IsValid, List<string> Errors);
