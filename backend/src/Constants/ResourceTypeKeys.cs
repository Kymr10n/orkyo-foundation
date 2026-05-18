namespace Api.Constants;

public static class ResourceTypeKeys
{
    public const string Space = "space";
    public const string Person = "person";
    public const string Tool = "tool";

    /// <summary>Known keys, in canonical (alphabetical) order.</summary>
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Person,
        Space,
        Tool,
    };

    public static bool IsKnown(string? key) => key is not null && All.Contains(key);
}
