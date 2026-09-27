namespace Orkyo.Foundation.Seed.Profiles;

public interface IProfile
{
    string Slug { get; }

    /// <summary>Candidate job titles for person resources.</summary>
    IReadOnlyList<string> JobTitlePool { get; }

    /// <summary>Top-level department names (children generated automatically).</summary>
    IReadOnlyList<string> DepartmentRootPool { get; }

    /// <summary>
    /// Second-level department names, combined with a root ("Production" + "Machining").
    /// </summary>
    /// <remarks>
    /// Profile-specific because the child names are what make the tree read as a real
    /// organization. A compass point works anywhere and means nothing anywhere.
    /// </remarks>
    IReadOnlyList<string> DepartmentChildPool { get; }
}

public static class ProfileCatalog
{
    public static readonly IReadOnlyDictionary<string, IProfile> All =
        new Dictionary<string, IProfile>(StringComparer.OrdinalIgnoreCase)
        {
            ["manufacturing"] = new Manufacturing(),
        };

    public static IProfile Resolve(string slug) =>
        All.TryGetValue(slug, out var p)
            ? p
            : throw new ArgumentException(
                $"Unknown profile '{slug}'. Expected one of: {string.Join(", ", All.Keys)}.");
}
