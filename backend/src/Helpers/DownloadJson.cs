using System.Text.Json;

namespace Api.Helpers;

/// <summary>
/// The serializer options for the two human-facing download payloads (export and preset
/// export). Indented and camelCase so the file a person opens reads the way the API does.
/// Held once: <see cref="JsonSerializerOptions"/> caches its resolved metadata, so a new
/// instance per request re-pays that cost.
/// </summary>
public static class DownloadJson
{
    /// <summary>Indented camelCase options for <c>Results.Json</c> download bodies.</summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
}
