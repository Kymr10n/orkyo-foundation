namespace Api.Constants;

/// <summary>
/// Keys of the <em>system</em> resource types seeded by migration 1300. Tenants may define
/// additional types at runtime, so this set is NOT the universe of valid resource type keys —
/// resolve arbitrary keys through <c>IResourceTypeRepository</c> instead.
/// </summary>
public static class ResourceTypeKeys
{
    public const string Space = "space";
    public const string Person = "person";
    public const string Tool = "tool";

    /// <summary>System type keys, in canonical (alphabetical) order.</summary>
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Person,
        Space,
        Tool,
    };

    /// <summary>True when the key belongs to a system type. Does not imply the key is invalid
    /// when false — user-defined types exist only in the database.</summary>
    public static bool IsKnown(string? key) => key is not null && All.Contains(key);
}
