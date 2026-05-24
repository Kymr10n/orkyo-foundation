namespace Orkyo.Foundation.Seed.Profiles;

public interface IProfile
{
    string Slug { get; }
    string DisplayName { get; }

    /// <summary>Candidate site names. Picker rotates / shuffles as needed.</summary>
    IReadOnlyList<string> SiteNamePool { get; }

    /// <summary>Format string template for space names, e.g. "Cell {0}".</summary>
    string SpaceNameTemplate { get; }

    /// <summary>Candidate job titles for person resources.</summary>
    IReadOnlyList<string> JobTitlePool { get; }

    /// <summary>Top-level department names (children generated automatically).</summary>
    IReadOnlyList<string> DepartmentRootPool { get; }

    /// <summary>Candidate resource-group names (space groups).</summary>
    IReadOnlyList<string> ResourceGroupPool { get; }

    /// <summary>Candidate people-group names (team / crew names).</summary>
    IReadOnlyList<string> PersonGroupPool { get; }

    /// <summary>Request name template parts. Combined to produce realistic-looking task names.</summary>
    IReadOnlyList<string> RequestNameVerbs { get; }
    IReadOnlyList<string> RequestNameNouns { get; }
}

public static class ProfileCatalog
{
    public static readonly IReadOnlyDictionary<string, IProfile> All =
        new Dictionary<string, IProfile>(StringComparer.OrdinalIgnoreCase)
        {
            ["generic"] = new Generic(),
            ["manufacturing"] = new Manufacturing(),
            ["construction"] = new Construction(),
            ["camping"] = new Camping(),
            ["education"] = new Education(),
        };

    public static IProfile Resolve(string slug) =>
        All.TryGetValue(slug, out var p)
            ? p
            : throw new ArgumentException(
                $"Unknown profile '{slug}'. Expected one of: {string.Join(", ", All.Keys)}.");
}
