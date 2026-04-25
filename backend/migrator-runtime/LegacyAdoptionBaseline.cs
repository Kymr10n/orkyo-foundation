using System.Text.Json;
using System.Text.Json.Serialization;
using Orkyo.Migrations.Abstractions;

namespace Orkyo.Migrator;

/// <summary>
/// Parses a legacy-adoption baseline JSON file and exposes the per-target id set the
/// runner should mark as already-applied during cutover from a hand-rolled migrator.
/// </summary>
/// <remarks>
/// Expected shape:
/// <code>
/// {
///   "applied_by_version": "legacy-adoption-2026-04-25",
///   "controlplane": ["1000.foundation.update_updated_at_fn", ...],
///   "tenant":       ["1100.foundation.update_updated_at_fns", ...]
/// }
/// </code>
/// Any id not in either list stays pending and will be applied normally on the next
/// <c>migrate</c> run. Re-running with the same baseline is a no-op.
/// </remarks>
public sealed class LegacyAdoptionBaseline
{
    public string? AppliedByVersion { get; init; }
    public IReadOnlySet<string> ControlPlaneIds { get; init; } = new HashSet<string>(StringComparer.Ordinal);
    public IReadOnlySet<string> TenantIds { get; init; } = new HashSet<string>(StringComparer.Ordinal);

    public static LegacyAdoptionBaseline LoadFromFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"Legacy adoption baseline file not found: {path}", path);
        }

        var json = File.ReadAllText(path);
        BaselineDto dto;
        try
        {
            dto = JsonSerializer.Deserialize<BaselineDto>(json, JsonOptions)
                ?? throw new InvalidOperationException("Baseline JSON deserialised to null.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Failed to parse legacy adoption baseline at '{path}': {ex.Message}", ex);
        }

        return new LegacyAdoptionBaseline
        {
            AppliedByVersion = dto.AppliedByVersion,
            ControlPlaneIds = new HashSet<string>(dto.ControlPlane ?? Array.Empty<string>(), StringComparer.Ordinal),
            TenantIds = new HashSet<string>(dto.Tenant ?? Array.Empty<string>(), StringComparer.Ordinal),
        };
    }

    public IReadOnlySet<string> IdsFor(MigrationTargetDatabase target) => target switch
    {
        MigrationTargetDatabase.ControlPlane => ControlPlaneIds,
        MigrationTargetDatabase.Tenant       => TenantIds,
        _ => throw new ArgumentOutOfRangeException(nameof(target), target, "Unknown migration target."),
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private sealed class BaselineDto
    {
        [JsonPropertyName("applied_by_version")] public string? AppliedByVersion { get; set; }
        [JsonPropertyName("controlplane")]       public string[]? ControlPlane { get; set; }
        [JsonPropertyName("tenant")]             public string[]? Tenant { get; set; }
    }
}
