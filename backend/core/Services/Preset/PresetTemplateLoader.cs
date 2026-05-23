using System.Reflection;
using System.Text.Json;
using Api.Models.Preset;

namespace Api.Services;

/// <summary>
/// Shared loader for preset JSON files identified by <see cref="StarterTemplateCatalog"/> keys.
/// Composition layers can provide file-system and embedded-resource locations while reusing
/// one deserialization and key-to-file mapping implementation.
/// </summary>
public static class PresetTemplateLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static Preset LoadPreset(
        string templateKey,
        string presetsDirectory,
        Assembly resourceAssembly)
    {
        if (!StarterTemplateCatalog.TryGetPresetFileName(templateKey, out var fileName))
            throw new ArgumentException($"No preset file for template: {templateKey}", nameof(templateKey));

        var resolvedFileName = fileName!;
        var filePath = Path.Combine(presetsDirectory, resolvedFileName);

        if (File.Exists(filePath))
        {
            var json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<Preset>(json, JsonOptions)
                ?? throw new InvalidOperationException($"Failed to deserialize preset: {resolvedFileName}");
        }

        var resourceName = resourceAssembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(resolvedFileName, StringComparison.Ordinal));

        if (resourceName == null)
            throw new FileNotFoundException($"Preset not found: {resolvedFileName}");

        using var stream = resourceAssembly.GetManifestResourceStream(resourceName)!;
        return JsonSerializer.Deserialize<Preset>(stream, JsonOptions)
            ?? throw new InvalidOperationException($"Failed to deserialize embedded preset: {resolvedFileName}");
    }
}
