namespace Orkyo.Foundation.Seed.Scales;

/// <summary>How large a seeded workspace is.</summary>
/// <param name="Requests">
/// Sizes the unscheduled backlog only. On the demo path, volume is a consequence of the
/// utilization targets and the roster.
/// </param>
public sealed record ScaleSpec(string Slug, int People, int Departments, int JobTitles, int Requests);

public static class ScaleCatalog
{
    public static readonly IReadOnlyDictionary<string, ScaleSpec> All = new ScaleSpec[]
    {
        new("tiny", People: 15, Departments: 3, JobTitles: 10, Requests: 100),
        new("small", People: 60, Departments: 8, JobTitles: 20, Requests: 500),
        // People is balanced against the EQUIPMENT, because utilization is a ratio and the
        // machine/tool catalog is fixed. Measured over the seeded year
        // (NarrativeSeed_FillsTheShop_ToARealisticUtilization): 48 people → 83 % of people busy
        // but only 55 % of stations; 72 → 79 % and 71 %; 84 → 78 % and 80 % at 21k requests. 72
        // keeps both high with the shop's people as the mild bottleneck, which is the honest shape
        // for a job shop and leaves the slack the conflict demos need. Move this only together
        // with MachineCatalog and the facilities' tool counts.
        new("medium", People: 72, Departments: 20, JobTitles: 40, Requests: 4_000),
        new("large", People: 600, Departments: 30, JobTitles: 60, Requests: 6_000),
        new("xlarge", People: 1_500, Departments: 50, JobTitles: 100, Requests: 15_000),
    }.ToDictionary(s => s.Slug, StringComparer.OrdinalIgnoreCase);

    public static ScaleSpec Resolve(string slug) =>
        All.TryGetValue(slug, out var s)
            ? s
            : throw new ArgumentException(
                $"Unknown scale '{slug}'. Expected one of: {string.Join(", ", All.Keys)}.");
}
