namespace Api.Models.Preset;

/// <summary>
/// Result of preset validation.
/// </summary>
public record PresetValidationResult(bool IsValid, List<string> Errors)
{
    public static PresetValidationResult Success() => new(true, new List<string>());

    public static PresetValidationResult Failure(string error) => new(false, new List<string> { error });

    public static PresetValidationResult Failure(IEnumerable<string> errors) => new(false, errors.ToList());
}
