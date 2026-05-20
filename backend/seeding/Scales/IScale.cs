namespace Orkyo.Foundation.Seed.Scales;

public interface IScale
{
    string Slug { get; }
    int Sites { get; }
    int SpacesPerSite { get; }
    int People { get; }
    int Departments { get; }
    int JobTitles { get; }
    int ResourceGroups { get; }
    int Criteria { get; }
    int Templates { get; }
    int Requests { get; }
    /// <summary>Time window in days (split half before / half after reference date).</summary>
    int TimeWindowDays { get; }
}

public static class ScaleCatalog
{
    public static readonly IReadOnlyDictionary<string, IScale> All =
        new Dictionary<string, IScale>(StringComparer.OrdinalIgnoreCase)
        {
            ["tiny"] = new Tiny(),
            ["small"] = new Small(),
            ["medium"] = new Medium(),
            ["large"] = new Large(),
            ["xlarge"] = new XLarge(),
        };

    public static IScale Resolve(string slug) =>
        All.TryGetValue(slug, out var s)
            ? s
            : throw new ArgumentException(
                $"Unknown scale '{slug}'. Expected one of: {string.Join(", ", All.Keys)}.");
}
